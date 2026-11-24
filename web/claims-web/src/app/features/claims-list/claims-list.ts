import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, merge, of, switchMap, tap } from 'rxjs';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { CauseOfLossCode, CLAIM_STATUSES, ClaimListQuery, ClaimStatus, ClaimSummary, PagedResult } from '../../core/api/models';
import { MoneyPipe } from '../../shared/money.pipe';
import { Badge, labelOf } from '../../shared/status-badge';

export const PAGE_SIZES = [10, 20, 50];
export const HANDLER_DEBOUNCE_MS = 300;

export interface FilterValues {
  readonly status: readonly ClaimStatus[];
  readonly dateFrom: string;
  readonly dateTo: string;
  readonly handler: string;
  readonly cause: string;
}

/** The S11.1 filter bar → GET /api/claims criteria; empty filters are left out. Dates are yyyy-MM-dd (UTC days). */
export function listFilters(v: FilterValues): Partial<ClaimListQuery> {
  const criteria: { -readonly [K in keyof ClaimListQuery]?: ClaimListQuery[K] } = {};
  if (v.status.length) criteria.status = v.status;
  if (v.dateFrom) criteria.dateFrom = v.dateFrom;
  if (v.dateTo) criteria.dateTo = v.dateTo;
  if (v.handler.trim()) criteria.assignedHandler = v.handler.trim();
  if (v.cause) criteria.causeOfLossCode = v.cause;
  return criteria;
}

/**
 * Claims dashboard (S11.1): the seven columns, the filter bar (status multi-select, loss date range,
 * assigned handler text, cause of loss), paging with the total count and page size, row navigation,
 * "Log New Claim" and the empty state.
 */
@Component({
  selector: 'app-claims-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatTableModule,
    MatPaginatorModule,
    MatSelectModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MoneyPipe,
    Badge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <h1>Claims</h1>
      <a mat-flat-button routerLink="/claims/new" data-testid="log-new-claim">
        <mat-icon>add</mat-icon>
        Log New Claim
      </a>
    </div>

    <form class="filters" [formGroup]="filters" data-testid="filter-bar">
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="status">
        <mat-label>Status</mat-label>
        <mat-select multiple formControlName="status" data-testid="status-filter">
          @for (status of statuses; track status) {
            <mat-option [value]="status">{{ label(status) }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="date">
        <mat-label>Loss date from</mat-label>
        <input matInput type="date" formControlName="dateFrom" data-testid="date-from" />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="date">
        <mat-label>Loss date to</mat-label>
        <input matInput type="date" formControlName="dateTo" data-testid="date-to" />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="handler">
        <mat-label>Assigned handler</mat-label>
        <input matInput formControlName="handler" placeholder="Name contains…" data-testid="handler-filter" />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="cause">
        <mat-label>Cause of loss</mat-label>
        <mat-select formControlName="cause" data-testid="cause-filter">
          <mat-option [value]="''">Any cause</mat-option>
          @for (c of causeCodes(); track c.code) {
            <mat-option [value]="c.code">{{ c.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <button mat-button type="button" (click)="clearFilters()" [disabled]="!hasFilters()" data-testid="clear-filters">
        <mat-icon>filter_alt_off</mat-icon> Clear
      </button>
    </form>
    @if (dateRangeInvalid()) {
      <p class="filter-error" role="alert" data-testid="date-range-error">"Loss date from" must be on or before "Loss date to".</p>
    }

    <div class="table-wrap">
      <table mat-table [dataSource]="result().items" class="claims-table">
        <ng-container matColumnDef="claimNumber">
          <th mat-header-cell *matHeaderCellDef>Claim number</th>
          <td mat-cell *matCellDef="let c" class="mono">{{ c.claimNumber }}</td>
        </ng-container>
        <ng-container matColumnDef="policyNumber">
          <th mat-header-cell *matHeaderCellDef>Policy number</th>
          <td mat-cell *matCellDef="let c">{{ c.policyNumber ?? '—' }}</td>
        </ng-container>
        <ng-container matColumnDef="clientName">
          <th mat-header-cell *matHeaderCellDef>Client name</th>
          <td mat-cell *matCellDef="let c">{{ c.clientName ?? '—' }}</td>
        </ng-container>
        <ng-container matColumnDef="lossDate">
          <th mat-header-cell *matHeaderCellDef>Loss date</th>
          <td mat-cell *matCellDef="let c">{{ c.lossDate | date: 'mediumDate' }}</td>
        </ng-container>
        <ng-container matColumnDef="causeOfLoss">
          <th mat-header-cell *matHeaderCellDef>Cause of loss</th>
          <td mat-cell *matCellDef="let c">{{ c.causeOfLossName ?? c.causeOfLossCode }}</td>
        </ng-container>
        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let c"><app-badge [status]="c.status" /></td>
        </ng-container>
        <ng-container matColumnDef="totalReserves">
          <th mat-header-cell *matHeaderCellDef class="num">Total reserves</th>
          <td mat-cell *matCellDef="let c" class="num">{{ c.totalReserves | money }}</td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr
          mat-row
          *matRowDef="let row; columns: columns"
          class="row"
          tabindex="0"
          (click)="open(row)"
          (keydown.enter)="open(row)"
          [attr.data-testid]="'claim-row-' + row.claimNumber"
        ></tr>
      </table>

      @if (loading()) {
        <div class="state"><mat-spinner diameter="32" /></div>
      } @else if (failed()) {
        <div class="state" data-testid="list-error">The claims could not be loaded. <button mat-button (click)="reload()">Try again</button></div>
      } @else if (result().totalCount === 0) {
        <div class="state" data-testid="empty-state">
          <mat-icon>inbox</mat-icon>
          <p>No claims match the current filter.</p>
        </div>
      }
    </div>

    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="query().page! - 1"
      [pageSize]="query().pageSize"
      [pageSizeOptions]="pageSizes"
      showFirstLastButtons
      (page)="onPage($event)"
      aria-label="Claims pages"
    />
  `,
  styles: `
    :host { display: block; min-width: 1200px; }
    .header { display: flex; align-items: center; justify-content: space-between; }
    h1 { font: var(--mat-sys-headline-small); margin: 0 0 16px; }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-bottom: 12px; }
    .filters .status { width: 240px; }
    .filters .date { width: 170px; }
    .filters .handler, .filters .cause { width: 200px; }
    .filter-error { color: var(--mat-sys-error); margin: -4px 0 8px; }
    .table-wrap { position: relative; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; overflow: hidden; }
    .claims-table { width: 100%; }
    .row { cursor: pointer; }
    .row:hover, .row:focus { background: var(--mat-sys-surface-container-high); outline: none; }
    .num { text-align: right; }
    .mono { font-family: 'Roboto Mono', monospace; }
    .state { display: flex; flex-direction: column; align-items: center; padding: 32px; gap: 8px; opacity: 0.8; }
  `,
})
export class ClaimsList {
  private readonly api = inject(ClaimsApiService);
  private readonly router = inject(Router);

  protected readonly statuses = CLAIM_STATUSES;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly columns = ['claimNumber', 'policyNumber', 'clientName', 'lossDate', 'causeOfLoss', 'status', 'totalReserves'];
  protected readonly label = labelOf;

  protected readonly filters = new FormGroup({
    status: new FormControl<ClaimStatus[]>([], { nonNullable: true }),
    dateFrom: new FormControl('', { nonNullable: true }),
    dateTo: new FormControl('', { nonNullable: true }),
    handler: new FormControl('', { nonNullable: true }),
    cause: new FormControl('', { nonNullable: true }),
  });
  /** Kept for the status filter's callers and tests. */
  protected readonly statusFilter = this.filters.controls.status;
  protected readonly query = signal<ClaimListQuery>({ page: 1, pageSize: 20, status: [] });
  protected readonly result = signal<PagedResult<ClaimSummary>>({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly dateRangeInvalid = signal(false);
  protected readonly hasFilters = signal(false);
  protected readonly causeCodes = toSignal(this.api.causeOfLossCodes().pipe(catchError(() => of<CauseOfLossCode[]>([]))), {
    initialValue: [] as CauseOfLossCode[],
  });

  constructor() {
    // Selects and dates apply at once; the handler text waits for a pause in typing.
    const c = this.filters.controls;
    merge(c.status.valueChanges, c.dateFrom.valueChanges, c.dateTo.valueChanges, c.cause.valueChanges, c.handler.valueChanges.pipe(debounceTime(HANDLER_DEBOUNCE_MS)))
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.applyFilters());

    toObservable(this.query)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
        }),
        switchMap((query) =>
          this.api.list(query).pipe(
            catchError(() => {
              this.failed.set(true);
              return of<PagedResult<ClaimSummary>>({ items: [], page: 1, pageSize: query.pageSize ?? 20, totalCount: 0 });
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.result.set(result);
        this.loading.set(false);
      });
  }

  protected onPage(event: PageEvent): void {
    this.query.update((q) => ({ ...q, page: event.pageSize === q.pageSize ? event.pageIndex + 1 : 1, pageSize: event.pageSize }));
  }

  protected clearFilters(): void {
    this.filters.reset();
  }

  private applyFilters(): void {
    const v = this.filters.getRawValue();
    const criteria = listFilters(v);
    this.hasFilters.set(Object.keys(criteria).length > 0);
    this.dateRangeInvalid.set(!!v.dateFrom && !!v.dateTo && v.dateFrom > v.dateTo);
    if (this.dateRangeInvalid()) {
      return;
    }
    this.query.update((q) => ({ page: 1, pageSize: q.pageSize, ...criteria }));
  }

  protected reload(): void {
    this.query.update((q) => ({ ...q }));
  }

  protected open(claim: ClaimSummary): void {
    void this.router.navigate(['/claims', claim.claimId]);
  }
}
