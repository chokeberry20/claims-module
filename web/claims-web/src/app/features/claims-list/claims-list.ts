import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { catchError, of, switchMap, tap } from 'rxjs';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { CLAIM_STATUSES, ClaimListQuery, ClaimStatus, ClaimSummary, PagedResult } from '../../core/api/models';
import { MoneyPipe } from '../../shared/money.pipe';
import { Badge, labelOf } from '../../shared/status-badge';

export const PAGE_SIZES = [10, 20, 50];

/**
 * Claims dashboard, minimal (P6a task 38; S11.1): the seven columns, status filter, paging with the total
 * count and page size, row navigation and "Log New Claim". The full filter bar comes in P6b.
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

    <div class="filters">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Status</mat-label>
        <mat-select multiple [formControl]="statusFilter" data-testid="status-filter">
          @for (status of statuses; track status) {
            <mat-option [value]="status">{{ label(status) }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </div>

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
    .filters { display: flex; gap: 16px; margin-bottom: 8px; }
    .filters mat-form-field { width: 320px; }
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

  protected readonly statusFilter = new FormControl<ClaimStatus[]>([], { nonNullable: true });
  protected readonly query = signal<ClaimListQuery>({ page: 1, pageSize: 20, status: [] });
  protected readonly result = signal<PagedResult<ClaimSummary>>({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  constructor() {
    this.statusFilter.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((status) => this.query.update((q) => ({ ...q, status, page: 1 })));

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

  protected reload(): void {
    this.query.update((q) => ({ ...q }));
  }

  protected open(claim: ClaimSummary): void {
    void this.router.navigate(['/claims', claim.claimId]);
  }
}
