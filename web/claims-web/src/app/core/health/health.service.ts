import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, of } from 'rxjs';
import { SKIP_ERROR_SNACKBAR, SKIP_LOADING } from '../api/api-error';
import { APP_CONFIG } from '../config/app-config';

/** Shape of the API's GET /health response (see ClaimsModule.API HealthResponseWriter). */
export interface HealthReport {
  readonly status: 'Healthy' | 'Degraded' | 'Unhealthy';
  readonly entries?: Readonly<Record<string, { readonly status: string; readonly description?: string | null }>>;
}

export type ApiStatus = 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unreachable';

export interface ApiHealth {
  readonly status: ApiStatus;
  readonly detail: string;
}

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(APP_CONFIG);

  check(): Observable<ApiHealth> {
    // A background ping: no loading bar, and the footer shows failures instead of a snackbar.
    const context = new HttpContext().set(SKIP_LOADING, true).set(SKIP_ERROR_SNACKBAR, true);
    return this.http.get<HealthReport>(`${this.config.apiBaseUrl}/health`, { context }).pipe(
      map((report) => toApiHealth(report)),
      catchError((error: unknown) => of(fromError(error))),
    );
  }
}

function toApiHealth(report: HealthReport): ApiHealth {
  const failing = Object.entries(report.entries ?? {})
    .filter(([, entry]) => entry.status !== 'Healthy')
    .map(([name]) => name);
  return {
    status: report.status,
    detail: failing.length === 0 ? 'All checks passed' : `Failing: ${failing.join(', ')}`,
  };
}

function fromError(error: unknown): ApiHealth {
  // An unhealthy API answers 503 with the same JSON body; status 0 means no response at all (down or CORS).
  if (error instanceof HttpErrorResponse && error.status !== 0 && isHealthReport(error.error)) {
    return toApiHealth(error.error);
  }
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  return { status: 'Unreachable', detail: status === 0 ? 'No response from the API' : `HTTP ${status}` };
}

function isHealthReport(body: unknown): body is HealthReport {
  const status = (body as { status?: unknown } | null)?.status;
  return status === 'Healthy' || status === 'Degraded' || status === 'Unhealthy';
}
