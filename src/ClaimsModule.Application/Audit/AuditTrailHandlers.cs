using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Common.Events;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Documents;
using ClaimsModule.Domain.Reserves.Events;
using MediatR;

namespace ClaimsModule.Application.Audit;

/// <summary>
/// Domain events → ClaimAuditLog (S14.1, BR-A-02), written <b>before commit</b> so each entry commits
/// atomically with the change it describes (S14.2). Every S14.1 event type is covered, plus the
/// additions in DECISIONS (D-07, D-28, ai-log). Two events are deliberately not audited on their
/// own: ClaimWithdrawn (already STATUS_CHANGED with the reason) and ReservePostingRequeued
/// (internal retry plumbing; the eventual GL_POSTING_SIMULATED is the business record).
/// The completeness test fails if a new domain event appears without a decision here.
/// </summary>
internal sealed class AuditTrailHandlers(IAuditLogService audit) :
    INotificationHandler<BeforeCommit<ClaimCreated>>,
    INotificationHandler<BeforeCommit<ClaimStatusChanged>>,
    INotificationHandler<BeforeCommit<ClaimClosed>>,
    INotificationHandler<BeforeCommit<ClaimReopened>>,
    INotificationHandler<BeforeCommit<ClaimPartyAdded>>,
    INotificationHandler<BeforeCommit<ClaimPartyRemoved>>,
    INotificationHandler<BeforeCommit<ClaimSlaBreachDetected>>,
    INotificationHandler<BeforeCommit<ValidationIssueRaised>>,
    INotificationHandler<BeforeCommit<ValidationIssueWaived>>,
    INotificationHandler<BeforeCommit<ValidationWarningsAcknowledged>>,
    INotificationHandler<BeforeCommit<ClaimLossDetailsUpdated>>,
    INotificationHandler<BeforeCommit<ClaimNotesUpdated>>,
    INotificationHandler<BeforeCommit<ClaimSeverityChanged>>,
    INotificationHandler<BeforeCommit<ClaimRiskObjectAdded>>,
    INotificationHandler<BeforeCommit<ClaimRiskObjectRemoved>>,
    INotificationHandler<BeforeCommit<ClaimPolicyLinked>>,
    INotificationHandler<BeforeCommit<ClaimHandlerAssigned>>,
    INotificationHandler<BeforeCommit<ReserveLimitOverrideChanged>>,
    INotificationHandler<BeforeCommit<ReserveTransactionSubmitted>>,
    INotificationHandler<BeforeCommit<ReserveTransactionApproved>>,
    INotificationHandler<BeforeCommit<ReserveTransactionRejected>>,
    INotificationHandler<BeforeCommit<ReserveTransactionRetracted>>,
    INotificationHandler<BeforeCommit<ReservePostingCompleted>>,
    INotificationHandler<BeforeCommit<ReservePostingFailed>>,
    INotificationHandler<BeforeCommit<ReservePostingRetryRequested>>,
    INotificationHandler<BeforeCommit<ClaimDocumentUploaded>>
{
    private const string Transaction = "ReserveTransaction";

    // ------------------------------------------------------------------ claim lifecycle

    public Task Handle(BeforeCommit<ClaimCreated> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ClaimCreated, $"Claim {n.Event.ClaimNumber} created.",
        NewValue: new { n.Event.ClaimNumber, n.Event.PolicyId }, ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimStatusChanged> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.StatusChanged, $"Status changed from {n.Event.FromStatus} to {n.Event.ToStatus}.",
        OldValue: new { Status = n.Event.FromStatus }, NewValue: new { Status = n.Event.ToStatus, n.Event.Reason },
        ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimClosed> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ClaimClosed, "Claim closed.",
        NewValue: new { n.Event.ClosureReason, n.Event.ClosedWithOpenReserves, n.Event.OpenReservesJustification },
        ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimReopened> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ClaimReopened, "Claim reopened.", NewValue: new { n.Event.Reason }, ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimSlaBreachDetected> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.SlaBreachDetected, "No activity for 48 hours: SLA breach detected.",
        NewValue: new { n.Event.LastActivityAt, InactiveHours = Math.Floor((n.Event.OccurredAt - n.Event.LastActivityAt).TotalHours) }));

    // ------------------------------------------------------------------ parties

    public Task Handle(BeforeCommit<ClaimPartyAdded> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.PartyAdded, $"{n.Event.Role} {n.Event.DisplayName} added.",
        NewValue: new { n.Event.PartyId, n.Event.Role, Name = n.Event.DisplayName },
        RelatedEntityId: n.Event.PartyId, RelatedEntityType: "ClaimParty", ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimPartyRemoved> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.PartyRemoved, $"{n.Event.Role} {n.Event.DisplayName} removed.",
        OldValue: new { n.Event.PartyId, n.Event.Role, Name = n.Event.DisplayName, IsActive = true }, NewValue: new { IsActive = false },
        RelatedEntityId: n.Event.PartyId, RelatedEntityType: "ClaimParty", ActorUserId: n.Event.ActorUserId));

    // ------------------------------------------------------------------ validation (S5.4, D-07)

    public Task Handle(BeforeCommit<ValidationIssueRaised> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ValidationIssueAdded, $"{n.Event.Severity} validation issue: {n.Event.Message}",
        NewValue: new { n.Event.Code, n.Event.Severity, n.Event.Message },
        RelatedEntityId: n.Event.IssueId, RelatedEntityType: "ClaimValidationIssue"));

    public Task Handle(BeforeCommit<ValidationIssueWaived> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ValidationIssueWaived, $"Critical issue {n.Event.Code} waived.",
        OldValue: new { Status = "Open" }, NewValue: new { Status = "Waived", n.Event.Code, n.Event.Reason },
        RelatedEntityId: n.Event.IssueId, RelatedEntityType: "ClaimValidationIssue", ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ValidationWarningsAcknowledged> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.WarningsAcknowledged, $"{n.Event.IssueIds.Count} validation warning(s) acknowledged.",
        NewValue: new { n.Event.IssueIds }, ActorUserId: n.Event.ActorUserId));

    // ------------------------------------------------------------------ claim maintenance

    public Task Handle(BeforeCommit<ClaimLossDetailsUpdated> n, CancellationToken ct) => Updated(n.Event.ClaimId, "Loss details", n.Event.ActorUserId);

    public Task Handle(BeforeCommit<ClaimNotesUpdated> n, CancellationToken ct) => Updated(n.Event.ClaimId, "Notes", n.Event.ActorUserId);

    public Task Handle(BeforeCommit<ClaimSeverityChanged> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ClaimUpdated, $"Severity changed from {n.Event.PreviousSeverity} to {n.Event.Severity}.",
        OldValue: new { Severity = n.Event.PreviousSeverity }, NewValue: new { n.Event.Severity }, ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimRiskObjectAdded> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ClaimUpdated, $"Risk object added: {n.Event.AssetType} {n.Event.AssetDescription}.",
        NewValue: new { n.Event.RiskObjectId, n.Event.AssetType, n.Event.AssetDescription },
        RelatedEntityId: n.Event.RiskObjectId, RelatedEntityType: "ClaimRiskObject", ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimRiskObjectRemoved> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ClaimUpdated, "Risk object removed.",
        OldValue: new { n.Event.RiskObjectId }, RelatedEntityId: n.Event.RiskObjectId, RelatedEntityType: "ClaimRiskObject",
        ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimPolicyLinked> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.PolicyLinked, $"Policy {n.Event.PolicyNumber} linked.",
        OldValue: new { PolicyId = n.Event.PreviousPolicyId }, NewValue: new { n.Event.PolicyId, n.Event.PolicyNumber },
        ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ClaimHandlerAssigned> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.HandlerAssigned, $"Claim assigned to {n.Event.HandlerName}.",
        OldValue: new { HandlerId = n.Event.PreviousHandlerId }, NewValue: new { n.Event.HandlerId, n.Event.HandlerName },
        ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ReserveLimitOverrideChanged> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ReserveLimitOverrideSet,
        n.Event.Enabled ? "Manager override for the $10,000,000 reserve limit set." : "Manager override for the $10,000,000 reserve limit cleared.",
        NewValue: new { n.Event.Enabled }, ActorUserId: n.Event.ActorUserId));

    // ------------------------------------------------------------------ reserves (S6, S14.1)

    public Task Handle(BeforeCommit<ReserveTransactionSubmitted> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ReserveCreated,
        $"{n.Event.ComponentType} reserve {n.Event.TransactionType}: {n.Event.Amount:+#,0.00;-#,0.00} ({n.Event.ApprovalStatus}).",
        NewValue: new
        {
            n.Event.ComponentType, n.Event.TransactionType, n.Event.Amount, n.Event.NewBalance,
            n.Event.ApprovalStatus, n.Event.RequiredLevel, n.Event.RequiresLimitOverride,
        },
        RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction, ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ReserveTransactionApproved> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId,
        n.Event.IsAutoApproved ? AuditEventTypes.ReserveAutoApproved : AuditEventTypes.ReserveApproved,
        n.Event.IsAutoApproved
            ? $"{n.Event.ComponentType} reserve change auto-approved (within $10,000)."
            : $"{n.Event.ComponentType} reserve change approved.",
        OldValue: new { ApprovalStatus = n.Event.IsAutoApproved ? "Submitted" : "PendingApproval" },
        NewValue: new { n.Event.Amount, n.Event.NewBalance, n.Event.ChangeSequence, n.Event.IdempotencyKey, ApprovedBy = n.Event.ApproverUserId },
        RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction, ActorUserId: n.Event.ApproverUserId));

    public Task Handle(BeforeCommit<ReserveTransactionRejected> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ReserveRejected, $"Reserve change of {n.Event.Amount:#,0.00} rejected: {n.Event.Reason}",
        OldValue: new { ApprovalStatus = "PendingApproval", RejectionReason = n.Event.Reason },
        NewValue: new { ApprovalStatus = "Rejected", n.Event.Amount },
        RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction, ActorUserId: n.Event.ActorUserId));

    public Task Handle(BeforeCommit<ReserveTransactionRetracted> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.ReserveRetracted, "Pending reserve change retracted by the submitter.",
        OldValue: new { ApprovalStatus = "PendingApproval" }, NewValue: new { ApprovalStatus = "Cancelled", n.Event.Amount },
        RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction, ActorUserId: n.Event.ActorUserId));

    // ------------------------------------------------------------------ GL posting (S12.1)

    /// <summary>GL_POSTING_SIMULATED with the simulated double-entry journal (S12.1).</summary>
    public Task Handle(BeforeCommit<ReservePostingCompleted> n, CancellationToken ct)
    {
        var amount = Math.Abs(n.Event.Amount);
        var increase = n.Event.Amount >= 0;
        return Record(new(
            n.Event.ClaimId, AuditEventTypes.GlPostingSimulated, $"Reserve change of {n.Event.Amount:+#,0.00;-#,0.00} posted to the general ledger (simulated).",
            OldValue: new { PostingStatus = "Pending" },
            NewValue: new
            {
                PostingStatus = "Posted",
                n.Event.IdempotencyKey,
                n.Event.JobId,
                Journal = new[]
                {
                    new { Account = increase ? GlAccounts.ReserveExpense : GlAccounts.OutstandingReserves, Debit = amount, Credit = 0m },
                    new { Account = increase ? GlAccounts.OutstandingReserves : GlAccounts.ReserveExpense, Debit = 0m, Credit = amount },
                },
            },
            RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction));
    }

    public Task Handle(BeforeCommit<ReservePostingFailed> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.GlPostingFailed, $"GL posting failed after all retries: {n.Event.Reason}",
        OldValue: new { PostingStatus = "Pending" }, NewValue: new { PostingStatus = "Failed", n.Event.IdempotencyKey, n.Event.Reason },
        RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction));

    public Task Handle(BeforeCommit<ReservePostingRetryRequested> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.GlPostingRetryRequested, "Failed GL posting re-queued.",
        OldValue: new { PostingStatus = "Failed" }, NewValue: new { PostingStatus = "Pending", n.Event.IdempotencyKey },
        RelatedEntityId: n.Event.TransactionId, RelatedEntityType: Transaction, ActorUserId: n.Event.ActorUserId));

    // ------------------------------------------------------------------ documents (S13)

    public Task Handle(BeforeCommit<ClaimDocumentUploaded> n, CancellationToken ct) => Record(new(
        n.Event.ClaimId, AuditEventTypes.DocumentUploaded, $"Document {n.Event.DocumentName} uploaded.",
        NewValue: new { n.Event.DocumentId, n.Event.DocumentType, n.Event.DocumentName, n.Event.FileSizeBytes },
        RelatedEntityId: n.Event.DocumentId, RelatedEntityType: "ClaimDocument", ActorUserId: n.Event.ActorUserId));

    private Task Updated(Guid claimId, string what, Guid actorUserId) => Record(new(
        claimId, AuditEventTypes.ClaimUpdated, $"{what} updated.", NewValue: new { Changed = what }, ActorUserId: actorUserId));

    private Task Record(AuditEntry entry)
    {
        audit.Record(entry);
        return Task.CompletedTask;
    }
}

/// <summary>Simulated general-ledger accounts for the GL_POSTING_SIMULATED journal.</summary>
public static class GlAccounts
{
    public const string ReserveExpense = "5100 Loss reserve expense";
    public const string OutstandingReserves = "2100 Outstanding loss reserves";
}
