import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { catchError, EMPTY, map, Observable, startWith, Subscription, switchMap, takeWhile, timer } from 'rxjs';
import { errorMessages, toApiError } from '../../core/api/api-error';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import {
  ClaimDetail,
  ClaimReserves,
  ClaimStatus,
  RESERVE_COMPONENTS,
  ReserveComponentType,
  ReserveTransaction,
  ReserveTransactionType,
} from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/ui/notification.service';
import { AuthorityIndicator } from '../../shared/authority-indicator';
import { ConfirmService } from '../../shared/confirm-dialog';
import { formatMoney, MoneyPipe } from '../../shared/money.pipe';
import { APPROVAL_STATUS_TONE, Badge, BadgeTone, labelOf, POSTING_STATUS_TONE } from '../../shared/status-badge';

/** Statuses in which reserves can change (D-20). */
const RESERVE_STATUSES: readonly ClaimStatus[] = ['Draft', 'Open', 'UnderInvestigation', 'PendingPayment'];
export const POSTING_POLL_MS = 2000;
const POSTING_POLL_LIMIT = 30;

/** Pure: the reserve change the panel would submit, and its effect on the balance (D-16). */
export function reserveDelta(type: ReserveTransactionType, amount: number | null, currentBalance: number): number | null {
  switch (type) {
    case 'Add':
      return amount;
    case 'Adjust':
      return amount === null ? null : amount - currentBalance;
    case 'Reverse':
      return -currentBalance;
  }
}

/** Add opens a component that has no reserve yet; an existing one is adjusted or reversed (D-16). */
export function transactionTypesFor(claim: ClaimDetail, component: ReserveComponentType): ReserveTransactionType[] {
  return claim.reserves.components.some((c) => c.componentType === component) ? ['Adjust', 'Reverse'] : ['Add'];
}

/** A transaction whose GL posting is still to come (approved, posting Pending). */
export function awaitingPosting(t: ReserveTransaction): boolean {
  return (t.approvalStatus === 'Approved' || t.approvalStatus === 'AutoApproved') && t.postingStatus === 'Pending';
}

/**
 * Reserves tab (S11.3): a card per component, the transaction history with approval and GL posting status,
 * approve / reject for supervisors and managers, retract for the submitter, and the "Add reserve" slide-in
 * panel with the authority indicator. While a posting is pending the history refreshes itself.
 */
@Component({
  selector: 'app-reserves-tab',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatSidenavModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatTooltipModule,
    MoneyPipe,
    Badge,
    AuthorityIndicator,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reserves-tab.html',
  styleUrl: './reserves-tab.scss',
})
export class ReservesTab {
  readonly claim = input.required<ClaimDetail>();
  readonly userNames = input(new Map<string, string>());
  /** Bumped by the page after any change; reloads the history. */
  readonly version = input(0);
  readonly changed = output<void>();

  private readonly api = inject(ClaimsApiService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly components = RESERVE_COMPONENTS;
  protected readonly label = labelOf;
  protected approvalToneOf(t: ReserveTransaction): BadgeTone {
    return APPROVAL_STATUS_TONE[t.approvalStatus];
  }
  protected postingToneOf(t: ReserveTransaction): BadgeTone {
    return POSTING_STATUS_TONE[t.postingStatus];
  }
  protected readonly columns = ['date', 'type', 'component', 'amount', 'status', 'submittedBy', 'approvedBy', 'posting', 'actions'];

  protected readonly reserves = signal<ClaimReserves | null>(null);
  protected readonly busy = signal<string | null>(null);
  private poll: Subscription | null = null;

  protected readonly isSupervisor = computed(() => this.auth.hasRole('Supervisor'));
  protected readonly myUserId = computed(() => this.auth.session()?.userId);
  protected readonly canChange = computed(() => {
    const c = this.claim();
    if (!c.policyId) return 'Link a policy first (BR-C-06).';
    if (!RESERVE_STATUSES.includes(c.status)) return `Reserves can't change while the claim is ${labelOf(c.status)}.`;
    return null;
  });

  // ------------------------------------------------------------ add panel

  protected readonly panelOpen = signal(false);
  protected readonly panelError = signal<string[]>([]);
  protected readonly submitting = signal(false);
  private panelKey = crypto.randomUUID();

  protected readonly form = this.fb.group({
    component: ['Indemnity' as ReserveComponentType],
    transactionType: ['Add' as ReserveTransactionType],
    amount: this.fb.control<number | null>(null),
    changeReason: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(500)]],
  });
  protected readonly formValue = toSignal(this.form.valueChanges.pipe(startWith(null), map(() => this.form.getRawValue())), {
    requireSync: true,
  });

  protected readonly selectedComponent = computed(() =>
    this.claim().reserves.components.find((c) => c.componentType === this.formValue().component),
  );
  protected readonly typeOptions = computed(() => transactionTypesFor(this.claim(), this.formValue().component));
  protected readonly pendingOnComponent = computed(() => !!this.selectedComponent()?.pendingTransactionId);
  protected readonly delta = computed(() => {
    const v = this.formValue();
    return reserveDelta(v.transactionType, v.amount, this.selectedComponent()?.currentAmount ?? 0);
  });

  constructor() {
    toObservable(this.version)
      .pipe(
        switchMap(() => this.load()),
        takeUntilDestroyed(),
      )
      .subscribe();

    // Use the emitted value: the control emits before the form, so formValue() still holds the old component here.
    this.form.controls.component.valueChanges.pipe(takeUntilDestroyed()).subscribe((component) => {
      this.form.controls.transactionType.setValue(transactionTypesFor(this.claim(), component)[0]);
    });
    this.form.controls.transactionType.valueChanges.pipe(takeUntilDestroyed()).subscribe((type) => {
      const amount = this.form.controls.amount;
      if (type === 'Reverse') {
        amount.setValue(null);
        amount.disable();
      } else {
        amount.enable();
      }
      amount.setValidators(type === 'Reverse' ? [] : [Validators.required, this.amountValidator()]);
      amount.updateValueAndValidity();
    });
    this.form.controls.amount.setValidators([Validators.required, this.amountValidator()]);
  }

  protected userName(id: string | null): string {
    return id ? (this.userNames().get(id) ?? 'Unknown user') : '—';
  }

  // ------------------------------------------------------------ approval workflow

  protected approve(t: ReserveTransaction): void {
    this.confirm
      .confirm({
        title: 'Approve reserve change?',
        items: [
          `${labelOf(t.componentType)}: ${formatMoney(t.previousBalance)} → ${formatMoney(t.newBalance)} (${formatMoney(t.amount, true)})`,
          `Reason: ${t.changeReason}`,
          `Submitted by ${t.submittedByName}`,
        ],
        confirmLabel: 'Approve',
      })
      .subscribe((ok) => ok && this.act(t, 'Approved', this.api.approveReserve(this.claim().claimId, t.transactionId)));
  }

  protected reject(t: ReserveTransaction): void {
    this.confirm
      .confirm({
        title: 'Reject reserve change?',
        items: [`${labelOf(t.componentType)}: ${formatMoney(t.amount, true)} by ${t.submittedByName}`],
        input: { label: 'Rejection reason', required: true, maxLength: 500 },
        confirmLabel: 'Reject',
        destructive: true,
      })
      .subscribe(
        (result) =>
          result && this.act(t, 'Rejected', this.api.rejectReserve(this.claim().claimId, t.transactionId, result.text ?? '')),
      );
  }

  protected retract(t: ReserveTransaction): void {
    this.confirm
      .confirm({ title: 'Retract your pending reserve change?', items: [`${labelOf(t.componentType)}: ${formatMoney(t.amount, true)}`], confirmLabel: 'Retract' })
      .subscribe((ok) => ok && this.act(t, 'Retracted', this.api.retractReserve(this.claim().claimId, t.transactionId)));
  }

  private act(t: ReserveTransaction, verb: string, call: Observable<unknown>): void {
    this.busy.set(t.transactionId);
    call.subscribe({
      next: () => {
        this.busy.set(null);
        this.notifications.success(`${verb}: ${labelOf(t.componentType)} ${formatMoney(t.amount, true)}.`);
        this.changed.emit();
      },
      error: () => {
        this.busy.set(null);
        this.changed.emit();
      },
    });
  }

  // ------------------------------------------------------------ add / adjust panel

  protected openPanel(): void {
    this.panelKey = crypto.randomUUID();
    this.panelError.set([]);
    // Resetting the component also picks the change type that fits it (Add for a new component, else Adjust).
    this.form.reset({ component: 'Indemnity', changeReason: '' });
    this.panelOpen.set(true);
  }

  protected submit(): void {
    if (this.form.invalid || this.pendingOnComponent()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.submitting.set(true);
    this.panelError.set([]);
    this.api
      .submitReserve(
        this.claim().claimId,
        { component: v.component, transactionType: v.transactionType, amount: v.transactionType === 'Reverse' ? null : v.amount, changeReason: v.changeReason.trim() },
        { idempotencyKey: this.panelKey, inlineErrors: true },
      )
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.panelOpen.set(false);
          const outcome =
            result.approvalStatus === 'PendingApproval'
              ? `awaits ${result.requiredApprovalLevel.toLowerCase()} approval`
              : 'was auto-approved';
          this.notifications.success(`${labelOf(result.componentType)} ${formatMoney(result.amount, true)} ${outcome}.`);
          for (const w of result.warnings) this.notifications.warning(w.message);
          this.changed.emit();
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          const body = toApiError(error);
          const messages = errorMessages(body);
          this.panelError.set(messages.length ? messages : [body.detail ? `${body.title} ${body.detail}` : body.title]);
        },
      });
  }

  private amountValidator() {
    return (control: { value: number | null }) => {
      if (control.value === null) return null;
      const negativeAllowed = this.form?.controls.component.value === 'SubrogationRecoverable';
      return (negativeAllowed ? control.value !== 0 : control.value > 0) ? null : { amount: true };
    };
  }

  // ------------------------------------------------------------ loading and GL posting refresh

  protected refresh(): void {
    this.load().subscribe();
  }

  private load(): Observable<void> {
    return this.api.reserves(this.claim().claimId).pipe(
      map((reserves) => {
        this.reserves.set(reserves);
        this.watchPostings(reserves);
      }),
      catchError(() => EMPTY),
    );
  }

  /** The GL job runs in the background (S12.1): poll until no approved change is waiting to post. */
  private watchPostings(reserves: ClaimReserves): void {
    if (this.poll || !reserves.history.some(awaitingPosting)) {
      return;
    }
    this.poll = timer(POSTING_POLL_MS, POSTING_POLL_MS)
      .pipe(
        switchMap((i) => this.api.reserves(this.claim().claimId).pipe(map((r) => ({ i, r })), catchError(() => EMPTY))),
        takeWhile(({ i, r }) => {
          this.reserves.set(r);
          return i < POSTING_POLL_LIMIT && r.history.some(awaitingPosting);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        complete: () => {
          this.poll = null;
        },
      });
  }
}
