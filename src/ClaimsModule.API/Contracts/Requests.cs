using System.ComponentModel.DataAnnotations;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Domain.Reserves;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Contracts;

// Request bodies and query strings of the HTTP API. Route ids and the Idempotency-Key header are not
// part of them: the controllers combine both into the Application command, so the Swagger schemas show
// only what a client actually sends. Business validation stays in the Application validators (422).

/// <summary>Query string of GET /api/claims (S10.1, S11.1, D-26).</summary>
public sealed class ClaimListRequest
{
    /// <summary>One or more statuses: <c>?status=Open&amp;status=Draft</c>.</summary>
    [FromQuery(Name = "status")]
    public ClaimStatus[]? Status { get; init; }

    /// <summary>Loss date from (UTC day, inclusive).</summary>
    public DateOnly? DateFrom { get; init; }

    /// <summary>Loss date to (UTC day, inclusive).</summary>
    public DateOnly? DateTo { get; init; }

    public Guid? AssignedHandlerId { get; init; }

    /// <summary>Part of the assigned handler's name (S11.1 text filter).</summary>
    public string? AssignedHandler { get; init; }

    public string? CauseOfLossCode { get; init; }

    public Guid? PolicyId { get; init; }

    /// <summary>Partial claim number, policy number or client name.</summary>
    public string? Search { get; init; }

    public ClaimSortField SortBy { get; init; } = ClaimSortField.ReportedDate;

    public bool Descending { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public ListClaimsQuery ToQuery() => new()
    {
        Statuses = Status ?? [],
        LossDateFrom = DateFrom is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
        LossDateTo = DateTo is { } to ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddTicks(-1) : null,
        AssignedHandlerId = AssignedHandlerId,
        AssignedHandler = AssignedHandler,
        CauseOfLossCode = CauseOfLossCode,
        PolicyId = PolicyId,
        Search = Search,
        SortBy = SortBy,
        Descending = Descending,
        Page = Page,
        PageSize = PageSize,
    };
}

/// <summary>Body of POST /api/claims (FNOL, S5.2, S10.1).</summary>
public sealed record CreateClaimRequest
{
    /// <summary>The linked policy; omit for an unknown policy (BR-C-06 warning).</summary>
    public Guid? PolicyId { get; init; }

    public DateTimeOffset LossDate { get; init; }

    /// <summary>At least 20 characters (BR-C-07).</summary>
    public string LossDescription { get; init; } = string.Empty;

    public string CauseOfLossCode { get; init; } = string.Empty;

    public string? LossLocation { get; init; }

    public decimal? EstimatedLossAmount { get; init; }

    public string? PoliceReportNumber { get; init; }

    public ClaimSeverity Severity { get; init; } = ClaimSeverity.Standard;

    public string? Notes { get; init; }

    public IReadOnlyList<PartyInput> Parties { get; init; } = [];

    public IReadOnlyList<RiskObjectInput> RiskObjects { get; init; } = [];

    public InitialReserveInput? InitialReserve { get; init; }

    public CreateClaimCommand ToCommand(string? idempotencyKey) => new()
    {
        PolicyId = PolicyId,
        LossDate = LossDate,
        LossDescription = LossDescription,
        CauseOfLossCode = CauseOfLossCode,
        LossLocation = LossLocation,
        EstimatedLossAmount = EstimatedLossAmount,
        PoliceReportNumber = PoliceReportNumber,
        Severity = Severity,
        Notes = Notes,
        Parties = Parties,
        RiskObjects = RiskObjects,
        InitialReserve = InitialReserve,
        IdempotencyKey = idempotencyKey,
    };
}

/// <summary>Body of PUT /api/claims/{id}/status (S10.1). Reason: Withdrawn and Reopened. Justification: closing with open reserves (CC-04).</summary>
public sealed record TransitionStatusRequest(
    ClaimStatus TargetStatus,
    string? Reason = null,
    string? ClosureJustification = null,
    bool AcknowledgeWarnings = false);

/// <summary>Body of PUT /api/claims/{id}/loss-details (Draft only, D-31).</summary>
public sealed record UpdateLossDetailsRequest(
    DateTimeOffset LossDate,
    string LossDescription,
    string CauseOfLossCode,
    string? LossLocation = null,
    decimal? EstimatedLossAmount = null,
    string? PoliceReportNumber = null);

public sealed record LinkPolicyRequest(Guid PolicyId);

public sealed record AssignHandlerRequest(Guid HandlerId);

public sealed record UpdateNotesRequest(string? Notes);

public sealed record ChangeSeverityRequest(ClaimSeverity Severity);

public sealed record WaiveIssueRequest(string Reason);

public sealed record ReserveLimitOverrideRequest(bool Enabled);

/// <summary>Body of POST /api/claims/{id}/reserves (S10.2): Add opens a component, Adjust sets a new balance, Reverse brings it to zero (D-16).</summary>
public sealed record SubmitReserveRequest(
    ReserveComponentType Component,
    ReserveTransactionType TransactionType,
    decimal? Amount,
    string ChangeReason);

/// <summary>Body of PUT /api/claims/{id}/reserves/{componentId}: an Adjust to a new target balance (B3.3.3, D-04).</summary>
public sealed record AdjustReserveRequest(decimal Amount, string ChangeReason);

/// <summary>Body of POST …/reserves/{txnId}/reject (S10.2).</summary>
public sealed record RejectReserveRequest(string RejectionReason);

/// <summary>Multipart form of POST /api/claims/{id}/documents (S13).</summary>
public sealed class UploadDocumentForm
{
    /// <summary>PDF, JPEG, PNG, DOCX, XLSX, TXT or CSV; at most 50 MB.</summary>
    [Required]
    public IFormFile? File { get; init; }

    /// <summary>For example "Photo", "Police report", "Invoice".</summary>
    [Required]
    public string DocumentType { get; init; } = string.Empty;

    public string? Notes { get; init; }
}

/// <summary>Query string of GET /api/reference/cause-of-loss-codes (S10.3).</summary>
public sealed record CauseOfLossCodesRequest(PerilCategory? PerilCategory = null, bool IncludeInactive = false);

/// <summary>Body of POST /api/auth/token.</summary>
public sealed record TokenRequest(string UserName, string Password);
