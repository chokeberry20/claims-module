import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from '../config/app-config';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let service: HealthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: 'https://api.test' } },
      ],
    });
    service = TestBed.inject(HealthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reports Healthy from the API health report', async () => {
    const result = firstValueFrom(service.check());
    http.expectOne('https://api.test/health').flush({
      status: 'Healthy',
      entries: { database: { status: 'Healthy' }, migrations: { status: 'Healthy' } },
    });

    expect(await result).toEqual({ status: 'Healthy', detail: 'All checks passed' });
  });

  it('reads the 503 body of an unhealthy API and names the failing checks', async () => {
    const result = firstValueFrom(service.check());
    http.expectOne('https://api.test/health').flush(
      { status: 'Unhealthy', entries: { database: { status: 'Healthy' }, migrations: { status: 'Unhealthy' } } },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(await result).toEqual({ status: 'Unhealthy', detail: 'Failing: migrations' });
  });

  it('reports Unreachable when there is no response (API down or CORS blocked)', async () => {
    const result = firstValueFrom(service.check());
    http.expectOne('https://api.test/health').error(new ProgressEvent('error'), { status: 0 });

    expect(await result).toEqual({ status: 'Unreachable', detail: 'No response from the API' });
  });

  it('reports Unreachable with the HTTP status when the body is not a health report', async () => {
    const result = firstValueFrom(service.check());
    http.expectOne('https://api.test/health').flush('Not found', { status: 404, statusText: 'Not Found' });

    expect(await result).toEqual({ status: 'Unreachable', detail: 'HTTP 404' });
  });
});
