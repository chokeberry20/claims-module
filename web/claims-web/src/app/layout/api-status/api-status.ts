import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatTooltipModule } from '@angular/material/tooltip';
import { interval, startWith, switchMap } from 'rxjs';
import { APP_CONFIG } from '../../core/config/app-config';
import { ApiHealth, HealthService } from '../../core/health/health.service';

/** Footer indicator: pings the API's /health on load and every 60 seconds. */
@Component({
  selector: 'app-api-status',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let h = health();
    <span class="api-status" [attr.data-status]="h?.status ?? 'Checking'" [matTooltip]="h?.detail ?? ''">
      <span class="dot" aria-hidden="true"></span>
      API: {{ h?.status ?? 'Checking…' }}
    </span>
    <span class="api-url">{{ apiBaseUrl }}</span>
  `,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 12px; }
    .api-status { display: inline-flex; align-items: center; gap: 6px; font-weight: 500; }
    .dot { width: 10px; height: 10px; border-radius: 50%; background: var(--mat-sys-outline); }
    [data-status='Healthy'] .dot { background: #2e7d32; }
    [data-status='Degraded'] .dot { background: #ed6c02; }
    [data-status='Unhealthy'] .dot, [data-status='Unreachable'] .dot { background: var(--mat-sys-error); }
    .api-url { opacity: 0.7; }
  `,
})
export class ApiStatus {
  protected readonly apiBaseUrl = inject(APP_CONFIG).apiBaseUrl;
  protected readonly health = signal<ApiHealth | null>(null);

  constructor() {
    const healthService = inject(HealthService);
    interval(60_000)
      .pipe(
        startWith(0),
        switchMap(() => healthService.check()),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((result) => this.health.set(result));
  }
}
