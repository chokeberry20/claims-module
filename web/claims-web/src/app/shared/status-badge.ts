import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ApprovalStatus, ClaimStatus, PostingStatus } from '../core/api/models';

export type BadgeTone = 'grey' | 'blue' | 'orange' | 'purple' | 'green' | 'amber' | 'dark' | 'red';

/** S11.1: Draft grey, Open blue, UnderInvestigation orange, PendingPayment purple, Closed green, Reopened amber, Withdrawn dark grey. */
export const CLAIM_STATUS_TONE: Readonly<Record<ClaimStatus, BadgeTone>> = {
  Draft: 'grey',
  Open: 'blue',
  UnderInvestigation: 'orange',
  PendingPayment: 'purple',
  Closed: 'green',
  Reopened: 'amber',
  Withdrawn: 'dark',
};

export const APPROVAL_STATUS_TONE: Readonly<Record<ApprovalStatus, BadgeTone>> = {
  AutoApproved: 'green',
  Approved: 'green',
  PendingApproval: 'amber',
  Rejected: 'red',
  Cancelled: 'grey',
};

export const POSTING_STATUS_TONE: Readonly<Record<PostingStatus, BadgeTone>> = {
  Pending: 'amber',
  Posted: 'green',
  Failed: 'red',
  Cancelled: 'grey',
};

/** "UnderInvestigation" → "Under investigation", "ALAE" stays "ALAE". */
export function labelOf(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).replace(/ ([A-Z])(?=[a-z])/g, (_, c: string) => ` ${c.toLowerCase()}`);
}

/** A coloured pill. Use `status` for a claim status (S11.1 colours), or `label` + `tone` for anything else. */
@Component({
  selector: 'app-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [attr.data-tone]="effectiveTone()">{{ text() }}</span>`,
  styles: `
    :host { display: inline-block; }
    .badge {
      display: inline-block;
      padding: 2px 10px;
      border-radius: 12px;
      font: var(--mat-sys-label-medium);
      white-space: nowrap;
      color: #fff;
    }
    [data-tone='grey'] { background: #78909c; }
    [data-tone='blue'] { background: #1565c0; }
    [data-tone='orange'] { background: #e65100; }
    [data-tone='purple'] { background: #6a1b9a; }
    [data-tone='green'] { background: #2e7d32; }
    [data-tone='amber'] { background: #ffb300; color: #3e2700; }
    [data-tone='dark'] { background: #37474f; }
    [data-tone='red'] { background: #c62828; }
  `,
})
export class Badge {
  readonly status = input<ClaimStatus>();
  readonly label = input<string>();
  readonly tone = input<BadgeTone>('grey');

  protected readonly effectiveTone = computed(() => {
    const status = this.status();
    return status ? CLAIM_STATUS_TONE[status] : this.tone();
  });

  protected readonly text = computed(() => labelOf(this.status() ?? this.label() ?? ''));
}
