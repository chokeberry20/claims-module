import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { TokenResponse } from '../api/models';
import { APP_CONFIG } from '../config/app-config';
import { AuthService, CLOCK, hasRoleAtLeast } from './auth.service';

const API = 'https://api.test';
const T0 = Date.parse('2026-11-24T10:00:00Z');

function token(name: string, role: TokenResponse['role'], expiresAt = '2026-11-24T18:00:00Z'): TokenResponse {
  return { accessToken: `jwt-${name}`, tokenType: 'Bearer', expiresAt, userId: `id-${name}`, displayName: name, role };
}

describe('AuthService (D-73: tokens in memory for the tab only)', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  let now: number;

  beforeEach(() => {
    now = T0;
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: CLOCK, useValue: () => now },
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function signIn(name: string, role: TokenResponse['role'], expiresAt?: string): Promise<void> {
    const done = firstValueFrom(auth.signIn(name, 'typed-in-the-form'));
    const req = http.expectOne(`${API}/api/auth/token`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userName: name, password: 'typed-in-the-form' });
    req.flush(token(name, role, expiresAt));
    await done;
  }

  it('signs in through the token endpoint and keeps the session in memory only', async () => {
    await signIn('handler1', 'Handler');

    expect(auth.session()).toMatchObject({ userName: 'handler1', role: 'Handler', accessToken: 'jwt-handler1' });
    expect(auth.accessToken()).toBe('jwt-handler1');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('switches back to a user who signed in earlier in this tab without asking again', async () => {
    await signIn('handler1', 'Handler');
    await signIn('supervisor1', 'Supervisor');

    expect(auth.switchTo('handler1')).toBe(true);
    expect(auth.session()?.userName).toBe('handler1');
    expect(auth.accessToken()).toBe('jwt-handler1');
  });

  it('asks for the password again (switchTo is false) when there is no token or it expired', async () => {
    expect(auth.switchTo('manager1')).toBe(false);

    await signIn('handler1', 'Handler', '2026-11-24T10:30:00Z');
    await signIn('supervisor1', 'Supervisor');
    now = Date.parse('2026-11-24T10:29:30Z'); // inside the one-minute safety margin

    expect(auth.hasValidToken('handler1')).toBe(false);
    expect(auth.switchTo('handler1')).toBe(false);
    expect(auth.session()?.userName).toBe('supervisor1');
  });

  it('drops an expired current token and reports signed out', async () => {
    await signIn('handler1', 'Handler', '2026-11-24T11:00:00Z');
    now = Date.parse('2026-11-24T11:00:00Z');

    expect(auth.accessToken()).toBeNull();
    expect(auth.isSignedIn()).toBe(false);
  });

  it('expireCurrent forgets only the rejected token; signOut forgets every token', async () => {
    await signIn('handler1', 'Handler');
    await signIn('supervisor1', 'Supervisor');

    auth.expireCurrent();
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.hasValidToken('supervisor1')).toBe(false);
    expect(auth.hasValidToken('handler1')).toBe(true);

    auth.signOut();
    expect(auth.hasValidToken('handler1')).toBe(false);
  });

  it('lists the demo users from the anonymous directory', async () => {
    const users = firstValueFrom(auth.users());
    http.expectOne(`${API}/api/auth/users`).flush([{ userId: '1', userName: 'handler1', displayName: 'Hanna', role: 'Handler' }]);

    expect((await users).map((u) => u.userName)).toEqual(['handler1']);
  });

  it('ranks roles handler < supervisor < manager (S3)', () => {
    expect(hasRoleAtLeast('Manager', 'Supervisor')).toBe(true);
    expect(hasRoleAtLeast('Supervisor', 'Supervisor')).toBe(true);
    expect(hasRoleAtLeast('Handler', 'Supervisor')).toBe(false);
    expect(hasRoleAtLeast(undefined, 'Handler')).toBe(false);
  });
});
