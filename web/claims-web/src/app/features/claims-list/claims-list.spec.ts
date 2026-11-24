import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ClaimSummary, PagedResult } from '../../core/api/models';
import { APP_CONFIG } from '../../core/config/app-config';
import { ClaimsList } from './claims-list';

const API = 'https://api.test';

function claim(n: number, overrides: Partial<ClaimSummary> = {}): ClaimSummary {
  return {
    claimId: `id-${n}`,
    claimNumber: `CLM-2026-000000${n}`,
    status: 'Open',
    severity: 'Standard',
    lossDate: '2026-11-20T09:30:00Z',
    reportedDate: '2026-11-21T10:00:00Z',
    causeOfLossCode: 'AUTO-COLL',
    causeOfLossName: 'Vehicle collision',
    policyNumber: 'POL-2026-003001',
    clientName: 'Northwind Traders',
    primaryClaimantName: 'Jane Doe',
    assignedHandlerId: 'u1',
    assignedHandlerName: 'Hanna Handler',
    isSlaBreached: false,
    totalReserves: 50_000,
    ...overrides,
  };
}

function page(items: ClaimSummary[], totalCount = items.length): PagedResult<ClaimSummary> {
  return { items, page: 1, pageSize: 20, totalCount };
}

describe('ClaimsList (S11.1, minimal)', () => {
  let fixture: ComponentFixture<ClaimsList>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ClaimsList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: APP_CONFIG, useValue: { apiBaseUrl: API } }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClaimsList);
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    http.expectOne(`${API}/api/reference/cause-of-loss-codes`).flush([
      { code: 'COL-FIRE', name: 'Fire', perilCategory: 'Property', isActive: true, sortOrder: 1 },
      { code: 'COL-VEH-COL', name: 'Vehicle collision', perilCategory: 'Auto', isActive: true, sortOrder: 2 },
    ]);
  });

  afterEach(() => http.verify());

  function expectList(): TestRequest {
    return http.expectOne((r) => r.url === `${API}/api/claims`);
  }

  it('shows the seven S11.1 columns with a coloured status badge', async () => {
    const req = expectList();
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush(page([claim(1), claim(2, { status: 'Draft', policyNumber: null, clientName: null, totalReserves: 0 })]));
    await fixture.whenStable();

    const headers = Array.from(el.querySelectorAll('th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Claim number', 'Policy number', 'Client name', 'Loss date', 'Cause of loss', 'Status', 'Total reserves']);
    const first = el.querySelector('[data-testid="claim-row-CLM-2026-0000001"]')!;
    expect(first.textContent).toContain('POL-2026-003001');
    expect(first.textContent).toContain('Vehicle collision');
    expect(first.textContent).toContain('$50,000.00');
    expect(first.querySelector('.badge')?.getAttribute('data-tone')).toBe('blue');
    expect(el.querySelector('[data-testid="claim-row-CLM-2026-0000002"]')?.textContent).toContain('—');
  });

  it('opens the claim when a row is clicked', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    expectList().flush(page([claim(1)]));
    await fixture.whenStable();

    el.querySelector<HTMLElement>('[data-testid="claim-row-CLM-2026-0000001"]')!.click();
    expect(navigate).toHaveBeenCalledWith(['/claims', 'id-1']);
  });

  it('shows a friendly empty state', async () => {
    expectList().flush(page([]));
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="empty-state"]')?.textContent).toContain('No claims match the current filter.');
  });

  it('filters by several statuses and goes back to page 1', async () => {
    expectList().flush(page([claim(1)], 45));
    await fixture.whenStable();

    (fixture.componentInstance as unknown as { statusFilter: { setValue(v: string[]): void } }).statusFilter.setValue(['Draft', 'Open']);
    await fixture.whenStable();
    const req = expectList();
    expect(req.request.params.getAll('status')).toEqual(['Draft', 'Open']);
    expect(req.request.params.get('page')).toBe('1');
    req.flush(page([]));
  });

  it('shows the total count and pages through it', async () => {
    expectList().flush(page([claim(1)], 45));
    await fixture.whenStable();
    expect(el.querySelector('.mat-mdc-paginator-range-label')?.textContent).toContain('of 45');

    el.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
    await fixture.whenStable();
    const req = expectList();
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ ...page([claim(3)], 45), page: 2 });
  });

  describe('filter bar (S11.1)', () => {
    type Filters = {
      filters: {
        controls: Record<'status' | 'dateFrom' | 'dateTo' | 'handler' | 'cause', { setValue(v: unknown): void }>;
      };
    };
    const f = () => (fixture.componentInstance as unknown as Filters).filters.controls;

    beforeEach(async () => {
      expectList().flush(page([claim(1)], 45));
      await fixture.whenStable();
    });

    it('filters by loss date range and cause of loss at once, back on page 1', async () => {
      el.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
      await fixture.whenStable();
      expectList().flush({ ...page([claim(3)], 45), page: 2 });

      f().dateFrom.setValue('2026-11-01');
      await fixture.whenStable();
      expectList().flush(page([claim(1)]));
      f().dateTo.setValue('2026-11-30');
      await fixture.whenStable();
      expectList().flush(page([claim(1)]));
      f().cause.setValue('COL-VEH-COL');
      await fixture.whenStable();
      const req = expectList();
      expect(req.request.params.get('dateFrom')).toBe('2026-11-01');
      expect(req.request.params.get('dateTo')).toBe('2026-11-30');
      expect(req.request.params.get('causeOfLossCode')).toBe('COL-VEH-COL');
      expect(req.request.params.get('page')).toBe('1');
      req.flush(page([claim(1)]));
    });

    it('searches the assigned handler by text after a pause in typing', async () => {
      f().handler.setValue('han');
      f().handler.setValue('hannah ');
      http.expectNone((r) => r.url === `${API}/api/claims`);
      await new Promise((r) => setTimeout(r, 350));
      await fixture.whenStable();
      const req = expectList();
      expect(req.request.params.get('assignedHandler')).toBe('hannah');
      req.flush(page([]));
    });

    it('refuses a reversed date range without querying', async () => {
      f().dateFrom.setValue('2026-12-01');
      await fixture.whenStable();
      expectList().flush(page([claim(1)]));
      f().dateTo.setValue('2026-11-01');
      await fixture.whenStable();
      http.expectNone((r) => r.url === `${API}/api/claims`);
      expect(el.querySelector('[data-testid="date-range-error"]')).not.toBeNull();
    });

    it('lists the cause codes, and Clear resets every filter in one query', async () => {
      el.querySelector<HTMLElement>('[data-testid="cause-filter"]')!.click();
      await fixture.whenStable();
      expect(Array.from(document.querySelectorAll('mat-option')).map((o) => o.textContent?.trim())).toEqual([
        'Any cause',
        'Fire',
        'Vehicle collision',
      ]);
      document.querySelectorAll<HTMLElement>('mat-option')[1].click();
      await fixture.whenStable();
      expectList().flush(page([claim(1)]));

      f().status.setValue(['Open']);
      await fixture.whenStable();
      expectList().flush(page([claim(1)]));
      await fixture.whenStable();
      el.querySelector<HTMLButtonElement>('[data-testid="clear-filters"]')!.click();
      await fixture.whenStable();
      const reqs = http.match((r) => r.url === `${API}/api/claims`);
      const last = reqs[reqs.length - 1];
      for (const key of ['status', 'dateFrom', 'dateTo', 'assignedHandler', 'causeOfLossCode']) {
        expect(last.request.params.has(key)).toBe(false);
      }
      expect(last.request.params.get('page')).toBe('1');
      reqs.forEach((r) => r.flush(page([claim(1)])));
      expect(el.querySelector<HTMLButtonElement>('[data-testid="clear-filters"]')!.disabled).toBe(true);
    });
  });
});
