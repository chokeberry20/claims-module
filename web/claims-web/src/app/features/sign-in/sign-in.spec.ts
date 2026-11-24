import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { APP_CONFIG } from '../../core/config/app-config';
import { SKIP_ERROR_SNACKBAR } from '../../core/api/api-error';
import { safeReturnUrl, SignIn } from './sign-in';

const API = 'https://api.test';

describe('SignIn (D-73: user picker + password)', () => {
  let fixture: ComponentFixture<SignIn>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SignIn);
    fixture.componentRef.setInput('user', 'supervisor1');
    fixture.componentRef.setInput('returnUrl', '/claims/42');
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    http.expectOne(`${API}/api/auth/users`).flush([
      { userId: 'u3', userName: 'supervisor1', displayName: 'Sam Supervisor', role: 'Supervisor' },
    ]);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  function typePassword(value: string): void {
    const input = el.querySelector<HTMLInputElement>('[data-testid="password"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    el.querySelector<HTMLButtonElement>('[data-testid="sign-in-submit"]')!.click();
  }

  it('has no password anywhere until the user types it', () => {
    expect(el.querySelector<HTMLInputElement>('[data-testid="password"]')!.value).toBe('');
    expect(el.querySelector('[data-testid="password"]')!.getAttribute('type')).toBe('password');
  });

  it('signs in as the preselected user and returns to the page it came from', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    typePassword('typed');
    submit();

    const req = http.expectOne(`${API}/api/auth/token`);
    expect(req.request.body).toEqual({ userName: 'supervisor1', password: 'typed' });
    req.flush({
      accessToken: 'jwt',
      tokenType: 'Bearer',
      expiresAt: new Date(Date.now() + 3600_000).toISOString(),
      userId: 'u3',
      displayName: 'Sam Supervisor',
      role: 'Supervisor',
    });
    await fixture.whenStable();

    expect(TestBed.inject(AuthService).session()?.role).toBe('Supervisor');
    expect(navigate).toHaveBeenCalledWith('/claims/42');
  });

  it('shows a wrong password inline and clears the field', async () => {
    typePassword('wrong');
    submit();
    http.expectOne(`${API}/api/auth/token`).flush(
      { type: 'Unauthorized', title: 'Invalid user name or password.', status: 401 },
      { status: 401, statusText: 'Unauthorized' },
    );
    await fixture.whenStable();

    expect(el.querySelector('[data-testid="sign-in-error"]')?.textContent).toContain('Wrong user or password.');
    expect(el.querySelector<HTMLInputElement>('[data-testid="password"]')!.value).toBe('');
  });

  it('does not submit without a password', () => {
    submit();
    http.expectNone(`${API}/api/auth/token`);
  });

  it('only follows same-app return URLs', () => {
    expect(safeReturnUrl('/claims/1')).toBe('/claims/1');
    expect(safeReturnUrl('//evil.example')).toBe('/claims');
    expect(safeReturnUrl('https://evil.example')).toBe('/claims');
    expect(safeReturnUrl('/sign-in')).toBe('/claims');
    expect(safeReturnUrl(undefined)).toBe('/claims');
  });
});

describe('SignIn user list states (D-75: delayed or failing GET /api/auth/users)', () => {
  let fixture: ComponentFixture<SignIn>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const USERS = [{ userId: 'u1', userName: 'handler1', displayName: 'Hannah Handler', role: 'Handler' }];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: APP_CONFIG, useValue: { apiBaseUrl: API } }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SignIn);
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const picker = () => el.querySelector<HTMLElement>('mat-select[data-testid="user-select"]')!;
  const submit = () => el.querySelector<HTMLButtonElement>('[data-testid="sign-in-submit"]')!;
  const openOptions = () => document.querySelectorAll('mat-option').length;
  const fail = (status: number) =>
    http.expectOne(`${API}/api/auth/users`).flush('unavailable', { status, statusText: 'Error' });

  it('while the list is on its way, the picker is disabled and says it is loading (the old race)', async () => {
    const req = http.expectOne(`${API}/api/auth/users`);
    expect(picker().getAttribute('aria-disabled')).toBe('true');
    expect(el.querySelector('[data-testid="users-loading"]')?.textContent).toContain('Loading users');
    expect(submit().disabled).toBe(true);

    // A click before the list arrives does nothing, but now visibly so (disabled), not silently.
    picker().click();
    await fixture.whenStable();
    expect(openOptions()).toBe(0);

    // The late response arrives: the picker is enabled and opens with the users.
    req.flush(USERS);
    await fixture.whenStable();
    expect(picker().getAttribute('aria-disabled')).toBe('false');
    expect(el.querySelector('[data-testid="users-loading"]')).toBeNull();
    picker().click();
    await fixture.whenStable();
    expect(openOptions()).toBe(1);
    expect(submit().disabled).toBe(false);
  });

  it('retries a transient failure once and recovers without the user doing anything', async () => {
    fail(503);
    await new Promise((r) => setTimeout(r, 1600));
    http.expectOne(`${API}/api/auth/users`).flush(USERS);
    await fixture.whenStable();
    expect(picker().getAttribute('aria-disabled')).toBe('false');
    expect(el.querySelector('[data-testid="users-error"]')).toBeNull();
  });

  it('after a second failure shows a clear inline error with "Try again", which recovers', async () => {
    const first = http.expectOne(`${API}/api/auth/users`);
    expect(first.request.context.get(SKIP_ERROR_SNACKBAR)).toBe(true);
    first.flush('unavailable', { status: 503, statusText: 'Error' });
    await new Promise((r) => setTimeout(r, 1600));
    fail(503);
    await fixture.whenStable();

    const alert = el.querySelector('[data-testid="users-error"]');
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(alert?.textContent).toContain("The user list couldn't be loaded (The request failed (HTTP 503).)");
    expect(picker().getAttribute('aria-disabled')).toBe('true');
    expect(submit().disabled).toBe(true);

    el.querySelector<HTMLButtonElement>('[data-testid="users-retry"]')!.click();
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="users-loading"]')).not.toBeNull();
    http.expectOne(`${API}/api/auth/users`).flush(USERS);
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="users-error"]')).toBeNull();
    expect(picker().getAttribute('aria-disabled')).toBe('false');
  });

  it('does not retry a non-transient answer, and says the API did not answer on a network failure', async () => {
    http.expectOne(`${API}/api/auth/users`).error(new ProgressEvent('error'), { status: 0 });
    await new Promise((r) => setTimeout(r, 1600));
    http.expectOne(`${API}/api/auth/users`).error(new ProgressEvent('error'), { status: 0 });
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="users-error"]')?.textContent).toContain("the API didn't answer");

    el.querySelector<HTMLButtonElement>('[data-testid="users-retry"]')!.click();
    fail(404);
    await fixture.whenStable();
    http.expectNone(`${API}/api/auth/users`);
    expect(el.querySelector('[data-testid="users-error"]')?.textContent).toContain('HTTP 404');
  });
});
