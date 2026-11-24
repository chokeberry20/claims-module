import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, retry, throwError, timer } from 'rxjs';
import { IDEMPOTENCY_KEY, SKIP_ERROR_SNACKBAR, SKIP_LOADING, summarizeError, toApiError } from '../api/api-error';
import { AuthService } from '../auth/auth.service';
import { APP_CONFIG } from '../config/app-config';
import { LoadingService } from '../ui/loading.service';
import { NotificationService } from '../ui/notification.service';

export const IDEMPOTENCY_HEADER = 'Idempotency-Key';
const WRITE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
/** Status codes worth one automatic retry: no response at all, or a gateway/restart answer. */
const TRANSIENT_STATUSES = new Set([0, 502, 503, 504]);
export const RETRY_DELAY_MS = 500;

function isApiCall(req: HttpRequest<unknown>, apiBaseUrl: string): boolean {
  return req.url.startsWith(`${apiBaseUrl}/api/`);
}

function isAnonymousAuthCall(req: HttpRequest<unknown>, apiBaseUrl: string): boolean {
  return req.url === `${apiBaseUrl}/api/auth/token` || req.url === `${apiBaseUrl}/api/auth/users`;
}

/**
 * Adds the bearer token of the signed-in user to every API call (S11.4). A 401 on a call that carried a
 * token means it expired or was rejected: the token is forgotten and the user is sent to sign in again.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const apiBaseUrl = inject(APP_CONFIG).apiBaseUrl;
  if (!isApiCall(req, apiBaseUrl) || isAnonymousAuthCall(req, apiBaseUrl)) {
    return next(req);
  }

  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.accessToken();
  const authorized = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        auth.expireCurrent();
        const returnUrl = router.url.startsWith('/sign-in') ? undefined : router.url;
        void router.navigate(['/sign-in'], { queryParams: { returnUrl } });
      }
      return throwError(() => error);
    }),
  );
};

/**
 * Every API write carries an Idempotency-Key (S10, D-66): the caller's key when it set one, otherwise a
 * new GUID per request. A transient failure is retried once **with the same key**, so if the first
 * attempt did reach the server, the retry replays its result instead of doing the work twice.
 */
export const idempotencyInterceptor: HttpInterceptorFn = (req, next) => {
  const apiBaseUrl = inject(APP_CONFIG).apiBaseUrl;
  if (!WRITE_METHODS.has(req.method) || !isApiCall(req, apiBaseUrl) || isAnonymousAuthCall(req, apiBaseUrl)) {
    return next(req);
  }

  const key = req.context.get(IDEMPOTENCY_KEY) ?? req.headers.get(IDEMPOTENCY_HEADER) ?? crypto.randomUUID();
  const keyed = req.clone({ setHeaders: { [IDEMPOTENCY_HEADER]: key } });

  return next(keyed).pipe(
    retry({
      count: 1,
      delay: (error: unknown) =>
        error instanceof HttpErrorResponse && TRANSIENT_STATUSES.has(error.status)
          ? timer(RETRY_DELAY_MS)
          : throwError(() => error),
    }),
  );
};

/** Drives the global loading bar for every call except background ones (B3.7.4). */
export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(SKIP_LOADING)) {
    return next(req);
  }
  const loading = inject(LoadingService);
  loading.begin();
  return next(req).pipe(finalize(() => loading.end()));
};

/**
 * Shows every failed API call as an error snackbar with the S10.4 body's title and messages (S11.4),
 * unless the caller displays the errors itself (inline form errors). The error still reaches the caller.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!req.context.get(SKIP_ERROR_SNACKBAR)) {
        const body = toApiError(error);
        const message = body.status === 401 ? 'Your session has ended. Please sign in again.' : summarizeError(body);
        notifications.show(message, body.status === 409 || body.status === 429 ? 'warning' : 'error');
      }
      return throwError(() => error);
    }),
  );
};

/** Outermost first: errors are reported once, after the retry; the loading bar spans the retry too. */
export const apiInterceptors = [errorInterceptor, loadingInterceptor, idempotencyInterceptor, authInterceptor];
