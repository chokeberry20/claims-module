import { ApprovalLevel } from '../core/api/models';

/**
 * Reserve authority thresholds (S6.3, BR-R-02..04; the domain's ReserveAuthorityLimits.Default):
 * up to 10,000 is auto-approved, up to 100,000 needs a supervisor, above that a manager.
 * The size of a change counts, whichever its sign (D-16: authority uses |delta|).
 * The server decides; this mirrors it so the UI can say so while the user types.
 */
export const AUTO_APPROVAL_LIMIT = 10_000;
export const SUPERVISOR_APPROVAL_LIMIT = 100_000;

export function approvalLevelFor(delta: number): ApprovalLevel {
  const size = Math.abs(delta);
  if (size <= AUTO_APPROVAL_LIMIT) {
    return 'Auto';
  }
  return size <= SUPERVISOR_APPROVAL_LIMIT ? 'Supervisor' : 'Manager';
}
