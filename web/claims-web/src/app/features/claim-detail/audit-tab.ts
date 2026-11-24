import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { catchError, EMPTY, switchMap } from 'rxjs';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { AuditLogEntry, PagedResult } from '../../core/api/models';
import { Badge, BadgeTone } from '../../shared/status-badge';

/** Event type → badge colour: approvals and postings green, failures and breaches red, the rest by area. */
export function auditTone(eventType: string): BadgeTone {
  if (/FAILED|BREACH|REJECTED/.test(eventType)) return 'red';
  if (/APPROVED|POSTING_SIMULATED|CLOSED/.test(eventType)) return 'green';
  if (/RESERVE/.test(eventType)) return 'purple';
  if (/STATUS|REOPENED|WITHDRAWN/.test(eventType)) return 'blue';
  if (/VALIDATION|WARNING/.test(eventType)) return 'amber';
  return 'grey';
}

/** Audit Log tab (S11.3, S14): newest first, paginated, read-only. */
@Component({
  selector: 'app-audit-tab',
  imports: [DatePipe, MatTableModule, MatPaginatorModule, Badge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <table mat-table [dataSource]="result().items" class="audit" data-testid="audit-table">
      <ng-container matColumnDef="time">
        <th mat-header-cell *matHeaderCellDef>Time</th>
        <td mat-cell *matCellDef="let e" class="nowrap">{{ e.createdAt | date: 'medium' }}</td>
      </ng-container>
      <ng-container matColumnDef="event">
        <th mat-header-cell *matHeaderCellDef>Event</th>
        <td mat-cell *matCellDef="let e"><app-badge [label]="e.eventType" [tone]="tone(e.eventType)" class="event" /></td>
      </ng-container>
      <ng-container matColumnDef="description">
        <th mat-header-cell *matHeaderCellDef>Description</th>
        <td mat-cell *matCellDef="let e">{{ e.description }}</td>
      </ng-container>
      <ng-container matColumnDef="user">
        <th mat-header-cell *matHeaderCellDef>User</th>
        <td mat-cell *matCellDef="let e" class="nowrap">{{ user(e) }}</td>
      </ng-container>
      <ng-container matColumnDef="related">
        <th mat-header-cell *matHeaderCellDef>Related</th>
        <td mat-cell *matCellDef="let e" class="nowrap">
          @if (e.relatedEntityType) {
            <span [title]="e.relatedEntityId ?? ''">{{ e.relatedEntityType }}</span>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns" [attr.data-event]="row.eventType"></tr>
    </table>
    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="page() - 1"
      [pageSize]="pageSize()"
      [pageSizeOptions]="[10, 25, 50]"
      (page)="onPage($event)"
      aria-label="Audit log pages"
    />
  `,
  styles: `
    :host { display: block; padding: 16px 4px; }
    .audit { width: 100%; }
    .nowrap { white-space: nowrap; }
  `,
})
export class AuditTab {
  readonly claimId = input.required<string>();
  readonly userNames = input(new Map<string, string>());
  readonly version = input(0);

  private readonly api = inject(ClaimsApiService);
  protected readonly columns = ['time', 'event', 'description', 'user', 'related'];
  protected readonly tone = auditTone;
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly result = signal<PagedResult<AuditLogEntry>>({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  private readonly request = computed(() => ({ page: this.page(), pageSize: this.pageSize(), version: this.version() }));

  constructor() {
    toObservable(this.request)
      .pipe(
        switchMap((r) => this.api.auditLog(this.claimId(), r.page, r.pageSize).pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.result.set(result));
  }

  protected user(entry: AuditLogEntry): string {
    if (!entry.createdByUserId) return 'System';
    return this.userNames().get(entry.createdByUserId) ?? 'System';
  }

  protected onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.page.set(event.pageIndex + 1);
  }
}
