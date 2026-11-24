import { TransitionOption, TransitionStatusRequest } from '../../core/api/models';
import { ConfirmDialogData } from '../../shared/confirm-dialog';
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
  const items = option.confirmationsRequired.map((c) => c.message);

  const dialog: ConfirmDialogData = {
    title: `Move the claim to ${labelOf(target)}?`,
    message:
      target === 'Closed'
        ? 'Close checklist: no pending reserve approvals, no open Critical issues and an active Claimant are satisfied.'
        : target === 'Reopened'
          ? 'The claim is reopened and goes straight back to Open (BR-ST-04).'
          : undefined,
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
