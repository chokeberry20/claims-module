import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { PolicySummary } from '../../core/api/models';
import { CLOCK } from '../../core/auth/auth.service';
import { APP_CONFIG } from '../../core/config/app-config';
import { apiInterceptors, IDEMPOTENCY_HEADER } from '../../core/http/interceptors';
import { NotificationService } from '../../core/ui/notification.service';
import { ConfirmService } from '../../shared/confirm-dialog';
import { FnolIntake } from './fnol-intake';
import {
  errorsByStep,
  filterCauseCodes,
  fnolWarnings,
  isInFuture,
  isLossInForce,
  localDateTimeToIso,
  stepForField,
  toCreateRequest,
} from './fnol-model';

const API = 'https://api.test';
const NOW = Date.parse('2026-11-24T12:00:00Z');
const POLICY: PolicySummary = {
  policyId: 'p-1',
  policyNumber: 'POL-2026-003001',
  clientName: 'Northwind Traders',
  effectiveDate: '2026-01-01',
  expirationDate: '2027-12-31',
  status: 'Active',
};

describe('FNOL rules', () => {
  it('treats more than five minutes ahead as future (BR-C-01, D-15)', () => {
    expect(isInFuture('2026-11-24T12:04:59Z', NOW)).toBe(false);
    expect(isInFuture('2026-11-24T12:05:01Z', NOW)).toBe(true);
    expect(isInFuture(null, NOW)).toBe(false);
  });

  it('compares the UTC loss day with the policy period, inclusive (S11.2 in-force badge)', () => {
    expect(isLossInForce(POLICY, '2026-01-01T00:00:00.000Z')).toBe(true);
    expect(isLossInForce(POLICY, '2027-12-31T23:59:00.000Z')).toBe(true);
    expect(isLossInForce(POLICY, '2025-12-31T23:59:00.000Z')).toBe(false);
    expect(isLossInForce({ ...POLICY, status: 'Cancelled' }, '2026-06-01T00:00:00.000Z')).toBe(false);
    expect(isLossInForce(null, '2026-06-01T00:00:00.000Z')).toBeNull();
  });

  it('lists the warnings to confirm: no policy, outside the period, no risk objects', () => {
    const base = { unknownPolicy: false, policy: POLICY, lossDateIso: '2026-06-01T10:00:00.000Z', parties: [], riskObjects: [] };
    expect(fnolWarnings(base)).toEqual(['No risk objects were added.']);
    expect(fnolWarnings({ ...base, unknownPolicy: true })[0]).toContain('No policy is linked');
    expect(fnolWarnings({ ...base, lossDateIso: '2025-06-01T10:00:00.000Z' })[0]).toContain('outside the policy period');
    expect(
      fnolWarnings({ ...base, riskObjects: [{ assetType: 'Vehicle', assetDescription: 'Car', isPrimary: true }] }),
    ).toEqual([]);
  });

  it('places server errors on the step their field belongs to', () => {
    expect(stepForField('LossDescription')).toBe(0);
    expect(stepForField('Parties[0].FirstName')).toBe(1);
    expect(stepForField('RiskObjects')).toBe(1);
    expect(stepForField('InitialReserve.Amount')).toBe(2);
    expect(stepForField('General')).toBe(2);

    const steps = errorsByStep({
      type: 'ValidationError',
      title: 't',
      status: 422,
      errors: { LossDate: ['Loss date cannot be in the future.'], 'Parties[0].LastName': ['Last name is required.'] },
    });
    expect(steps).toEqual([['Loss date: Loss date cannot be in the future.'], ['Parties 1 last name: Last name is required.'], []]);
    expect(errorsByStep({ type: 'Conflict', title: 'Conflict.', status: 409, detail: 'Try again.' })[2]).toEqual(['Conflict. Try again.']);
  });

  it('builds the request: unknown policy sends no policy and no reserve, blanks become null', () => {
    const request = toCreateRequest({
      policy: POLICY,
      unknownPolicy: true,
      lossDateIso: '2026-11-20T08:00:00.000Z',
      causeOfLossCode: 'AUTO-COLL',
      lossDescription: '  Rear-ended at a red light on Main St.  ',
      lossLocation: '  ',
      estimatedLossAmount: null,
      parties: [],
      riskObjects: [],
      initialReserve: { componentType: 'Indemnity', amount: 5000, changeReason: 'Estimate' },
    });
    expect(request.policyId).toBeNull();
    expect(request.initialReserve).toBeNull();
    expect(request.lossLocation).toBeNull();
    expect(request.lossDescription).toBe('Rear-ended at a red light on Main St.');
  });

  it('converts the datetime-local value to UTC ISO', () => {
    const iso = localDateTimeToIso('2026-11-20T08:30');
    expect(iso).toBe(new Date('2026-11-20T08:30').toISOString());
    expect(localDateTimeToIso('')).toBeNull();
  });
});

describe('FnolIntake (S11.2)', () => {
  let fixture: ComponentFixture<FnolIntake>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const confirm = vi.fn();
  const success = vi.fn();
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    confirm.mockReset();
    success.mockReset();
    await TestBed.configureTestingModule({
      imports: [FnolIntake],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors(apiInterceptors)),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: CLOCK, useValue: () => NOW },
        { provide: ConfirmService, useValue: { confirm } },
        { provide: NotificationService, useValue: { success, show: vi.fn() } },
      ],
    }).compileComponents();
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FnolIntake);
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    http.expectOne(`${API}/api/reference/cause-of-loss-codes`).flush([
      { code: 'AUTO-COLL', name: 'Vehicle collision', perilCategory: 'Auto', isActive: true, sortOrder: 1 },
    ]);
  });

  afterEach(() => http.verify());

  // The component's forms, reached the way the template binds them.
  type Internals = {
    step1: { patchValue(v: object): void; valid: boolean };
    reserve: { patchValue(v: object): void };
    addParty(p: object): void;
    addRiskObject(r: object): void;
    submit(): void;
  };
  const c = () => fixture.componentInstance as unknown as Internals;

  function fillStep1(): void {
    c().step1.patchValue({
      policy: POLICY,
      lossDate: '2026-11-20T08:30',
      causeOfLossCode: 'AUTO-COLL',
      lossDescription: 'Rear-ended at a red light on Main Street.',
      lossLocation: 'Main St & 3rd Ave',
    });
  }

  it('shows the description counter and the in-force badge as the user types', async () => {
    c().step1.patchValue({ lossDescription: 'Too short', policy: POLICY, lossDate: '2026-11-20T08:30' });
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="description-counter"]')?.textContent).toContain('9 / 20');
    expect(el.querySelector('[data-testid="in-force"]')?.textContent).toContain('In force');
  });

  it('rejects a future loss date', async () => {
    fillStep1();
    c().step1.patchValue({ lossDate: '2026-12-31T08:30' });
    expect(c().step1.valid).toBe(false);
  });

  it('keeps "Create claim" disabled until there is a Claimant', async () => {
    fillStep1();
    await fixture.whenStable();
    const create = () => el.querySelector<HTMLButtonElement>('[data-testid="create-claim"]')!;
    expect(create().disabled).toBe(true);

    c().addParty({ role: 'Claimant', type: 'Person', firstName: 'Jane', lastName: 'Doe', email: null, phone: null });
    await fixture.whenStable();
    expect(create().disabled).toBe(false);
  });

  it('asks to confirm warnings, then posts the claim with an Idempotency-Key and opens it', async () => {
    confirm.mockReturnValue(of({ text: null }));
    fillStep1();
    c().addParty({ role: 'Claimant', type: 'Person', firstName: 'Jane', lastName: 'Doe', email: null, phone: null });
    c().reserve.patchValue({ include: true, componentType: 'Indemnity', amount: 50_000, changeReason: 'Repair quote' });
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="authority"]')?.textContent).toContain('Supervisor approval required');

    c().submit();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ items: ['No risk objects were added.'] }));

    const req = http.expectOne(`${API}/api/claims`);
    expect(req.request.headers.get(IDEMPOTENCY_HEADER)).toMatch(/^[0-9a-f-]{36}$/);
    expect(req.request.body).toMatchObject({
      policyId: 'p-1',
      causeOfLossCode: 'AUTO-COLL',
      parties: [{ role: 'Claimant', firstName: 'Jane', lastName: 'Doe' }],
      riskObjects: [],
      initialReserve: { componentType: 'Indemnity', amount: 50_000, changeReason: 'Repair quote' },
    });
    req.flush({
      claimId: 'c-1',
      claimNumber: 'CLM-2026-0000009',
      status: 'Draft',
      validationIssues: [],
      initialReserve: { approvalStatus: 'PendingApproval', requiredApprovalLevel: 'Supervisor' },
    });

    expect(success).toHaveBeenCalledWith('Claim CLM-2026-0000009 created. The initial reserve awaits supervisor approval.');
    expect(navigate).toHaveBeenCalledWith(['/claims', 'c-1']);
  });

  it('does nothing when the warning dialog is cancelled', () => {
    confirm.mockReturnValue(of(undefined));
    fillStep1();
    c().addParty({ role: 'Claimant', type: 'Person', firstName: 'Jane', lastName: 'Doe' });
    c().submit();
    http.expectNone(`${API}/api/claims`);
  });

  it('shows server errors inline on their step and reuses the key on resubmit', async () => {
    fillStep1();
    c().addParty({ role: 'Claimant', type: 'Person', firstName: 'Jane', lastName: 'Doe' });
    c().addRiskObject({ assetType: 'Vehicle', assetDescription: 'Toyota Corolla', isPrimary: true });
    c().submit();
    const first = http.expectOne(`${API}/api/claims`);
    const key = first.request.headers.get(IDEMPOTENCY_HEADER);
    first.flush(
      {
        type: 'ValidationError',
        title: 'One or more validation errors occurred.',
        status: 422,
        errors: { CauseOfLossCode: ['Cause of loss code is not recognised or is inactive.'] },
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="step1-errors"]')?.textContent).toContain('Cause of loss code: Cause of loss code is not recognised');

    c().submit();
    const second = http.expectOne(`${API}/api/claims`);
    expect(second.request.headers.get(IDEMPOTENCY_HEADER)).toBe(key);
    second.flush({ claimId: 'c-2', claimNumber: 'CLM-2026-0000010', status: 'Draft', validationIssues: [], initialReserve: null });
  });

  it('switches the reserve off for an unknown policy', async () => {
    fillStep1();
    c().reserve.patchValue({ include: true, amount: 100 });
    c().step1.patchValue({ unknownPolicy: true });
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="reserve-amount"]')).toBeNull();
  });

  it('cause of loss: typing filters the list, only a listed code is accepted (S11.2 searchable dropdown)', async () => {
    const search = (fixture.componentInstance as unknown as { causeSearch: { setValue(v: string): void } }).causeSearch;
    const code = () => (c().step1 as unknown as { controls: { causeOfLossCode: { value: string } } }).controls.causeOfLossCode.value;

    search.setValue('collis');
    await fixture.whenStable();
    expect(code()).toBe('');

    search.setValue('AUTO-COLL'); // what the autocomplete writes when an option is chosen
    await fixture.whenStable();
    expect(code()).toBe('AUTO-COLL');
    const input = el.querySelector<HTMLInputElement>('[data-testid="cause-of-loss"]')!;
    expect(input.value).toBe('Vehicle collision (AUTO-COLL)');

    search.setValue('Vehicle collision (AUTO-COLL) x'); // edited after choosing: no longer a valid choice
    await fixture.whenStable();
    expect(code()).toBe('');
  });
});

describe('filterCauseCodes', () => {
  const codes = [
    { code: 'COL-FIRE', name: 'Fire', perilCategory: 'Property' },
    { code: 'COL-VEH-COL', name: 'Vehicle collision', perilCategory: 'Auto' },
    { code: 'COL-THEFT', name: 'Theft', perilCategory: 'Crime' },
  ];

  it('matches name, code or peril, case-insensitively', () => {
    expect(filterCauseCodes(codes, 'fire').map((c) => c.code)).toEqual(['COL-FIRE']);
    expect(filterCauseCodes(codes, 'veh-col').map((c) => c.code)).toEqual(['COL-VEH-COL']);
    expect(filterCauseCodes(codes, 'CRIME').map((c) => c.code)).toEqual(['COL-THEFT']);
    expect(filterCauseCodes(codes, 'flood')).toEqual([]);
  });

  it('lists everything for empty text or an already selected code', () => {
    expect(filterCauseCodes(codes, '  ')).toHaveLength(3);
    expect(filterCauseCodes(codes, 'COL-FIRE')).toHaveLength(3);
  });
});
