import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { approvalLevelFor } from './authority';

/** Real-time authority indicator for a reserve amount (S11.2 step 3, S11.3 add panel). */
@Component({
  selector: 'app-authority-indicator',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (level(); as lvl) {
      <span class="indicator" [attr.data-level]="lvl" role="status" aria-live="polite">
        <mat-icon aria-hidden="true">{{ lvl === 'Auto' ? 'check_circle' : 'warning' }}</mat-icon>
        {{ message() }}
      </span>
    }
  `,
  styles: `
    .indicator { display: inline-flex; align-items: center; gap: 6px; font: var(--mat-sys-body-medium); }
    mat-icon { font-size: 20px; width: 20px; height: 20px; }
    [data-level='Auto'] { color: #2e7d32; }
    [data-level='Supervisor'] { color: #a15c00; }
    [data-level='Manager'] { color: #b3261e; }
  `,
})
export class AuthorityIndicator {
  /** The change in balance (for Add: the amount). Nothing is shown while it is empty or zero. */
  readonly delta = input<number | null | undefined>(null);

  protected readonly level = computed(() => {
    const delta = this.delta();
    return delta === null || delta === undefined || Number.isNaN(delta) || delta === 0 ? null : approvalLevelFor(delta);
  });

  protected readonly message = computed(() => {
    switch (this.level()) {
      case 'Auto':
        return 'Auto-approved (≤ $10,000)';
      case 'Supervisor':
        return 'Supervisor approval required';
      case 'Manager':
        return 'Manager approval required';
      default:
        return '';
    }
  });
}
