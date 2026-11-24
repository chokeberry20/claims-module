import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { IDEMPOTENCY_KEY, SKIP_ERROR_SNACKBAR, SKIP_LOADING } from '../api/api-error';
import { AuthService } from '../auth/auth.service';
import { APP_CONFIG } from '../config/app-config';
import { LoadingService } from '../ui/loading.service';
import { NotificationService } from '../ui/notification.service';
import { apiInterceptors, IDEMPOTENCY_HEADER, RETRY_DELAY_MS } from './interceptors';

const API = 'https://api.test';

describe('API interceptors', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let token: string | null;
  const expireCurrent = vi.fn();
  const navigate = vi.fn(() => Promise.resolve(true));
  const show = vi.fn();

  beforeEach(() => {
    token = 'jwt-handler1';
    expireCurrent.mockReset();
    navigate.mockClear();
    show.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors(apiInterceptors)),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: AuthService, useValue: { accessToken: () => token, expireCurrent } },
        { provide: Router, useValue: { url: '/claims/42', navigate } },
        { provide: NotificationService, useValue: { show } },
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const validation = {
    type: 'ValidationError',
    title: 'One or more validation errors occurred.',
    status: 422,
    errors: { LossDescription: ['Loss description must be at least 20 characters.'], General: ['Self-approval is not permitted.'] },
    codes: { LossDescription: ['LOSS_DESCRIPTION_TOO_SHORT'], General: ['SELF_APPROVAL'] },
    correlationId: '0f8e…',
  };

  describe('auth', () => {
    it('adds the bearer token to API calls', () => {
      client.get(`${API}/api/claims`).subscribe();
      expect(http.expectOne(`${API}/api/claims`).request.headers.get('Authorization')).toBe('Bearer jwt-handler1');
    });

    it('sends no token to the sign-in endpoints, the health check or other hosts', () => {
      client.post(`${API}/api/auth/token`, {}).subscribe();
      client.get(`${API}/api/auth/users`).subscribe();
      client.get(`${API}/health`).subscribe();
      client.get('https://blob.example/doc.pdf').subscribe();

      for (const url of [`${API}/api/auth/token`, `${API}/api/auth/users`, `${API}/health`, 'https://blob.example/doc.pdf']) {
        expect(http.expectOne(url).request.headers.has('Authorization')).toBe(false);
      }
    });

    it('sends no header when signed out', () => {
      token = null;
      client.get(`${API}/api/claims`).subscribe({ error: () => undefined });
      expect(http.expectOne(`${API}/api/claims`).request.headers.has('Authorization')).toBe(false);
    });

    it('on 401 forgets the token and sends the user to sign in, coming back to the page', async () => {
      const call = firstValueFrom(client.get(`${API}/api/claims`)).catch((e: unknown) => e);
      http.expectOne(`${API}/api/claims`).flush(
        { type: 'Unauthorized', title: 'Authentication is required.', status: 401 },
        { status: 401, statusText: 'Unauthorized' },
      );
      await call;

      expect(expireCurrent).toHaveBeenCalledOnce();
      expect(navigate).toHaveBeenCalledWith(['/sign-in'], { queryParams: { returnUrl: '/claims/42' } });
      expect(show).toHaveBeenCalledWith('Your session has ended. Please sign in again.', 'error');
    });
  });

  describe('idempotency', () => {
    it('adds a new GUID key to each write and none to reads', () => {
      client.post(`${API}/api/claims`, {}).subscribe();
      client.post(`${API}/api/claims`, {}).subscribe();
      client.get(`${API}/api/claims`).subscribe();

      const [first, second] = http.match((r) => r.method === 'POST').map((r) => r.request.headers.get(IDEMPOTENCY_HEADER));
      expect(first).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
      expect(second).not.toBe(first);
      expect(http.expectOne((r) => r.method === 'GET').request.headers.has(IDEMPOTENCY_HEADER)).toBe(false);
    });

    it('uses the key the caller chose (a retried form submit)', () => {
      const context = new HttpContext().set(IDEMPOTENCY_KEY, 'fnol-attempt-1');
      client.put(`${API}/api/claims/1/status`, {}, { context }).subscribe();
      expect(http.expectOne(`${API}/api/claims/1/status`).request.headers.get(IDEMPOTENCY_HEADER)).toBe('fnol-attempt-1');
    });

    it('does not key the sign-in call', () => {
      client.post(`${API}/api/auth/token`, {}).subscribe();
      expect(http.expectOne(`${API}/api/auth/token`).request.headers.has(IDEMPOTENCY_HEADER)).toBe(false);
    });

    it('retries a network failure once with the same key', async () => {
      vi.useFakeTimers();
      const result = firstValueFrom(client.post<{ ok: boolean }>(`${API}/api/claims`, {}));

      const first = http.expectOne(`${API}/api/claims`);
      const key = first.request.headers.get(IDEMPOTENCY_HEADER);
      first.error(new ProgressEvent('error'), { status: 0 });
      await vi.advanceTimersByTimeAsync(RETRY_DELAY_MS);

      const second = http.expectOne(`${API}/api/claims`);
      expect(second.request.headers.get(IDEMPOTENCY_HEADER)).toBe(key);
      second.flush({ ok: true });
      expect(await result).toEqual({ ok: true });
      expect(show).not.toHaveBeenCalled();
    });

    it('does not retry a business error', async () => {
      const result = firstValueFrom(client.post(`${API}/api/claims`, {})).catch((e: unknown) => e);
      http.expectOne(`${API}/api/claims`).flush(validation, { status: 422, statusText: 'Unprocessable Entity' });
      await result;
      http.expectNone(`${API}/api/claims`);
    });
  });

  describe('errors', () => {
    it('shows the S10.4 messages in an error snackbar and still fails the call', async () => {
      const result = firstValueFrom(client.post(`${API}/api/claims`, {})).catch((e: unknown) => e);
      http.expectOne(`${API}/api/claims`).flush(validation, { status: 422, statusText: 'Unprocessable Entity' });

      expect(await result).toBeTruthy();
      expect(show).toHaveBeenCalledWith(
        'Loss description: Loss description must be at least 20 characters. Self-approval is not permitted.',
        'error',
      );
    });

    it('shows a 409 conflict as a warning with its detail', async () => {
      const result = firstValueFrom(client.put(`${API}/api/claims/1/status`, {})).catch((e: unknown) => e);
      http.expectOne(`${API}/api/claims/1/status`).flush(
        {
          type: 'Conflict',
          title: 'The request conflicts with the current state.',
          status: 409,
          detail: 'Reload it and try again.',
          codes: { General: ['CONCURRENCY_CONFLICT'] },
        },
        { status: 409, statusText: 'Conflict' },
      );
      await result;
      expect(show).toHaveBeenCalledWith('The request conflicts with the current state. Reload it and try again.', 'warning');
    });

    it('stays quiet when the caller shows the errors inline', async () => {
      const context = new HttpContext().set(SKIP_ERROR_SNACKBAR, true);
      const result = firstValueFrom(client.post(`${API}/api/claims`, {}, { context })).catch((e: unknown) => e);
      http.expectOne(`${API}/api/claims`).flush(validation, { status: 422, statusText: 'Unprocessable Entity' });
      await result;
      expect(show).not.toHaveBeenCalled();
    });
  });

  describe('loading', () => {
    it('is on while calls are in flight, and skips background calls', () => {
      const loading = TestBed.inject(LoadingService);
      client.get(`${API}/api/claims`).subscribe();
      client.get(`${API}/health`, { context: new HttpContext().set(SKIP_LOADING, true) }).subscribe();
      expect(loading.isLoading()).toBe(true);

      http.expectOne(`${API}/api/claims`).flush({});
      expect(loading.isLoading()).toBe(false);
      http.expectOne(`${API}/health`).flush({});
    });
  });
});
