// Typed shapes of the Claims API (P5). They mirror src/ClaimsModule.API/Contracts/Requests.cs and the
// Application DTOs; enums travel as their names (JsonStringEnumConverter), so they are string unions here.
// Dates arrive as ISO 8601 strings and are kept as strings (formatted by the date pipe in templates).

export const CLAIM_STATUSES = [
  'Draft',
  'Open',
  'UnderInvestigation',
  'PendingPayment',
  'Closed',
  'Reopened',
  'Withdrawn',
] as const;
export type ClaimStatus = (typeof CLAIM_STATUSES)[number];

export type ClaimSeverity = 'Standard' | 'Minor' | 'Critical' | 'Catastrophic';
export const PARTY_ROLES = ['Claimant', 'Insured', 'ThirdParty', 'Witness', 'Attorney'] as const;
export type PartyRole = (typeof PARTY_ROLES)[number];
export type PartyType = 'Person' | 'Company';
export const ASSET_TYPES = ['Vehicle', 'Property', 'Person', 'Equipment', 'Other'] as const;
export type AssetType = (typeof ASSET_TYPES)[number];
export const RESERVE_COMPONENTS = ['Indemnity', 'Expense', 'ALAE', 'SubrogationRecoverable'] as const;
export type ReserveComponentType = (typeof RESERVE_COMPONENTS)[number];
export type ReserveComponentStatus = 'Active' | 'Closed';
export type ReserveTransactionType = 'Add' | 'Adjust' | 'Reverse';
export type ApprovalStatus = 'AutoApproved' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Cancelled';
export type ApprovalLevel = 'Auto' | 'Supervisor' | 'Manager';
export type PostingStatus = 'Pending' | 'Posted' | 'Failed' | 'Cancelled';
export type ValidationSeverity = 'Critical' | 'Warning';
export type ValidationIssueStatus = 'Open' | 'Acknowledged' | 'Waived' | 'Resolved';
export type UserRole = 'Handler' | 'Supervisor' | 'Manager';
export type PolicyStatus = 'Active' | 'Expired' | 'Cancelled';
export type PerilCategory = 'Property' | 'Auto' | 'Liability' | 'Weather' | 'Equipment' | 'Crime' | 'General';
export type ClaimSortField = 'ReportedDate' | 'LossDate' | 'ClaimNumber' | 'Status';

// ------------------------------------------------------------------ auth

export interface DemoUser {
  readonly userId: string;
  readonly userName: string;
  readonly displayName: string;
  readonly role: UserRole;
}

export interface TokenResponse {
  readonly accessToken: string;
  readonly tokenType: string;
  readonly expiresAt: string;
  readonly userId: string;
  readonly displayName: string;
  readonly role: UserRole;
}

// ------------------------------------------------------------------ common

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface ValidationIssue {
  readonly issueId: string;
  readonly code: string;
  readonly severity: ValidationSeverity;
  readonly message: string;
  readonly field: string | null;
  readonly status: ValidationIssueStatus;
}

export interface Warning {
  readonly code: string;
  readonly message: string;
  readonly field: string | null;
}

export interface ClaimChangeResult {
  readonly claimId: string;
  readonly status: ClaimStatus;
  readonly validationIssues: readonly ValidationIssue[];
}

// ------------------------------------------------------------------ reference data and policies

export interface CauseOfLossCode {
  readonly code: string;
  readonly name: string;
  readonly perilCategory: PerilCategory;
  readonly isActive: boolean;
  readonly sortOrder: number;
}

export interface PolicySummary {
  readonly policyId: string;
  readonly policyNumber: string;
  readonly clientName: string;
  /** ISO date (yyyy-MM-dd). */
  readonly effectiveDate: string;
  /** ISO date (yyyy-MM-dd). */
  readonly expirationDate: string;
  readonly status: PolicyStatus;
}

// ------------------------------------------------------------------ claims

export interface ClaimListQuery {
  readonly status?: readonly ClaimStatus[];
  readonly dateFrom?: string;
  readonly dateTo?: string;
  readonly assignedHandler?: string;
  readonly causeOfLossCode?: string;
  readonly search?: string;
  readonly sortBy?: ClaimSortField;
  readonly descending?: boolean;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface ClaimSummary {
  readonly claimId: string;
  readonly claimNumber: string;
  readonly status: ClaimStatus;
  readonly severity: ClaimSeverity;
  readonly lossDate: string;
  readonly reportedDate: string;
  readonly causeOfLossCode: string;
  readonly causeOfLossName: string | null;
  readonly policyNumber: string | null;
  readonly clientName: string | null;
  readonly primaryClaimantName: string | null;
  readonly assignedHandlerId: string | null;
  readonly assignedHandlerName: string | null;
  readonly isSlaBreached: boolean;
  readonly totalReserves: number;
}

export interface PartyInput {
  readonly role: PartyRole;
  readonly type: PartyType;
  readonly firstName?: string | null;
  readonly lastName?: string | null;
  readonly companyName?: string | null;
  readonly email?: string | null;
  readonly phone?: string | null;
  readonly notes?: string | null;
}

export interface RiskObjectInput {
  readonly assetType: AssetType;
  readonly assetDescription: string;
  readonly damageDescription?: string | null;
  readonly assetReference?: string | null;
  readonly isPrimary?: boolean;
}

/** The FNOL initial reserve. Note: `componentType` here, while the reserve endpoint's body uses `component`. */
export interface InitialReserveInput {
  readonly componentType: ReserveComponentType;
  readonly amount: number;
  readonly changeReason: string;
}

export interface CreateClaimRequest {
  readonly policyId: string | null;
  /** ISO 8601 with offset. */
  readonly lossDate: string;
  readonly lossDescription: string;
  readonly causeOfLossCode: string;
  readonly lossLocation?: string | null;
  readonly estimatedLossAmount?: number | null;
  readonly policeReportNumber?: string | null;
  readonly severity?: ClaimSeverity;
  readonly notes?: string | null;
  readonly parties: readonly PartyInput[];
  readonly riskObjects: readonly RiskObjectInput[];
  readonly initialReserve?: InitialReserveInput | null;
}

export interface ReserveSubmission {
  readonly reserveComponentId: string;
  readonly transactionId: string;
  readonly componentType: ReserveComponentType;
  readonly transactionType: ReserveTransactionType;
  readonly amount: number;
  readonly previousBalance: number;
  readonly newBalance: number;
  readonly approvalStatus: ApprovalStatus;
  readonly requiredApprovalLevel: ApprovalLevel;
  readonly requiresLimitOverride: boolean;
  readonly warnings: readonly Warning[];
}

export interface CreateClaimResult {
  readonly claimId: string;
  readonly claimNumber: string;
  readonly status: ClaimStatus;
  readonly validationIssues: readonly ValidationIssue[];
  readonly initialReserve: ReserveSubmission | null;
}

export interface LossEvent {
  readonly lossDate: string;
  readonly lossDescription: string;
  readonly lossLocation: string | null;
  readonly causeOfLossCode: string;
  readonly causeOfLossName: string | null;
  readonly estimatedLossAmount: number | null;
  readonly reportDate: string;
  readonly policeReportNumber: string | null;
}

export interface Party {
  readonly partyId: string;
  readonly role: PartyRole;
  readonly type: PartyType;
  readonly displayName: string;
  readonly email: string | null;
  readonly phone: string | null;
  readonly isActive: boolean;
}

export interface RiskObject {
  readonly riskObjectId: string;
  readonly assetType: AssetType;
  readonly assetDescription: string;
  readonly damageDescription: string | null;
  readonly assetReference: string | null;
  readonly isPrimary: boolean;
}

export interface ReserveComponentSummary {
  readonly reserveComponentId: string;
  readonly componentType: ReserveComponentType;
  readonly currentAmount: number;
  readonly status: ReserveComponentStatus;
  readonly pendingTransactionId: string | null;
  readonly pendingAmount: number | null;
}

export interface ReserveTotals {
  readonly approvedGross: number;
  readonly recoveries: number;
  readonly hasPendingApproval: boolean;
  readonly components: readonly ReserveComponentSummary[];
}

export interface ClaimDetail {
  readonly claimId: string;
  readonly claimNumber: string;
  readonly status: ClaimStatus;
  readonly severity: ClaimSeverity;
  readonly claimType: string;
  readonly reportedDate: string;
  readonly policyId: string | null;
  readonly policyNumber: string | null;
  readonly clientName: string | null;
  readonly assignedHandlerId: string | null;
  readonly assignedHandlerName: string | null;
  readonly notes: string | null;
  readonly closedAt: string | null;
  readonly reopenedAt: string | null;
  readonly reopenCount: number;
  readonly isSlaBreached: boolean;
  readonly reserveLimitOverride: boolean;
  readonly lossEvent: LossEvent;
  readonly parties: readonly Party[];
  readonly riskObjects: readonly RiskObject[];
  readonly validationIssues: readonly ValidationIssue[];
  readonly reserves: ReserveTotals;
  readonly documents: readonly ClaimDocument[];
}

// ------------------------------------------------------------------ status transitions

export interface TransitionOption {
  readonly targetStatus: ClaimStatus;
  readonly requiredRole: UserRole;
  readonly hasRequiredRole: boolean;
  readonly requiresReason: boolean;
  readonly isAllowed: boolean;
  readonly canProceed: boolean;
  readonly blockingConditions: readonly Warning[];
  readonly confirmationsRequired: readonly Warning[];
}

export interface TransitionStatusRequest {
  readonly targetStatus: ClaimStatus;
  readonly reason?: string | null;
  readonly closureJustification?: string | null;
  readonly acknowledgeWarnings?: boolean;
}

// ------------------------------------------------------------------ reserves

export interface ReserveTransaction {
  readonly transactionId: string;
  readonly reserveComponentId: string;
  readonly componentType: ReserveComponentType;
  readonly transactionType: ReserveTransactionType;
  readonly changeSequence: number;
  /** The signed change (D-16). */
  readonly amount: number;
  readonly previousBalance: number;
  readonly newBalance: number;
  readonly changeReason: string;
  readonly approvalStatus: ApprovalStatus;
  readonly requiredApprovalLevel: ApprovalLevel;
  readonly requiresLimitOverride: boolean;
  readonly submittedByUserId: string;
  readonly submittedByName: string;
  readonly submittedAt: string;
  readonly approvedByUserId: string | null;
  readonly approvedAt: string | null;
  readonly rejectedByUserId: string | null;
  readonly rejectedAt: string | null;
  readonly rejectionReason: string | null;
  readonly postingStatus: PostingStatus;
  readonly postingJobId: string | null;
  readonly postedAt: string | null;
  readonly postingFailureReason: string | null;
}

export interface ClaimReserves {
  readonly claimId: string;
  readonly totals: ReserveTotals;
  readonly history: readonly ReserveTransaction[];
}

/** Body of POST /api/claims/{id}/reserves: Add opens, Adjust sets a new balance, Reverse brings it to zero (D-16). */
export interface SubmitReserveRequest {
  readonly component: ReserveComponentType;
  readonly transactionType: ReserveTransactionType;
  readonly amount: number | null;
  readonly changeReason: string;
}

export interface ReserveTransactionState {
  readonly reserveComponentId: string;
  readonly transactionId: string;
  readonly approvalStatus: ApprovalStatus;
  readonly postingStatus: PostingStatus;
  readonly componentBalance: number;
}

// ------------------------------------------------------------------ documents and audit

export interface ClaimDocument {
  readonly documentId: string;
  readonly claimId: string;
  readonly documentType: string;
  readonly documentName: string;
  readonly contentType: string;
  readonly fileSizeBytes: number;
  readonly uploadedAt: string;
  readonly uploadedByUserId: string;
  readonly uploadedByName: string;
  readonly notes: string | null;
  /** A 1-hour SAS link in Azure, /uploads/... locally (may be relative to the API). */
  readonly downloadUrl: string;
  readonly downloadUrlExpiresAt: string;
}

export interface AuditLogEntry {
  readonly auditLogId: string;
  readonly eventType: string;
  readonly description: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
  readonly relatedEntityId: string | null;
  readonly relatedEntityType: string | null;
  readonly correlationId: string | null;
  readonly createdByUserId: string | null;
  readonly createdAt: string;
}
