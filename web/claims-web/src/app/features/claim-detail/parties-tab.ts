import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { errorMessages, toApiError } from '../../core/api/api-error';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { ClaimDetail, Party, PartyInput, PartyRole } from '../../core/api/models';
import { NotificationService } from '../../core/ui/notification.service';
import { ConfirmService } from '../../shared/confirm-dialog';
import { PartyForm } from '../../shared/party-form';
import { Badge, BadgeTone, labelOf } from '../../shared/status-badge';
import { isEditable } from './overview-tab';

export const ROLE_TONE: Readonly<Record<PartyRole, BadgeTone>> = {
  Claimant: 'blue',
  Insured: 'green',
  ThirdParty: 'orange',
  Witness: 'grey',
  Attorney: 'purple',
};

/**
 * Why a party can't be removed, or null when it can: the claim is closed/withdrawn, it is already removed,
 * or it is the last active Claimant (BR-P-01; the server answers 422 PARTY_LAST_CLAIMANT as well).
 */
export function removeBlockedReason(claim: ClaimDetail, party: Party): string | null {
  if (!isEditable(claim)) return `The claim is ${claim.status}.`;
  if (!party.isActive) return 'Already removed.';
  const activeClaimants = claim.parties.filter((p) => p.isActive && p.role === 'Claimant').length;
  if (party.role === 'Claimant' && activeClaimants <= 1) return 'The last Claimant can’t be removed (BR-P-01).';
  return null;
}

/**
 * Parties tab (S11.3 tab 2): role badge, name, contact details, active/inactive; "Add party" opens the
 * inline form; Remove is disabled for the last active Claimant. Removal is a soft delete (S9.3).
 */
@Component({
  selector: 'app-parties-tab',
  imports: [MatTableModule, MatButtonModule, MatIconModule, MatTooltipModule, Badge, PartyForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toolbar">
      @if (!adding()) {
        <span [matTooltip]="editable() ? '' : 'The claim is ' + claim().status + '.'">
          <button mat-flat-button (click)="openForm()" [disabled]="!editable()" data-testid="add-party">
            <mat-icon>person_add</mat-icon> Add party
          </button>
        </span>
      }
      <span class="count">{{ activeCount() }} active · {{ claim().parties.length - activeCount() }} removed</span>
    </div>
    @if (adding()) {
      <div class="form-row">
        <app-party-form [busy]="saving()" (added)="add($event)" (cancelled)="adding.set(false)" />
        @if (error(); as message) {
          <p class="error" role="alert" data-testid="party-error">{{ message }}</p>
        }
      </div>
    }

    <table mat-table [dataSource]="claim().parties" class="parties" data-testid="parties-table">
      <ng-container matColumnDef="role">
        <th mat-header-cell *matHeaderCellDef>Role</th>
        <td mat-cell *matCellDef="let p"><app-badge [label]="p.role" [tone]="roleTone(p)" /></td>
      </ng-container>
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let p">{{ p.displayName }} <span class="type">{{ p.type }}</span></td>
      </ng-container>
      <ng-container matColumnDef="contact">
        <th mat-header-cell *matHeaderCellDef>Contact</th>
        <td mat-cell *matCellDef="let p">
          @if (p.email) {
            <a [href]="'mailto:' + p.email">{{ p.email }}</a>
          }
          @if (p.email && p.phone) {
            ·
          }
          {{ p.phone ?? '' }}
          @if (!p.email && !p.phone) {
            —
          }
        </td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let p">
          <app-badge [label]="p.isActive ? 'Active' : 'Removed'" [tone]="p.isActive ? 'green' : 'grey'" />
        </td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let p">
          @if (p.isActive) {
            <span [matTooltip]="blocked(p) ?? ''">
              <button
                mat-stroked-button
                (click)="remove(p)"
                [disabled]="!!blocked(p) || removing() === p.partyId"
                [attr.data-testid]="'remove-' + p.partyId"
              >
                Remove
              </button>
            </span>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns" [class.inactive]="!row.isActive" [attr.data-testid]="'party-' + row.partyId"></tr>
    </table>
  `,
  styles: `
    :host { display: block; padding: 16px 4px; }
    .toolbar { display: flex; align-items: center; gap: 16px; margin-bottom: 12px; }
    .count { opacity: 0.7; font: var(--mat-sys-body-small); }
    .form-row { border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; padding: 12px; margin-bottom: 12px; }
    .error { color: var(--mat-sys-error); margin: 4px 0 0; }
    .parties { width: 100%; }
    .type { opacity: 0.6; font: var(--mat-sys-body-small); margin-left: 4px; }
    .inactive { opacity: 0.55; }
  `,
})
export class PartiesTab {
  readonly claim = input.required<ClaimDetail>();
  /** A party was added or removed: the page reloads (parties, validation issues, audit). */
  readonly changed = output<void>();

  private readonly api = inject(ClaimsApiService);
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);

  protected readonly columns = ['role', 'name', 'contact', 'status', 'actions'];
  protected readonly adding = signal(false);
  protected readonly saving = signal(false);
  protected readonly removing = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  private formKey = crypto.randomUUID();

  protected readonly editable = computed(() => isEditable(this.claim()));
  protected readonly activeCount = computed(() => this.claim().parties.filter((p) => p.isActive).length);

  protected roleTone(p: Party): BadgeTone {
    return ROLE_TONE[p.role];
  }

  protected blocked(p: Party): string | null {
    return removeBlockedReason(this.claim(), p);
  }

  protected openForm(): void {
    this.formKey = crypto.randomUUID();
    this.error.set(null);
    this.adding.set(true);
  }

  protected add(party: PartyInput): void {
    this.saving.set(true);
    this.error.set(null);
    this.api.addParty(this.claim().claimId, party, { idempotencyKey: this.formKey, inlineErrors: true }).subscribe({
      next: () => {
        this.saving.set(false);
        this.adding.set(false);
        this.notifications.success(`${labelOf(party.role)} added.`);
        this.changed.emit();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        const body = toApiError(err);
        this.error.set(errorMessages(body).join(' ') || body.title);
      },
    });
  }

  protected remove(p: Party): void {
    this.confirm
      .confirm({
        title: `Remove ${p.displayName}?`,
        message: `The ${labelOf(p.role)} is marked as removed; the record and its history are kept.`,
        confirmLabel: 'Remove',
        destructive: true,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.removing.set(p.partyId);
        this.api.removeParty(this.claim().claimId, p.partyId).subscribe({
          next: () => {
            this.removing.set(null);
            this.notifications.success(`${p.displayName} removed.`);
            this.changed.emit();
          },
          error: () => {
            this.removing.set(null);
            this.changed.emit();
          },
        });
      });
  }
}
