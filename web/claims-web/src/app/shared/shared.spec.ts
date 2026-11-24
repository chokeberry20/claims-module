import { TestBed } from '@angular/core/testing';
import { CLAIM_STATUSES } from '../core/api/models';
import { approvalLevelFor } from './authority';
import { AuthorityIndicator } from './authority-indicator';
import { ConfirmService } from './confirm-dialog';
import { formatMoney } from './money.pipe';
import { Badge, CLAIM_STATUS_TONE, labelOf } from './status-badge';

describe('approvalLevelFor (S6.3, BR-R-02..04; |delta| per D-16)', () => {
  it.each([
    [0.01, 'Auto'],
    [10_000, 'Auto'],
    [10_000.01, 'Supervisor'],
    [100_000, 'Supervisor'],
    [100_000.01, 'Manager'],
    [2_500_000, 'Manager'],
    [-10_000, 'Auto'],
    [-15_000, 'Supervisor'],
    [-100_000.01, 'Manager'],
  ] as const)('%d needs %s', (delta, level) => {
    expect(approvalLevelFor(delta)).toBe(level);
  });
});

describe('AuthorityIndicator', () => {
  async function render(delta: number | null): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(AuthorityIndicator);
    fixture.componentRef.setInput('delta', delta);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it.each([
    [10_000, 'Auto', 'Auto-approved (≤ $10,000)'],
    [10_000.01, 'Supervisor', 'Supervisor approval required'],
    [100_000, 'Supervisor', 'Supervisor approval required'],
    [100_000.01, 'Manager', 'Manager approval required'],
  ] as const)('shows %d as %s', async (delta, level, text) => {
    const el = await render(delta);
    expect(el.querySelector('.indicator')?.getAttribute('data-level')).toBe(level);
    expect(el.textContent).toContain(text);
  });

  it('shows nothing without an amount', async () => {
    expect((await render(null)).querySelector('.indicator')).toBeNull();
    expect((await render(0)).querySelector('.indicator')).toBeNull();
  });
});

describe('Badge', () => {
  it('colours all seven claim statuses as S11.1 asks, each differently', async () => {
    const tones = new Set<string>();
    for (const status of CLAIM_STATUSES) {
      const fixture = TestBed.createComponent(Badge);
      fixture.componentRef.setInput('status', status);
      await fixture.whenStable();
      const badge = (fixture.nativeElement as HTMLElement).querySelector('.badge')!;
      expect(badge.getAttribute('data-tone')).toBe(CLAIM_STATUS_TONE[status]);
      tones.add(badge.getAttribute('data-tone')!);
    }
    expect(tones.size).toBe(7);
    expect(CLAIM_STATUS_TONE).toMatchObject({ Draft: 'grey', Open: 'blue', Closed: 'green', Withdrawn: 'dark' });
  });

  it('renders a label with a tone', async () => {
    const fixture = TestBed.createComponent(Badge);
    fixture.componentRef.setInput('label', 'PendingApproval');
    fixture.componentRef.setInput('tone', 'amber');
    await fixture.whenStable();
    const badge = (fixture.nativeElement as HTMLElement).querySelector('.badge')!;
    expect(badge.textContent).toBe('Pending approval');
    expect(badge.getAttribute('data-tone')).toBe('amber');
  });

  it('labels enum names for people', () => {
    expect(labelOf('UnderInvestigation')).toBe('Under investigation');
    expect(labelOf('SubrogationRecoverable')).toBe('Subrogation recoverable');
    expect(labelOf('ALAE')).toBe('ALAE');
    expect(labelOf('Open')).toBe('Open');
  });
});

describe('formatMoney', () => {
  it('formats USD with cents, signs changes on request, and dashes missing values', () => {
    expect(formatMoney(50_000)).toBe('$50,000.00');
    expect(formatMoney(1234.5, true)).toBe('+$1,234.50');
    expect(formatMoney(-2500, true)).toBe('-$2,500.00');
    expect(formatMoney(null)).toBe('—');
  });
});

describe('ConfirmService', () => {
  function button(testId: string): HTMLButtonElement {
    return document.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!;
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((e) => (e.innerHTML = '')));

  it('needs the required reason before it confirms, and returns it trimmed', async () => {
    const results: unknown[] = [];
    TestBed.inject(ConfirmService)
      .confirm({ title: 'Withdraw claim', input: { label: 'Reason', required: true, maxLength: 500 } })
      .subscribe((r) => results.push(r));
    await new Promise((r) => setTimeout(r));

    expect(button('confirm-ok').disabled).toBe(true);
    const text = document.querySelector<HTMLTextAreaElement>('[data-testid="confirm-text"]')!;
    text.value = '  Duplicate of another claim  ';
    text.dispatchEvent(new Event('input'));
    TestBed.tick();
    expect(button('confirm-ok').disabled).toBe(false);

    button('confirm-ok').click();
    await new Promise((r) => setTimeout(r, 0));
    TestBed.tick();
    await new Promise((r) => setTimeout(r, 200));
    expect(results).toEqual([{ text: 'Duplicate of another claim' }]);
  });

  it('emits undefined when cancelled', async () => {
    const results: unknown[] = [];
    TestBed.inject(ConfirmService)
      .confirm({ title: 'Approve?', items: ['Amount: $50,000.00'] })
      .subscribe((r) => results.push(r));
    await new Promise((r) => setTimeout(r));
    expect(document.body.textContent).toContain('Amount: $50,000.00');

    button('confirm-cancel').click();
    TestBed.tick();
    await new Promise((r) => setTimeout(r, 200));
    expect(results).toEqual([undefined]);
  });
});
