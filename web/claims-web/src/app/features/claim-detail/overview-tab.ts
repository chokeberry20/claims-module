import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { errorMessages, toApiError } from '../../core/api/api-error';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { ClaimDetail, ClaimSeverity } from '../../core/api/models';
import { NotificationService } from '../../core/ui/notification.service';
import { MoneyPipe } from '../../shared/money.pipe';
import { Badge, BadgeTone } from '../../shared/status-badge';

export const MAX_NOTES_LENGTH = 4000;

export const SEVERITY_TONE: Readonly<Record<ClaimSeverity, BadgeTone>> = {
  Minor: 'grey',
  Standard: 'blue',
  Critical: 'orange',
  Catastrophic: 'red',
};

/** Claims in these statuses can't be changed any more (the domain's IsTerminalForEditing). */
export function isEditable(claim: ClaimDetail): boolean {
  return claim.status !== 'Closed' && claim.status !== 'Withdrawn';
}

/**
 * Overview tab (S11.3 tab 1): loss event details (date, description, location, cause), severity badge,
 * estimated loss amount, and the editable claim notes (PUT /notes; read-only once Closed or Withdrawn).
 */
@Component({
  selector: 'app-overview-tab',
  imports: [DatePipe, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MoneyPipe, Badge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let c = claim();
    <div class="grid">
      <section>
        <h3>Loss event</h3>
        <dl>
          <dt>Loss date</dt>
          <dd data-testid="overview-loss-date">{{ c.lossEvent.lossDate | date: 'medium' }}</dd>
          <dt>Cause of loss</dt>
          <dd>{{ c.lossEvent.causeOfLossName ?? c.lossEvent.causeOfLossCode }} ({{ c.lossEvent.causeOfLossCode }})</dd>
          <dt>Location</dt>
          <dd>{{ c.lossEvent.lossLocation || '—' }}</dd>
          <dt>Description</dt>
          <dd class="pre" data-testid="overview-description">{{ c.lossEvent.lossDescription }}</dd>
          <dt>Police report</dt>
          <dd>{{ c.lossEvent.policeReportNumber || '—' }}</dd>
          <dt>Reported</dt>
          <dd>{{ c.lossEvent.reportDate | date: 'medium' }}</dd>
        </dl>
      </section>
      <section>
        <h3>Claim</h3>
        <dl>
          <dt>Severity</dt>
          <dd><app-badge [label]="c.severity" [tone]="severityTone()" data-testid="overview-severity" /></dd>
          <dt>Claim type</dt>
          <dd>{{ c.claimType || '—' }}</dd>
          <dt>Estimated loss</dt>
          <dd data-testid="overview-estimated">{{ c.lossEvent.estimatedLossAmount | money }}</dd>
          <dt>Approved reserves (gross)</dt>
          <dd>{{ c.reserves.approvedGross | money }}</dd>
          @if (c.closedAt) {
            <dt>Closed</dt>
            <dd>{{ c.closedAt | date: 'medium' }}</dd>
          }
        </dl>
      </section>
    </div>

    <section class="notes">
      <h3>Notes</h3>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Claim notes</mat-label>
        <textarea matInput rows="5" [formControl]="notes" data-testid="notes"></textarea>
        <mat-hint align="end">{{ notes.value.length }} / {{ maxNotes }}</mat-hint>
        <mat-error>At most {{ maxNotes }} characters.</mat-error>
      </mat-form-field>
      @if (error(); as message) {
        <p class="error" role="alert" data-testid="notes-error">{{ message }}</p>
      }
      @if (editable()) {
        <div class="actions">
          <button mat-button (click)="reset()" [disabled]="!dirty() || saving()">Discard</button>
          <button mat-flat-button (click)="save()" [disabled]="!dirty() || notes.invalid || saving()" data-testid="save-notes">
            Save notes
          </button>
        </div>
      } @else {
        <p class="hint">The claim is {{ c.status }}: notes can no longer be changed.</p>
      }
    </section>
  `,
  styles: `
    :host { display: block; padding: 16px 4px; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; }
    h3 { font: var(--mat-sys-title-medium); margin: 0 0 8px; }
    dl { display: grid; grid-template-columns: 180px 1fr; gap: 6px 12px; margin: 0; }
    dt { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-large); }
    dd { margin: 0; }
    .pre { white-space: pre-wrap; }
    .notes { margin-top: 24px; max-width: 900px; }
    .full { width: 100%; }
    .actions { display: flex; justify-content: flex-end; gap: 8px; }
    .error { color: var(--mat-sys-error); }
    .hint { opacity: 0.7; }
  `,
})
export class OverviewTab {
  readonly claim = input.required<ClaimDetail>();
  /** The notes were saved: the page reloads (header, audit). */
  readonly changed = output<void>();

  private readonly api = inject(ClaimsApiService);
  private readonly notifications = inject(NotificationService);

  protected readonly maxNotes = MAX_NOTES_LENGTH;
  protected readonly notes = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(MAX_NOTES_LENGTH)] });
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly value = signal('');

  protected readonly editable = computed(() => isEditable(this.claim()));
  protected readonly severityTone = computed(() => SEVERITY_TONE[this.claim().severity]);
  protected readonly dirty = computed(() => this.value() !== (this.claim().notes ?? ''));

  constructor() {
    this.notes.valueChanges.subscribe((v) => this.value.set(v));
    // Take the saved notes when the claim (re)loads, unless the user has unsaved edits (a reload caused by
    // another tab mustn't wipe them). The field is read-only for closed claims.
    let lastSaved: string | null = null;
    effect(() => {
      const claim = this.claim();
      const saved = claim.notes ?? '';
      if (lastSaved === null || this.notes.value === lastSaved) {
        this.notes.setValue(saved);
      }
      lastSaved = saved;
      if (isEditable(claim)) this.notes.enable();
      else this.notes.disable();
    });
  }

  protected reset(): void {
    this.notes.setValue(this.claim().notes ?? '');
    this.error.set(null);
  }

  protected save(): void {
    if (this.notes.invalid) {
      return;
    }
    const text = this.notes.value.trim();
    this.saving.set(true);
    this.error.set(null);
    this.api.updateNotes(this.claim().claimId, text === '' ? null : text, { inlineErrors: true }).subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success('Notes saved.');
        this.changed.emit();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        const body = toApiError(err);
        this.error.set(errorMessages(body).join(' ') || body.title);
      },
    });
  }
}
