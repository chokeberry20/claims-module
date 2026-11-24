import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';
import { App } from './app';
import { DemoUser, TokenResponse } from './core/api/models';
import { AuthService } from './core/auth/auth.service';
import { APP_CONFIG } from './core/config/app-config';
import { HealthService } from './core/health/health.service';

const API = 'https://api.test';
const USERS: DemoUser[] = [
  { userId: 'u1', userName: 'handler1', displayName: 'Hanna Handler', role: 'Handler' },
  { userId: 'u3', userName: 'supervisor1', displayName: 'Sam Supervisor', role: 'Supervisor' },
];

describe('App shell', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: HealthService, useValue: { check: () => of({ status: 'Healthy', detail: 'All checks passed' }) } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  async function signIn(user: DemoUser): Promise<void> {
    const done = firstValueFrom(auth.signIn(user.userName, 'from-the-form'));
    const response: TokenResponse = {
      accessToken: `jwt-${user.userName}`,
      tokenType: 'Bearer',
      expiresAt: new Date(Date.now() + 8 * 3600_000).toISOString(),
      userId: user.userId,
      displayName: user.displayName,
      role: user.role,
    };
    http.expectOne(`${API}/api/auth/token`).flush(response);
    await done;
  }

  it('renders the toolbar brand and no user while signed out', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.app-toolbar')?.textContent).toContain('Claims Module');
    expect(el.querySelector('[data-testid="current-user"]')).toBeNull();
  });

  it('shows the API health and base URL in the footer', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const footer = (fixture.nativeElement as HTMLElement).querySelector('.app-footer')!;

    expect(footer.querySelector('[data-status]')?.getAttribute('data-status')).toBe('Healthy');
    expect(footer.textContent).toContain('API: Healthy');
    expect(footer.textContent).toContain(API);
  });

  it('shows the signed-in user and role, and switching user changes the header', async () => {
    const fixture = TestBed.createComponent(App);
    const el = fixture.nativeElement as HTMLElement;
    const header = () => [
      el.querySelector('[data-testid="current-user"]')?.textContent?.trim(),
      el.querySelector('[data-testid="current-role"]')?.textContent?.trim(),
    ];

    await signIn(USERS[0]);
    await fixture.whenStable();
    http.expectOne(`${API}/api/auth/users`).flush(USERS);
    expect(header()).toEqual(['Hanna Handler', 'Handler']);

    await signIn(USERS[1]);
    await fixture.whenStable();
    expect(header()).toEqual(['Sam Supervisor', 'Supervisor']);

    expect(auth.switchTo('handler1')).toBe(true);
    await fixture.whenStable();
    expect(header()).toEqual(['Hanna Handler', 'Handler']);
    http.verify();
  });
});
