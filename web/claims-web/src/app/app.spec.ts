import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { App } from './app';
import { APP_CONFIG } from './core/config/app-config';
import { HealthService } from './core/health/health.service';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: 'https://api.test' } },
        { provide: HealthService, useValue: { check: () => of({ status: 'Healthy', detail: 'All checks passed' }) } },
      ],
    }).compileComponents();
  });

  it('renders the toolbar brand', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.app-toolbar')?.textContent).toContain('Claims Module');
  });

  it('shows the API health and base URL in the footer', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const footer = (fixture.nativeElement as HTMLElement).querySelector('.app-footer')!;

    expect(footer.querySelector('[data-status]')?.getAttribute('data-status')).toBe('Healthy');
    expect(footer.textContent).toContain('API: Healthy');
    expect(footer.textContent).toContain('https://api.test');
  });
});
