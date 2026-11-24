import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { APP_CONFIG } from '../../core/config/app-config';
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
