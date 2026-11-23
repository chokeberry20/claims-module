using ClaimsModule.Application.Audit;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Documents;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

// ------------------------------------------------------------------ DTOs

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>A claims dashboard row (S11.1, D-26). <see cref="TotalReserves"/> is the approved gross total (D-19).</summary>
public sealed record ClaimSummaryDto
{
    public Guid ClaimId { get; init; }
    public string ClaimNumber { get; init; } = string.Empty;
    public ClaimStatus Status { get; init; }
    public ClaimSeverity Severity { get; init; }
    public DateTimeOffset LossDate { get; init; }
    public DateTimeOffset ReportedDate { get; init; }
    public string CauseOfLossCode { get; init; } = string.Empty;
    public string? CauseOfLossName { get; init; }
    public string? PolicyNumber { get; init; }
    public string? ClientName { get; init; }
    public string? PrimaryClaimantName { get; init; }
    public Guid? AssignedHandlerId { get; init; }
    public string? AssignedHandlerName { get; init; }
    public bool IsSlaBreached { get; init; }
    public decimal TotalReserves { get; init; }
}

public sealed record LossEventDto
{
    public DateTimeOffset LossDate { get; init; }
    public string LossDescription { get; init; } = string.Empty;
    public string? LossLocation { get; init; }
    public string CauseOfLossCode { get; init; } = string.Empty;
    public string? CauseOfLossName { get; init; }
    public decimal? EstimatedLossAmount { get; init; }
    public DateTimeOffset ReportDate { get; init; }
    public string? PoliceReportNumber { get; init; }
}

public sealed record PartyDto
{
    public Guid PartyId { get; init; }
    public PartyRole Role { get; init; }
    public PartyType Type { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? CompanyName { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? RemovedAt { get; init; }
}

public sealed record RiskObjectDto
{
    public Guid RiskObjectId { get; init; }
    public AssetType AssetType { get; init; }
    public string AssetDescription { get; init; } = string.Empty;
    public string? DamageDescription { get; init; }
    public string? AssetReference { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed record ReserveComponentSummaryDto
{
    public Guid ReserveComponentId { get; init; }
    public ReserveComponentType ComponentType { get; init; }
    public decimal CurrentAmount { get; init; }
    public ReserveComponentStatus Status { get; init; }
    public Guid? PendingTransactionId { get; init; }
    public decimal? PendingAmount { get; init; }
}

/// <summary>Reserve totals on the claim header (S11.3). Gross excludes SubrogationRecoverable (D-19).</summary>
public sealed record ReserveTotalsDto
{
    public decimal ApprovedGross { get; init; }
    public decimal Recoveries { get; init; }
    public bool HasPendingApproval { get; init; }
    public IReadOnlyList<ReserveComponentSummaryDto> Components { get; init; } = [];
}

/// <summary>GET /api/claims/{id} (S10.1, S11.3).</summary>
public sealed record ClaimDetailDto
{
    public Guid ClaimId { get; init; }
    public string ClaimNumber { get; init; } = string.Empty;
    public ClaimStatus Status { get; init; }
    public ClaimSeverity Severity { get; init; }
    public string ClaimType { get; init; } = string.Empty;
    public DateTimeOffset ReportedDate { get; init; }
    public Guid? PolicyId { get; init; }
    public string? PolicyNumber { get; init; }
    public string? ClientName { get; init; }
    public Guid? AssignedHandlerId { get; init; }
    public string? AssignedHandlerName { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string? ClosureReason { get; init; }
    public DateTimeOffset? ReopenedAt { get; init; }
    public int ReopenCount { get; init; }
    public bool IsSlaBreached { get; init; }
    public DateTimeOffset? LastSlaBreachAt { get; init; }
    public bool ReserveLimitOverride { get; init; }
    public LossEventDto LossEvent { get; init; } = new();
    public IReadOnlyList<PartyDto> Parties { get; init; } = [];
    public IReadOnlyList<RiskObjectDto> RiskObjects { get; init; } = [];
    public IReadOnlyList<ValidationIssueDto> ValidationIssues { get; init; } = [];
    public ReserveTotalsDto Reserves { get; init; } = new();

    /// <summary>The claim's documents, each with a fresh read link (S10.1).</summary>
    public IReadOnlyList<DocumentDto> Documents { get; init; } = [];

    /// <summary>The newest audit entries (S10.1 "recent audit log entries"); the full log is paginated at /audit.</summary>
    public IReadOnlyList<AuditLogEntryDto> RecentAuditEntries { get; init; } = [];
}

// ------------------------------------------------------------------ read side

public sealed record ClaimListFilter(
    IReadOnlyList<ClaimStatus> Statuses,
    DateTimeOffset? LossDateFrom,
    DateTimeOffset? LossDateTo,
    Guid? AssignedHandlerId,
    string? AssignedHandler,
    string? CauseOfLossCode,
    Guid? PolicyId,
    string? Search,
    ClaimSortField SortBy,
    bool Descending,
    int Page,
    int PageSize);

public enum ClaimSortField
{
    ReportedDate = 0,
    LossDate = 1,
    ClaimNumber = 2,
    Status = 3,
}

/// <summary>Claim reads (no tracking, projections). Implemented in Persistence.</summary>
public interface IClaimReader
{
    Task<PagedResult<ClaimSummaryDto>> ListAsync(ClaimListFilter filter, CancellationToken cancellationToken);

    Task<ClaimDetailDto?> GetDetailAsync(Guid claimId, CancellationToken cancellationToken);
}

// ------------------------------------------------------------------ queries

/// <summary>GET /api/claims (S10.1, S11.1): filters, search, sorting and paging (FR-90, D-26).</summary>
public sealed record ListClaimsQuery : IQuery<PagedResult<ClaimSummaryDto>>
{
    public const int MaxPageSize = 100;

    public IReadOnlyList<ClaimStatus> Statuses { get; init; } = [];
    public DateTimeOffset? LossDateFrom { get; init; }
    public DateTimeOffset? LossDateTo { get; init; }
    public Guid? AssignedHandlerId { get; init; }
    public string? AssignedHandler { get; init; }
    public string? CauseOfLossCode { get; init; }
    public Guid? PolicyId { get; init; }
    public string? Search { get; init; }
    public ClaimSortField SortBy { get; init; } = ClaimSortField.ReportedDate;
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

internal sealed class ListClaimsQueryValidator : AbstractValidator<ListClaimsQuery>
{
    public ListClaimsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithErrorCode("PAGE_OUT_OF_RANGE").WithMessage("Page must be 1 or greater.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, ListClaimsQuery.MaxPageSize).WithErrorCode("PAGE_SIZE_OUT_OF_RANGE")
            .WithMessage($"Page size must be between 1 and {ListClaimsQuery.MaxPageSize}.");
        RuleForEach(q => q.Statuses).IsInEnum().WithErrorCode("STATUS_INVALID").WithMessage("Unknown claim status.");
        RuleFor(q => q.SortBy).IsInEnum().WithErrorCode("SORT_INVALID").WithMessage("Unknown sort field.");
        RuleFor(q => q.LossDateTo).GreaterThanOrEqualTo(q => q.LossDateFrom!.Value)
            .When(q => q.LossDateFrom is not null && q.LossDateTo is not null)
            .WithErrorCode("DATE_RANGE_INVALID").WithMessage("Loss date 'to' must not be before 'from'.");
        RuleFor(q => q.Search).MaximumLength(100).WithErrorCode("SEARCH_TERM_TOO_LONG").WithMessage("Search term must be at most 100 characters.");
        RuleFor(q => q.AssignedHandler).MaximumLength(100).WithErrorCode("SEARCH_TERM_TOO_LONG").WithMessage("Handler filter must be at most 100 characters.");
    }
}

internal sealed class ListClaimsHandler(IClaimReader reader) : IRequestHandler<ListClaimsQuery, PagedResult<ClaimSummaryDto>>
{
    public Task<PagedResult<ClaimSummaryDto>> Handle(ListClaimsQuery request, CancellationToken cancellationToken) =>
        reader.ListAsync(
            new ClaimListFilter(
                request.Statuses.Distinct().ToList(),
                request.LossDateFrom,
                request.LossDateTo,
                request.AssignedHandlerId,
                Clean(request.AssignedHandler),
                Clean(request.CauseOfLossCode),
                request.PolicyId,
                Clean(request.Search),
                request.SortBy,
                request.Descending,
                request.Page,
                request.PageSize),
            cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>GET /api/claims/{id}.</summary>
public sealed record GetClaimQuery(Guid ClaimId) : IQuery<ClaimDetailDto>;

internal sealed class GetClaimHandler(IClaimReader reader, IClaimDocumentReader documents, DocumentLinks links, IAuditLogReader auditLog)
    : IRequestHandler<GetClaimQuery, ClaimDetailDto>
{
    public const int RecentAuditEntryCount = 10;

    public async Task<ClaimDetailDto> Handle(GetClaimQuery request, CancellationToken cancellationToken)
    {
        var detail = await reader.GetDetailAsync(request.ClaimId, cancellationToken) ?? throw new NotFoundException("Claim", request.ClaimId);
        var documentRecords = await documents.ListAsync(request.ClaimId, cancellationToken) ?? [];
        var recent = await auditLog.ListAsync(request.ClaimId, eventType: null, page: 1, RecentAuditEntryCount, cancellationToken);
        return detail with
        {
            Documents = documentRecords.Select(links.ToDto).ToList(),
            RecentAuditEntries = recent?.Items ?? [],
        };
    }
}
