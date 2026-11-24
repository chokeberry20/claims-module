import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ClaimDetail, Party } from '../../core/api/models';
import { APP_CONFIG } from '../../core/config/app-config';
import { NotificationService } from '../../core/ui/notification.service';
import { ConfirmService } from '../../shared/confirm-dialog';
import { OverviewTab } from './overview-tab';
import { PartiesTab, removeBlockedReason } from './parties-tab';

const API = 'https://api.test';

function party(overrides: Partial<Party>): Party {
  return {
    partyId: 'p-1',
    role: 'Claimant',
    type: 'Person',
    firstName: 'Jane',
    lastName: 'Doe',
    companyName: null,
    displayName: 'Jane Doe',
    email: 'jane@example.com',
    phone: null,
    notes: null,
    isActive: true,
    removedAt: null,
    ...overrides,
  };
}

function claim(overrides: Partial<ClaimDetail> = {}): ClaimDetail {
  return {
    claimId: 'c-1',
    claimNumber: 'CLM-2026-0000004',
    status: 'Open',
    severity: 'Critical',
    claimType: 'Auto',
    reportedDate: '2026-11-22T09:00:00Z',
    policyId: 'pol-1',
    policyNumber: 'POL-2026-003001',
    clientName: 'Northwind',
    assignedHandlerId: 'u-h1',
    assignedHandlerName: 'Hannah Handler',
    notes: 'Called the insured.',
    closedAt: null,
    reopenedAt: null,
    reopenCount: 0,
    isSlaBreached: false,
    reserveLimitOverride: false,
    lossEvent: {
      lossDate: '2026-11-20T09:30:00Z',
      lossDescription: 'Rear-ended at a red light on Main Street.',
      lossLocation: 'Main St & 3rd Ave',
      causeOfLossCode: 'COL-VEH-COL',
      causeOfLossName: 'Vehicle collision',
      estimatedLossAmount: 12_500,
      reportDate: '2026-11-22T09:00:00Z',
      policeReportNumber: null,
    },
    parties: [
      party({}),
      party({ partyId: 'p-2', role: 'Witness', displayName: 'Bob Smith', email: null, phone: '555-0100' }),
      party({ partyId: 'p-3', role: 'Claimant', displayName: 'Old Claimant', isActive: false, removedAt: '2026-11-21T00:00:00Z' }),
    ],
    riskObjects: [],
    validationIssues: [],
    reserves: { approvedGross: 50_000, recoveries: 0, hasPendingApproval: false, components: [] },
    documents: [],
    ...overrides,
  };
}

const notifications = { success: vi.fn(), show: vi.fn(), info: vi.fn(), warning: vi.fn() };

async function setup<T>(component: new (...args: never[]) => T, confirm = vi.fn()) {
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
      { provide: NotificationService, useValue: notifications },
      { provide: ConfirmService, useValue: { confirm } },
    ],
  }).compileComponents();
  return TestBed.inject(HttpTestingController);
}

describe('OverviewTab (S11.3 tab 1)', () => {
  let fixture: ComponentFixture<OverviewTab>;
  let http: HttpTestingController;
  let el: HTMLElement;

  async function render(c: ClaimDetail): Promise<void> {
    http = await setup(OverviewTab);
    fixture = TestBed.createComponent(OverviewTab);
    fixture.componentRef.setInput('claim', c);
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  const notes = () => el.querySelector<HTMLTextAreaElement>('[data-testid="notes"]')!;
  const save = () => el.querySelector<HTMLButtonElement>('[data-testid="save-notes"]');
  const type = async (text: string) => {
    notes().value = text;
    notes().dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };

  it('shows the loss event, the severity badge and the estimated amount', async () => {
    await render(claim());
    expect(el.textContent).toContain('Rear-ended at a red light on Main Street.');
    expect(el.textContent).toContain('Main St & 3rd Ave');
    expect(el.textContent).toContain('Vehicle collision (COL-VEH-COL)');
    expect(el.querySelector('[data-testid="overview-severity"] .badge')?.getAttribute('data-tone')).toBe('orange');
    expect(el.querySelector('[data-testid="overview-estimated"]')?.textContent).toBe('$12,500.00');
    expect(notes().value).toBe('Called the insured.');
  });

  it('saves edited notes through PUT /notes and tells the page to reload', async () => {
    await render(claim());
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    expect(save()!.disabled).toBe(true);

    await type('Called the insured. Garage estimate expected Friday.  ');
    expect(save()!.disabled).toBe(false);
    save()!.click();

    const req = http.expectOne(`${API}/api/claims/c-1/notes`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ notes: 'Called the insured. Garage estimate expected Friday.' });
    req.flush({ claimId: 'c-1', status: 'Open', validationIssues: [] });
    expect(changed).toHaveBeenCalledOnce();
  });

  it('shows a server error inline', async () => {
    await render(claim());
    await type('New notes');
    save()!.click();
    http.expectOne(`${API}/api/claims/c-1/notes`).flush(
      { type: 'ValidationError', title: 't', status: 422, errors: { Status: ['The claim is Closed and can no longer be changed.'] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="notes-error"]')?.textContent).toContain('can no longer be changed');
  });

  it('keeps unsaved edits when the claim reloads, and takes new saved notes otherwise', async () => {
    await render(claim());
    await type('Unsaved draft');
    fixture.componentRef.setInput('claim', claim({ notes: 'Changed elsewhere' }));
    await fixture.whenStable();
    expect(notes().value).toBe('Unsaved draft');

    await type('Changed elsewhere');
    fixture.componentRef.setInput('claim', claim({ notes: 'Newest' }));
    await fixture.whenStable();
    expect(notes().value).toBe('Newest');
  });

  it('is read-only for a closed claim', async () => {
    await render(claim({ status: 'Closed', closedAt: '2026-11-24T10:00:00Z' }));
    expect(notes().disabled).toBe(true);
    expect(save()).toBeNull();
    expect(el.textContent).toContain('notes can no longer be changed');
  });
});

describe('PartiesTab (S11.3 tab 2)', () => {
  let fixture: ComponentFixture<PartiesTab>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const confirm = vi.fn();

  async function render(c: ClaimDetail): Promise<void> {
    confirm.mockReset();
    http = await setup(PartiesTab, confirm);
    fixture = TestBed.createComponent(PartiesTab);
    fixture.componentRef.setInput('claim', c);
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  const removeButton = (id: string) => el.querySelector<HTMLButtonElement>(`[data-testid="remove-${id}"]`);

  it('lists every party with role badge, contact and active/removed status', async () => {
    await render(claim());
    const rows = el.querySelectorAll('tr[data-testid^="party-"]');
    expect(rows).toHaveLength(3);
    expect(el.querySelector('[data-testid="party-p-1"]')?.textContent).toContain('jane@example.com');
    expect(el.querySelector('[data-testid="party-p-2"]')?.textContent).toContain('555-0100');
    expect(el.querySelector('[data-testid="party-p-3"]')?.textContent).toContain('Removed');
    expect(el.querySelector('[data-testid="party-p-3"]')?.classList).toContain('inactive');
  });

  it('disables Remove for the last active Claimant (BR-P-01), not for others', async () => {
    await render(claim());
    expect(removeButton('p-1')!.disabled).toBe(true);
    expect(removeButton('p-2')!.disabled).toBe(false);
    expect(removeButton('p-3')).toBeNull();
    expect(removeBlockedReason(claim(), claim().parties[0])).toContain('last Claimant');

    const twoClaimants = claim({ parties: [party({}), party({ partyId: 'p-4', displayName: 'Second Claimant' })] });
    expect(removeBlockedReason(twoClaimants, twoClaimants.parties[0])).toBeNull();
  });

  it('removes a party after confirmation', async () => {
    await render(claim());
    confirm.mockReturnValue(of({ text: null }));
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);

    removeButton('p-2')!.click();
    const req = http.expectOne(`${API}/api/claims/c-1/parties/p-2`);
    expect(req.request.method).toBe('DELETE');
    req.flush({ claimId: 'c-1', status: 'Open', validationIssues: [] });
    expect(changed).toHaveBeenCalledOnce();
  });

  it('adds a party through the inline form with an Idempotency-Key and closes the form', async () => {
    await render(claim());
    el.querySelector<HTMLButtonElement>('[data-testid="add-party"]')!.click();
    await fixture.whenStable();
    const input = (id: string, value: string) => {
      const field = el.querySelector<HTMLInputElement>(`[data-testid="${id}"]`)!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
    };
    input('party-first-name', 'Tom');
    input('party-last-name', 'Brown');
    el.querySelector<HTMLButtonElement>('[data-testid="party-add"]')!.click();

    const req = http.expectOne(`${API}/api/claims/c-1/parties`);
    expect(req.request.body).toMatchObject({ role: 'Claimant', type: 'Person', firstName: 'Tom', lastName: 'Brown' });
    req.flush({ claimId: 'c-1', childId: 'p-9', validationIssues: [] });
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="party-form"]')).toBeNull();
  });

  it('keeps the form open with the server error inline', async () => {
    await render(claim());
    el.querySelector<HTMLButtonElement>('[data-testid="add-party"]')!.click();
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('[data-testid="party-type-company"] button')!.click();
    await fixture.whenStable();
    const company = el.querySelector<HTMLInputElement>('[data-testid="party-company-name"]')!;
    company.value = 'Acme';
    company.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('[data-testid="party-add"]')!.click();
    http.expectOne(`${API}/api/claims/c-1/parties`).flush(
      { type: 'ValidationError', title: 't', status: 422, errors: { Email: ['Email address is not valid.'] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await fixture.whenStable();
    expect(el.querySelector('[data-testid="party-error"]')?.textContent).toContain('Email: Email address is not valid.');
    expect(el.querySelector('[data-testid="party-form"]')).not.toBeNull();
  });

  it('offers no changes on a withdrawn claim', async () => {
    await render(claim({ status: 'Withdrawn' }));
    expect(el.querySelector<HTMLButtonElement>('[data-testid="add-party"]')!.disabled).toBe(true);
    expect(removeButton('p-2')!.disabled).toBe(true);
  });
});
