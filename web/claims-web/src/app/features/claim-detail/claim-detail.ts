import { ClipboardModule } from '@angular/cdk/clipboard';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { ClaimDetail as ClaimDetailDto, DemoUser, TransitionOption } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/ui/notification.service';
import { ConfirmService } from '../../shared/confirm-dialog';
import { MoneyPipe } from '../../shared/money.pipe';
import { Badge, labelOf } from '../../shared/status-badge';
import { AuditTab } from './audit-tab';
import { DocumentsTab } from './documents-tab';
import { ReservesTab } from './reserves-tab';
import { blockedReason, planTransition } from './transition-plan';

/**
 * Claim detail, minimal (P6a task 40; S11.3): the header with the transition menu, and the Reserves,
 * Documents and Audit Log tabs. Overview and Parties tabs come in P6b.
 */
@Component({
  selector: 'app-claim-detail',
  imports: [
    DatePipe,
    RouterLink,
    ClipboardModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatTabsModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    Badge,
    MoneyPipe,
    ReservesTab,
    DocumentsTab,
    AuditTab,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claim-detail.html',
  styleUrl: './claim-detail.scss',
})
export class ClaimDetail implements OnInit {
  /** Route parameter (component input binding). */
  readonly claimId = input.required<string>();

  private readonly api = inject(ClaimsApiService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);

  protected readonly claim = signal<ClaimDetailDto | null>(null);
  protected readonly transitions = signal<TransitionOption[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly transitioning = signal(false);
  /** Bumped after every change, so the tabs reload (audit entries, balances). */
  protected readonly version = signal(0);
  protected readonly label = labelOf;
  protected readonly blockedReason = blockedReason;

  /** User id → display name, from the demo directory (the audit log and history carry ids). */
  protected readonly userNames = toSignal(
    this.auth.users().pipe(
      map((users: DemoUser[]) => new Map(users.map((u) => [u.userId, u.displayName]))),
      catchError(() => of(new Map<string, string>())),
    ),
    { initialValue: new Map<string, string>() },
  );

  protected readonly openIssues = computed(() => (this.claim()?.validationIssues ?? []).filter((i) => i.status === 'Open'));

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    forkJoin({ claim: this.api.get(this.claimId()), transitions: this.api.transitions(this.claimId()) }).subscribe({
      next: ({ claim, transitions }) => {
        this.claim.set(claim);
        this.transitions.set(transitions);
        this.loadError.set(null);
        this.version.update((v) => v + 1);
      },
      error: (error: unknown) => {
        if (!this.claim()) {
          this.loadError.set(toApiError(error).title);
        }
      },
    });
  }

  protected copied(): void {
    this.notifications.info(`Copied ${this.claim()?.claimNumber}`);
  }

  protected startTransition(option: TransitionOption): void {
    const plan = planTransition(option);
    this.confirm.confirm(plan.dialog).subscribe((result) => {
      if (!result) {
        return;
      }
      this.transitioning.set(true);
      this.api.transition(this.claimId(), plan.toRequest(result.text)).subscribe({
        next: (changed) => {
          this.transitioning.set(false);
          this.notifications.success(`Claim is now ${labelOf(changed.status)}.`);
          this.load();
        },
        error: () => {
          this.transitioning.set(false);
          this.load();
        },
      });
    });
  }
}
