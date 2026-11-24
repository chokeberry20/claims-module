import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClaimDetail as ClaimDetailDto, ClaimReserves, ReserveTransaction, TransitionOption, UserRole } from '../../core/api/models';
import { AuthService, hasRoleAtLeast } from '../../core/auth/auth.service';
import { APP_CONFIG } from '../../core/config/app-config';
import { NotificationService } from '../../core/ui/notification.service';
import { ConfirmService } from '../../shared/confirm-dialog';
import { auditTone } from './audit-tab';
import { ClaimDetail } from './claim-detail';
import { uploadProblem } from './documents-tab';
import { awaitingPosting, ReservesTab, reserveDelta, transactionTypesFor } from './reserves-tab';
import { blockedReason, canOpen, closeChecklist, planTransition } from './transition-plan';

const API = 'https://api.test';

function option(overrides: Partial<TransitionOption>): TransitionOption {
  return {
    targetStatus: 'Open',
    requiredRole: 'Handler',
    hasRequiredRole: true,
    requiresReason: false,
    isAllowed: true,
    canProceed: true,
    blockingConditions: [],
    confirmationsRequired: [],
    ...overrides,
  };
}

function claim(overrides: Partial<ClaimDetailDto> = {}): ClaimDetailDto {
  return {
    claimId: 'c-1',
    claimNumber: 'CLM-2026-0000004',
    status: 'Open',
    severity: 'Standard',
    claimType: 'Auto',
    reportedDate: '2026-11-22T09:00:00Z',
    policyId: 'p-1',
    policyNumber: 'POL-2026-003001',
    clientName: 'Northwind',
    assignedHandlerId: 'u-h1',
    assignedHandlerName: 'Hannah Handler',
    notes: null,
    closedAt: null,
    reopenedAt: null,
    reopenCount: 0,
    isSlaBreached: false,
    reserveLimitOverride: false,
    lossEvent: {
      lossDate: '2026-11-20T09:30:00Z',
      lossDescription: 'Rear-ended at a red light.',
      lossLocation: null,
      causeOfLossCode: 'AUTO-COLL',
      causeOfLossName: 'Vehicle collision',
      estimatedLossAmount: null,
      reportDate: '2026-11-22T09:00:00Z',
      policeReportNumber: null,
    },
    parties: [],
    riskObjects: [],
    validationIssues: [],
    reserves: {
      approvedGross: 0,
      recoveries: 0,
      hasPendingApproval: true,
      components: [
        {
          reserveComponentId: 'rc-1',
          componentType: 'Indemnity',
          currentAmount: 0,
          status: 'Active',
          pendingTransactionId: 't-1',
          pendingAmount: 50_000,
        },
      ],
    },
    documents: [],
    ...overrides,
  };
}

function txn(overrides: Partial<ReserveTransaction> = {}): ReserveTransaction {
  return {
    transactionId: 't-1',
    reserveComponentId: 'rc-1',
    componentType: 'Indemnity',
    transactionType: 'Add',
    changeSequence: 1,
    amount: 50_000,
    previousBalance: 0,
    newBalance: 50_000,
    changeReason: 'Repair quote',
    approvalStatus: 'PendingApproval',
    requiredApprovalLevel: 'Supervisor',
    requiresLimitOverride: false,
    submittedByUserId: 'u-h1',
    submittedByName: 'Hannah Handler',
    submittedAt: '2026-11-22T09:00:00Z',
    approvedByUserId: null,
    approvedAt: null,
    rejectedByUserId: null,
    rejectedAt: null,
    rejectionReason: null,
    postingStatus: 'Pending',
    postingJobId: null,
    postedAt: null,
    postingFailureReason: null,
    ...overrides,
  };
}

describe('transition plan (S11.3, D-27)', () => {
  it('acknowledges warnings when opening', () => {
    const plan = planTransition(
      option({ confirmationsRequired: [{ code: 'WARNINGS_NOT_ACKNOWLEDGED', message: 'No risk objects.', field: 'AcknowledgeWarnings' }] }),
    );
    expect(plan.dialog.items).toEqual(['No risk objects.']);
    expect(plan.dialog.input).toBeUndefined();
    expect(plan.toRequest(null)).toEqual({ targetStatus: 'Open', reason: null, closureJustification: null, acknowledgeWarnings: true });
  });

  it('asks for a closure justification for open balances (CC-04)', () => {
    const plan = planTransition(
      option({ targetStatus: 'Closed', confirmationsRequired: [{ code: 'CC-04', message: 'Open reserve balances remain.', field: 'ClosureJustification' }] }),
    );
    expect(plan.dialog.input?.label).toBe('Closure justification');
    expect(plan.toRequest('Paid')).toMatchObject({ targetStatus: 'Closed', closureJustification: 'Paid', reason: null });
  });

  it('asks for a reason when the transition requires one (Withdrawn, Reopened)', () => {
    const plan = planTransition(option({ targetStatus: 'Withdrawn', requiresReason: true }));
    expect(plan.dialog.input?.required).toBe(true);
    expect(plan.dialog.destructive).toBe(true);
    expect(plan.toRequest('Duplicate')).toMatchObject({ targetStatus: 'Withdrawn', reason: 'Duplicate', acknowledgeWarnings: false });
  });

  it('explains why an option is disabled: role first, then the blocking conditions', () => {
    expect(blockedReason(option({ hasRequiredRole: false, requiredRole: 'Supervisor' }))).toBe('Needs the Supervisor role or above.');
    expect(blockedReason(option({ blockingConditions: [{ code: 'CC-01', message: 'Pending approvals.', field: 'Reserves' }] }))).toBe(
      'Pending approvals.',
    );
    expect(blockedReason(option({}))).toBeNull();
  });
});

describe('reserve helpers', () => {
  it('computes the change in balance for Add, Adjust and Reverse (D-16)', () => {
    expect(reserveDelta('Add', 5000, 0)).toBe(5000);
    expect(reserveDelta('Adjust', 30_000, 50_000)).toBe(-20_000);
    expect(reserveDelta('Reverse', null, 50_000)).toBe(-50_000);
    expect(reserveDelta('Adjust', null, 50_000)).toBeNull();
  });

  it('offers Add for a new component and Adjust/Reverse for an existing one', () => {
    expect(transactionTypesFor(claim(), 'Indemnity')).toEqual(['Adjust', 'Reverse']);
    expect(transactionTypesFor(claim(), 'Expense')).toEqual(['Add']);
  });

  it('knows which postings are still to come', () => {
    expect(awaitingPosting(txn({ approvalStatus: 'Approved', postingStatus: 'Pending' }))).toBe(true);
    expect(awaitingPosting(txn({ approvalStatus: 'PendingApproval' }))).toBe(false);
    expect(awaitingPosting(txn({ approvalStatus: 'AutoApproved', postingStatus: 'Posted' }))).toBe(false);
  });
});

describe('documents and audit helpers', () => {
  it('checks the S13 allowlist and the 50 MB limit before uploading', () => {
    expect(uploadProblem({ name: 'report.PDF', size: 10 })).toBeNull();
    expect(uploadProblem({ name: 'run.exe', size: 10 })).toContain('only PDF');
    expect(uploadProblem({ name: 'big.png', size: 50 * 1024 * 1024 + 1 })).toContain('larger than 50 MB');
    expect(uploadProblem({ name: 'empty.txt', size: 0 })).toContain('empty');
  });

  it('colours audit events', () => {
    expect(auditTone('GL_POSTING_FAILED')).toBe('red');
    expect(auditTone('GL_POSTING_SIMULATED')).toBe('green');
    expect(auditTone('RESERVE_SUBMITTED')).toBe('purple');
    expect(auditTone('STATUS_CHANGED')).toBe('blue');
  });
});

describe('ReservesTab role gating (S3, S11.3)', () => {
  let fixture: ComponentFixture<ReservesTab>;
  let http: HttpTestingController;

  async function render(role: UserRole, userId: string, history: ReserveTransaction[]): Promise<HTMLElement> {
    const session = signal({ userId, role });
    await TestBed.configureTestingModule({
      imports: [ReservesTab],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: AuthService, useValue: { session, hasRole: (min: UserRole) => hasRoleAtLeast(session().role, min) } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReservesTab);
    fixture.componentRef.setInput('claim', claim());
    await fixture.whenStable();
    const reserves: ClaimReserves = { claimId: 'c-1', totals: claim().reserves, history };
    http.expectOne(`${API}/api/claims/c-1/reserves`).flush(reserves);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const buttons = (el: HTMLElement, id: string) => Array.from(el.querySelectorAll<HTMLButtonElement>(`[data-testid="${id}"]`));

  it('a handler sees no Approve/Reject, and Retract only on their own pending change', async () => {
    const el = await render('Handler', 'u-h1', [txn(), txn({ transactionId: 't-2', submittedByUserId: 'u-h2', changeSequence: 2 })]);
    expect(buttons(el, 'approve')).toHaveLength(0);
    expect(buttons(el, 'reject')).toHaveLength(0);
    expect(buttons(el, 'retract')).toHaveLength(1);
    expect(el.querySelector('[data-testid="card-Indemnity"]')?.textContent).toContain('Pending: +$50,000.00');
  });

  it('a supervisor can approve others’ changes but not their own (BR-R-03)', async () => {
    const el = await render('Supervisor', 'u-s1', [txn(), txn({ transactionId: 't-2', submittedByUserId: 'u-s1', changeSequence: 2 })]);
    const approve = buttons(el, 'approve');
    expect(approve.map((b) => b.disabled)).toEqual([false, true]);
  });

  it('shows the GL posting badge only for approved changes', async () => {
    const el = await render('Handler', 'u-h1', [txn({ approvalStatus: 'AutoApproved', postingStatus: 'Posted' })]);
    expect(el.querySelector('[data-testid="posting-t-1"] .badge')?.textContent).toBe('Posted');
  });

  it('offers Retry on a Failed posting to a supervisor only (D-28), and re-queues it', async () => {
    const failed = txn({ approvalStatus: 'Approved', postingStatus: 'Failed', postingFailureReason: 'GL unavailable' });
    const asHandler = await render('Handler', 'u-h1', [failed]);
    expect(buttons(asHandler, 'retry-posting')).toHaveLength(0);
    http.verify();
    TestBed.resetTestingModule();

    const el = await render('Supervisor', 'u-s1', [failed]);
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    buttons(el, 'retry-posting')[0].click();
    const req = http.expectOne(`${API}/api/claims/c-1/reserves/t-1/retry-posting`);
    expect(req.request.method).toBe('POST');
    req.flush({ reserveComponentId: 'rc-1', transactionId: 't-1', approvalStatus: 'Approved', postingStatus: 'Pending', componentBalance: 50_000 });
    expect(changed).toHaveBeenCalledOnce();
  });
});

describe('close pre-flight checklist (S11.3, S4.3)', () => {
  const close = (overrides: Partial<TransitionOption>) => option({ targetStatus: 'Closed', ...overrides });

  it('lists CC-01..CC-04 with the server state of each', () => {
    const items = closeChecklist(
      close({
        isAllowed: false,
        blockingConditions: [{ code: 'CC-01', message: 'Pending approvals.', field: 'Reserves' }],
        confirmationsRequired: [{ code: 'CC-04', message: 'Open balances remain.', field: 'ClosureJustification' }],
      }),
    );
    expect(items.map((i) => [i.label.slice(0, 5), i.state])).toEqual([
      ['CC-01', 'blocked'],
      ['CC-02', 'ok'],
      ['CC-03', 'ok'],
      ['CC-04', 'confirm'],
    ]);
    expect(items[0].detail).toBe('Pending approvals.');
  });

  it('opens the checklist even when Close is blocked, but nothing else that is blocked', () => {
    const blockedClose = close({ isAllowed: false, blockingConditions: [{ code: 'CC-02', message: 'x', field: 'ValidationIssues' }] });
    expect(canOpen(blockedClose)).toBe(true);
    expect(planTransition(blockedClose).dialog.confirmDisabled).toBe(true);
    expect(canOpen(close({ isAllowed: false, hasRequiredRole: false }))).toBe(false);
    expect(canOpen(option({ targetStatus: 'PendingPayment', isAllowed: false }))).toBe(false);

    const ready = planTransition(close({}));
    expect(ready.dialog.confirmDisabled).toBe(false);
    expect(ready.dialog.checklist?.every((i) => i.state === 'ok')).toBe(true);
  });
});

describe('ClaimDetail waive (D-07)', () => {
  async function render(role: UserRole, confirm = vi.fn()) {
    const session = signal({ userId: 'u-x', role });
    await TestBed.configureTestingModule({
      imports: [ClaimDetail],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: ConfirmService, useValue: { confirm } },
        { provide: NotificationService, useValue: { success: vi.fn(), info: vi.fn(), warning: vi.fn(), show: vi.fn() } },
        {
          provide: AuthService,
          useValue: { session, hasRole: (min: UserRole) => hasRoleAtLeast(role, min), users: () => of([]), hasValidToken: () => false },
        },
      ],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClaimDetail);
    fixture.componentRef.setInput('claimId', 'c-1');
    await fixture.whenStable();
    const issues = [
      { issueId: 'i-1', code: 'COL_INACTIVE', severity: 'Critical' as const, message: 'Cause of loss is inactive.', field: null, status: 'Open' as const },
      { issueId: 'i-2', code: 'NO_CLAIMANT', severity: 'Critical' as const, message: 'At least one Claimant is required.', field: null, status: 'Open' as const },
      { issueId: 'i-3', code: 'NO_RISK_OBJECTS', severity: 'Warning' as const, message: 'No risk objects.', field: null, status: 'Open' as const },
    ];
    http.expectOne(`${API}/api/claims/c-1`).flush(claim({ validationIssues: issues }));
    http.expectOne(`${API}/api/claims/c-1/transitions`).flush([]);
    await fixture.whenStable();
    http.match(() => true).forEach((r) => r.flush(r.request.url.endsWith('/reserves') ? { claimId: 'c-1', totals: claim().reserves, history: [] } : []));
    await fixture.whenStable();
    return { http, el: fixture.nativeElement as HTMLElement };
  }

  it('a supervisor can waive an open Critical issue, but not NO_CLAIMANT or a Warning', async () => {
    const { el } = await render('Supervisor');
    expect(el.querySelector('[data-testid="waive-COL_INACTIVE"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="waive-NO_CLAIMANT"]')).toBeNull();
    expect(el.querySelector('[data-testid="waive-NO_RISK_OBJECTS"]')).toBeNull();
  });

  it('a handler gets no waive button', async () => {
    const { el } = await render('Handler');
    expect(el.querySelector('[data-testid^="waive-"]')).toBeNull();
  });

  it('posts the waiver with the reason from the dialog', async () => {
    const confirm = vi.fn().mockReturnValue(of({ text: 'Legacy code accepted by underwriting' }));
    const { http, el } = await render('Manager', confirm);
    el.querySelector<HTMLButtonElement>('[data-testid="waive-COL_INACTIVE"]')!.click();
    const req = http.expectOne(`${API}/api/claims/c-1/validation-issues/i-1/waive`);
    expect(req.request.body).toEqual({ reason: 'Legacy code accepted by underwriting' });
    req.flush({ claimId: 'c-1', status: 'Draft', validationIssues: [] });
    http.expectOne(`${API}/api/claims/c-1`).flush(claim());
    http.expectOne(`${API}/api/claims/c-1/transitions`).flush([]);
  });
});

describe('ClaimDetail status change', () => {
  it('closes with the justification typed in the dialog, then reloads', async () => {
    const confirm = vi.fn().mockReturnValue(of({ text: 'Paid by the garage' }));
    await TestBed.configureTestingModule({
      imports: [ClaimDetail],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: API } },
        { provide: ConfirmService, useValue: { confirm } },
        { provide: NotificationService, useValue: { success: vi.fn(), info: vi.fn(), warning: vi.fn(), show: vi.fn() } },
      ],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClaimDetail);
    fixture.componentRef.setInput('claimId', 'c-1');
    await fixture.whenStable();

    http.expectOne(`${API}/api/auth/users`).flush([]);
    const close = option({
      targetStatus: 'Closed',
      confirmationsRequired: [{ code: 'CC-04', message: 'Open reserve balances remain.', field: 'ClosureJustification' }],
    });
    http.expectOne(`${API}/api/claims/c-1`).flush(claim({ reserves: { ...claim().reserves, components: [] } }));
    http.expectOne(`${API}/api/claims/c-1/transitions`).flush([close]);
    await fixture.whenStable();
    http.match(() => true).forEach((r) => r.flush(r.request.url.endsWith('/reserves') ? { claimId: 'c-1', totals: claim().reserves, history: [] } : []));

    (fixture.componentInstance as unknown as { startTransition(o: TransitionOption): void }).startTransition(close);
    const put = http.expectOne(`${API}/api/claims/c-1/status`);
    expect(put.request.body).toEqual({
      targetStatus: 'Closed',
      reason: null,
      closureJustification: 'Paid by the garage',
      acknowledgeWarnings: false,
    });
    put.flush({ claimId: 'c-1', status: 'Closed', validationIssues: [] });
    http.expectOne(`${API}/api/claims/c-1`).flush(claim({ status: 'Closed' }));
    http.expectOne(`${API}/api/claims/c-1/transitions`).flush([]);
    await fixture.whenStable();
    http.match(() => true).forEach((r) => r.flush(r.request.url.endsWith('/reserves') ? { claimId: 'c-1', totals: claim().reserves, history: [] } : []));

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="claim-status"] .badge')?.textContent).toBe('Closed');
  });
});
