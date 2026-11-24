import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, filter, map, of, startWith, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import {
  CauseOfLossCode,
  PartyInput,
  PolicySummary,
  RESERVE_COMPONENTS,
  ReserveComponentType,
  RiskObjectInput,
} from '../../core/api/models';
import { CLOCK } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/ui/notification.service';
import { AuthorityIndicator } from '../../shared/authority-indicator';
import { approvalLevelFor } from '../../shared/authority';
import { ConfirmService } from '../../shared/confirm-dialog';
import { MoneyPipe } from '../../shared/money.pipe';
import { labelOf } from '../../shared/status-badge';
import {
  errorsByStep,
  fnolWarnings,
  hasClaimant,
  isInFuture,
  isLossInForce,
  localDateTimeToIso,
  partyName,
  toCreateRequest,
  toLocalDateTimeValue,
} from './fnol-model';
import { PartyForm } from '../../shared/party-form';
import { RiskObjectForm } from './risk-object-form';

export const MIN_DESCRIPTION_LENGTH = 20;

/**
 * FNOL intake (P6a task 39, S11.2): a linear three-step reactive form. Step 1 policy and loss details,
 * step 2 parties and risk objects (a Claimant is required), step 3 optional initial reserve and review.
 * Server validation errors are shown at the top of the step they belong to.
 */
@Component({
  selector: 'app-fnol-intake',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatStepperModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatAutocompleteModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    AuthorityIndicator,
    MoneyPipe,
    PartyForm,
    RiskObjectForm,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './fnol-intake.html',
  styleUrl: './fnol-intake.scss',
})
export class FnolIntake {
  private readonly api = inject(ClaimsApiService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly now = inject(CLOCK);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly stepper = viewChild.required(MatStepper);

  /** One key per intake: a resubmit after a lost response replays the first result instead of a second claim. */
  private readonly idempotencyKey = crypto.randomUUID();

  protected readonly components = RESERVE_COMPONENTS;
  protected readonly label = labelOf;
  protected readonly partyName = partyName;
  protected readonly minDescription = MIN_DESCRIPTION_LENGTH;
  protected readonly maxLossDate = toLocalDateTimeValue(new Date(this.now()));

  // ------------------------------------------------------------ step 1

  protected readonly step1 = this.fb.group(
    {
      unknownPolicy: [false],
      policy: this.fb.control<PolicySummary | string>(''),
      lossDate: ['', [Validators.required, this.notInFuture()]],
      causeOfLossCode: ['', Validators.required],
      lossDescription: ['', [Validators.required, Validators.minLength(MIN_DESCRIPTION_LENGTH), Validators.maxLength(4000)]],
      lossLocation: ['', Validators.maxLength(500)],
      estimatedLossAmount: this.fb.control<number | null>(null, Validators.min(0)),
    },
    { validators: policyChosenOrUnknown },
  );

  protected readonly causeCodes = toSignal(
    this.api.causeOfLossCodes().pipe(catchError(() => of<CauseOfLossCode[]>([]))),
    { initialValue: [] as CauseOfLossCode[] },
  );

  protected readonly policyOptions = toSignal(
    this.step1.controls.policy.valueChanges.pipe(
      filter((v): v is string => typeof v === 'string'),
      map((v) => v.trim()),
      debounceTime(300),
      distinctUntilChanged(),
      switchMap((term) => (term.length < 2 ? of([]) : this.api.searchPolicies(term).pipe(catchError(() => of([]))))),
    ),
    { initialValue: [] as PolicySummary[] },
  );

  private readonly step1Value = toSignal(this.step1.valueChanges.pipe(startWith(null), map(() => this.step1.getRawValue())), {
    requireSync: true,
  });

  protected readonly selectedPolicy = computed(() => {
    const v = this.step1Value();
    return !v.unknownPolicy && typeof v.policy === 'object' ? v.policy : null;
  });
  protected readonly lossDateIso = computed(() => localDateTimeToIso(this.step1Value().lossDate));
  protected readonly inForce = computed(() => isLossInForce(this.selectedPolicy(), this.lossDateIso()));
  protected readonly unknownPolicy = computed(() => this.step1Value().unknownPolicy);
  protected readonly descriptionLength = computed(() => this.step1Value().lossDescription.trim().length);

  // ------------------------------------------------------------ step 2

  protected readonly parties = signal<PartyInput[]>([]);
  protected readonly riskObjects = signal<RiskObjectInput[]>([]);
  protected readonly addingParty = signal(true);
  protected readonly addingRisk = signal(false);
  protected readonly hasClaimant = computed(() => hasClaimant(this.parties()));

  // ------------------------------------------------------------ step 3

  protected readonly reserve = this.fb.group({
    include: [false],
    componentType: ['Indemnity' as ReserveComponentType],
    amount: this.fb.control<number | null>(null),
    changeReason: ['Initial estimate at FNOL', Validators.maxLength(500)],
  });
  private readonly reserveValue = toSignal(this.reserve.valueChanges.pipe(startWith(null), map(() => this.reserve.getRawValue())), {
    requireSync: true,
  });
  protected readonly includeReserve = computed(() => this.reserveValue().include && !this.unknownPolicy());
  protected readonly reserveAmount = computed(() => this.reserveValue().amount);
  protected readonly approvalLevel = computed(() => {
    const amount = this.reserveAmount();
    return amount ? approvalLevelFor(amount) : null;
  });

  protected readonly warnings = computed(() =>
    fnolWarnings({
      unknownPolicy: this.unknownPolicy(),
      policy: this.selectedPolicy(),
      lossDateIso: this.lossDateIso(),
      parties: this.parties(),
      riskObjects: this.riskObjects(),
    }),
  );

  protected readonly submitting = signal(false);
  protected readonly stepErrors = signal<string[][]>([[], [], []]);

  constructor() {
    this.reserve.controls.include.valueChanges.pipe(takeUntilDestroyed()).subscribe((include) => {
      const { amount, changeReason } = this.reserve.controls;
      amount.setValidators(include ? [Validators.required, this.reserveAmountValidator()] : []);
      changeReason.setValidators(include ? [Validators.required, Validators.pattern(/\S/), Validators.maxLength(500)] : []);
      amount.updateValueAndValidity();
      changeReason.updateValueAndValidity();
    });
    // No policy, no reserve (BR-C-06, D-20): the reserve section is switched off while "Unknown policy" is on.
    this.step1.controls.unknownPolicy.valueChanges.pipe(takeUntilDestroyed()).subscribe((unknown) => {
      const include = this.reserve.controls.include;
      if (unknown) {
        include.setValue(false);
        include.disable();
      } else {
        include.enable();
      }
    });
    this.reserve.controls.componentType.valueChanges.pipe(takeUntilDestroyed()).subscribe(() =>
      this.reserve.controls.amount.updateValueAndValidity(),
    );
  }

  protected displayPolicy(value: PolicySummary | string | null): string {
    return value && typeof value === 'object' ? `${value.policyNumber} · ${value.clientName}` : (value ?? '');
  }

  protected addParty(party: PartyInput): void {
    this.parties.update((list) => [...list, party]);
    this.addingParty.set(false);
  }

  protected removeParty(index: number): void {
    this.parties.update((list) => list.filter((_, i) => i !== index));
  }

  protected addRiskObject(riskObject: RiskObjectInput): void {
    this.riskObjects.update((list) => [...list, riskObject]);
    this.addingRisk.set(false);
  }

  protected removeRiskObject(index: number): void {
    this.riskObjects.update((list) => {
      const rest = list.filter((_, i) => i !== index);
      // Keep one primary risk object when the primary one is removed.
      return rest.length > 0 && !rest.some((r) => r.isPrimary) ? [{ ...rest[0], isPrimary: true }, ...rest.slice(1)] : rest;
    });
  }

  protected canSubmit(): boolean {
    return this.step1.valid && this.hasClaimant() && (!this.includeReserve() || this.reserve.valid) && !this.submitting();
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      this.step1.markAllAsTouched();
      this.reserve.markAllAsTouched();
      return;
    }
    const warnings = this.warnings();
    if (warnings.length === 0) {
      this.create();
      return;
    }
    this.confirm
      .confirm({
        title: 'Create the claim with warnings?',
        message: 'These warnings do not block creation. They are recorded on the claim and must be acknowledged before it is opened.',
        items: warnings,
        confirmLabel: 'Create claim',
      })
      .subscribe((result) => {
        if (result) {
          this.create();
        }
      });
  }

  private create(): void {
    const s1 = this.step1.getRawValue();
    const r = this.reserve.getRawValue();
    const request = toCreateRequest({
      policy: typeof s1.policy === 'object' ? s1.policy : null,
      unknownPolicy: s1.unknownPolicy,
      lossDateIso: this.lossDateIso()!,
      causeOfLossCode: s1.causeOfLossCode,
      lossDescription: s1.lossDescription,
      lossLocation: s1.lossLocation,
      estimatedLossAmount: s1.estimatedLossAmount,
      parties: this.parties(),
      riskObjects: this.riskObjects(),
      initialReserve:
        this.includeReserve() && r.amount !== null
          ? { componentType: r.componentType, amount: r.amount, changeReason: r.changeReason.trim() }
          : null,
    });

    this.submitting.set(true);
    this.stepErrors.set([[], [], []]);
    this.api.create(request, { idempotencyKey: this.idempotencyKey, inlineErrors: true }).subscribe({
      next: (result) => {
        const pending = result.initialReserve?.approvalStatus === 'PendingApproval';
        this.notifications.success(
          `Claim ${result.claimNumber} created.${pending ? ` The initial reserve awaits ${result.initialReserve!.requiredApprovalLevel.toLowerCase()} approval.` : ''}`,
        );
        void this.router.navigate(['/claims', result.claimId]);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        const byStep = errorsByStep(toApiError(error));
        this.stepErrors.set(byStep);
        const first = byStep.findIndex((messages) => messages.length > 0);
        if (first >= 0 && first < 2) {
          this.stepper().selectedIndex = first;
        }
      },
    });
  }

  // ------------------------------------------------------------ validators

  private notInFuture(): ValidatorFn {
    return (control: AbstractControl<string>): ValidationErrors | null =>
      isInFuture(localDateTimeToIso(control.value), this.now()) ? { future: true } : null;
  }

  private reserveAmountValidator(): ValidatorFn {
    return (control: AbstractControl<number | null>): ValidationErrors | null => {
      const amount = control.value;
      if (amount === null) {
        return null;
      }
      const negativeAllowed = this.reserve?.controls.componentType.value === 'SubrogationRecoverable';
      return (negativeAllowed ? amount !== 0 : amount > 0) ? null : { amount: true };
    };
  }
}

/** Step 1 needs a selected policy unless "Unknown policy" is on (S11.2). */
const policyChosenOrUnknown: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const unknown = group.get('unknownPolicy')?.value as boolean;
  const policy = group.get('policy')?.value as unknown;
  return unknown || (policy !== null && typeof policy === 'object') ? null : { policyRequired: true };
};
