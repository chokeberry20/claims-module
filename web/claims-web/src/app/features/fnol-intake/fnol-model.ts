import { ApiErrorBody, errorMessages } from '../../core/api/api-error';
import {
  CreateClaimRequest,
  InitialReserveInput,
  PartyInput,
  PolicySummary,
  RiskObjectInput,
} from '../../core/api/models';

/** Pure FNOL helpers, kept apart from the component so the rules are unit-tested directly. */

/** Matches the server's BR-C-01 tolerance (D-15): up to five minutes ahead of now is not "future". */
export const FUTURE_TOLERANCE_MS = 5 * 60_000;

/** `<input type="datetime-local">` value (local time, no zone) → ISO 8601 in UTC. */
export function localDateTimeToIso(value: string): string | null {
  if (!value) {
    return null;
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** A Date → the `datetime-local` value in local time ("2026-11-24T09:30"). */
export function toLocalDateTimeValue(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function isInFuture(iso: string | null, now: number): boolean {
  return iso !== null && Date.parse(iso) > now + FUTURE_TOLERANCE_MS;
}

/**
 * The S11.2 in-force badge: true when the loss date (its UTC day, D-15) falls inside the policy's effective
 * period, inclusive, and the policy isn't cancelled; null when there is nothing to compare yet.
 */
export function isLossInForce(policy: PolicySummary | null, lossDateIso: string | null): boolean | null {
  if (!policy || !lossDateIso) {
    return null;
  }
  if (policy.status === 'Cancelled') {
    return false;
  }
  const lossDay = lossDateIso.slice(0, 10);
  return lossDay >= policy.effectiveDate && lossDay <= policy.expirationDate;
}

export interface FnolDraft {
  readonly unknownPolicy: boolean;
  readonly policy: PolicySummary | null;
  readonly lossDateIso: string | null;
  readonly parties: readonly PartyInput[];
  readonly riskObjects: readonly RiskObjectInput[];
}

/** The S5.4 / D-05 warnings the user is asked to confirm before creating (they don't block). */
export function fnolWarnings(draft: FnolDraft): string[] {
  const warnings: string[] = [];
  if (draft.unknownPolicy || !draft.policy) {
    warnings.push('No policy is linked (BR-C-06): reserves stay blocked until a policy is linked.');
  } else if (isLossInForce(draft.policy, draft.lossDateIso) === false) {
    warnings.push('The loss date is outside the policy period or the policy is not in force (BR-C-02).');
  }
  if (draft.riskObjects.length === 0) {
    warnings.push('No risk objects were added.');
  }
  return warnings;
}

export function hasClaimant(parties: readonly PartyInput[]): boolean {
  return parties.some((p) => p.role === 'Claimant');
}

/** Which step a server error belongs to (0: policy & loss, 1: parties & risk objects, 2: reserve & review). */
export function stepForField(field: string): number {
  if (/^(Parties|RiskObjects)\b/i.test(field)) {
    return 1;
  }
  if (/^(InitialReserve|General|Idempotency-Key)\b/i.test(field)) {
    return 2;
  }
  return 0;
}

/** The server's messages grouped by step, for the banner at the top of each step (S11.2 "On error"). */
export function errorsByStep(body: ApiErrorBody): string[][] {
  const steps: string[][] = [[], [], []];
  for (const [field, messages] of Object.entries(body.errors ?? {})) {
    const step = stepForField(field);
    steps[step].push(...errorMessages({ ...body, errors: { [field]: messages } }));
  }
  if (steps.every((s) => s.length === 0)) {
    steps[2].push(body.detail ? `${body.title} ${body.detail}` : body.title);
  }
  return steps;
}

export interface FnolValues {
  readonly policy: PolicySummary | null;
  readonly unknownPolicy: boolean;
  readonly lossDateIso: string;
  readonly causeOfLossCode: string;
  readonly lossDescription: string;
  readonly lossLocation: string;
  readonly estimatedLossAmount: number | null;
  readonly parties: readonly PartyInput[];
  readonly riskObjects: readonly RiskObjectInput[];
  readonly initialReserve: InitialReserveInput | null;
}

/** The POST /api/claims body. Empty optional text becomes null; no policy → policyId null (unknown policy). */
export function toCreateRequest(v: FnolValues): CreateClaimRequest {
  const text = (s: string) => (s.trim() === '' ? null : s.trim());
  return {
    policyId: v.unknownPolicy ? null : (v.policy?.policyId ?? null),
    lossDate: v.lossDateIso,
    lossDescription: v.lossDescription.trim(),
    causeOfLossCode: v.causeOfLossCode,
    lossLocation: text(v.lossLocation),
    estimatedLossAmount: v.estimatedLossAmount,
    parties: v.parties,
    riskObjects: v.riskObjects,
    initialReserve: v.unknownPolicy ? null : v.initialReserve,
  };
}

export function partyName(p: PartyInput): string {
  return p.type === 'Company' ? (p.companyName ?? '') : `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim();
}
