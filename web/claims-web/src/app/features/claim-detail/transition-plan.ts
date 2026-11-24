import { TransitionOption, TransitionStatusRequest } from '../../core/api/models';
import { ChecklistItem, ConfirmDialogData } from '../../shared/confirm-dialog';
import { labelOf } from '../../shared/status-badge';

/**
 * How the UI asks for a status change (S11.3 transition menu, D-27 pre-flight). The server lists what is
 * still needed per option; each confirmation maps to one thing the user supplies in the dialog:
 * - `AcknowledgeWarnings` → the warnings are listed and confirming acknowledges them (BR-C-02, D-07);
 * - `Reason` → a required reason (Withdrawn, Reopened; S4.2, D-36);
 * - `ClosureJustification` → a required justification for open reserve balances (CC-04, D-21).
 */
export interface TransitionPlan {
  readonly dialog: ConfirmDialogData;
  readonly toRequest: (text: string | null) => TransitionStatusRequest;
}

const MAX_REASON = 500;

export function planTransition(option: TransitionOption): TransitionPlan {
  const fields = new Set(option.confirmationsRequired.map((c) => c.field));
  const target = option.targetStatus;
  const acknowledge = fields.has('AcknowledgeWarnings');
  const justification = fields.has('ClosureJustification');
  const reason = option.requiresReason || fields.has('Reason');
  const closing = target === 'Closed';
  // For Closed, the checklist shows the CC-04 confirmation; elsewhere the confirmations are listed as items.
  const items = closing ? [] : option.confirmationsRequired.map((c) => c.message);

  const dialog: ConfirmDialogData = {
    title: closing ? 'Close checklist' : `Move the claim to ${labelOf(target)}?`,
    message: closing
      ? option.isAllowed
        ? 'All closure conditions are met.'
        : 'The claim can’t be closed until the failing items are resolved.'
      : target === 'Reopened'
        ? 'The claim is reopened and goes straight back to Open (BR-ST-04).'
        : undefined,
    checklist: closing ? closeChecklist(option) : undefined,
    confirmDisabled: !option.isAllowed,
    items: items.length ? items : undefined,
    confirmLabel: acknowledge ? `Acknowledge and move to ${labelOf(target)}` : `Move to ${labelOf(target)}`,
    destructive: target === 'Withdrawn',
    input: justification
      ? { label: 'Closure justification', required: true, maxLength: MAX_REASON }
      : reason
        ? { label: 'Reason', required: true, maxLength: MAX_REASON }
        : undefined,
  };

  return {
    dialog,
    toRequest: (text) => ({
      targetStatus: target,
      reason: reason && !justification ? text : null,
      closureJustification: justification ? text : null,
      acknowledgeWarnings: acknowledge,
    }),
  };
}

/** The S4.3 closure conditions, in order, with the server's state for each (from GET /transitions, D-27). */
const CLOSE_CONDITIONS = [
  { code: 'CC-01', label: 'No reserve changes awaiting approval' },
  { code: 'CC-02', label: 'No open Critical validation issues' },
  { code: 'CC-03', label: 'At least one active Claimant' },
  { code: 'CC-04', label: 'Open reserve balances confirmed with a justification' },
] as const;

/**
 * The close pre-flight checklist (S11.3): every condition, passed or not. The server reports only what is
 * unmet (blocking: CC-01..03; to confirm: CC-04), so an absent code means the condition is satisfied.
 */
export function closeChecklist(option: TransitionOption): ChecklistItem[] {
  return CLOSE_CONDITIONS.map(({ code, label }) => {
    const blocking = option.blockingConditions.find((b) => b.code === code);
    const confirm = option.confirmationsRequired.find((c) => c.code === code);
    if (blocking) return { label: `${code}: ${label}`, state: 'blocked' as const, detail: blocking.message };
    if (confirm) return { label: `${code}: ${label}`, state: 'confirm' as const, detail: 'Enter the justification below.' };
    return { label: `${code}: ${label}`, state: 'ok' as const };
  });
}

/**
 * Whether the menu item opens a dialog. Allowed transitions do; Closed also opens when it is blocked only by
 * its conditions, to show the checklist (S11.3). Missing role or other blocked transitions stay disabled.
 */
export function canOpen(option: TransitionOption): boolean {
  return option.isAllowed || (option.targetStatus === 'Closed' && option.hasRequiredRole);
}

/** Why an option can't be chosen now, for the menu tooltip (role first, then the blocking conditions). */
export function blockedReason(option: TransitionOption): string | null {
  if (!option.hasRequiredRole) {
    return `Needs the ${option.requiredRole} role or above.`;
  }
  if (option.blockingConditions.length > 0) {
    return option.blockingConditions.map((b) => b.message).join(' ');
  }
  return null;
}
