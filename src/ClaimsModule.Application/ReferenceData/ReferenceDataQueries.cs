using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.ReferenceData;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.ReferenceData;

// ------------------------------------------------------------------ DTOs

/// <summary>A cause-of-loss code for the FNOL dropdown (S5.6, S10.3).</summary>
public sealed record CauseOfLossCodeDto
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public PerilCategory PerilCategory { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>One allowed status transition from the ClaimStatusTransitions table (B3.2, FR-27, D-08).</summary>
public sealed record ClaimStatusTransitionDto
{
    public ClaimStatus FromStatus { get; init; }
    public ClaimStatus ToStatus { get; init; }
    public UserRole RequiredRole { get; init; }
    public bool RequiresReason { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>A policy search result (S10.3 typeahead). <see cref="Status"/> is computed for today (D-34).</summary>
public sealed record PolicySummaryDto
{
    public Guid PolicyId { get; init; }
    public string PolicyNumber { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public DateOnly EffectiveDate { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public PolicyStatus Status { get; init; }
}

/// <summary>GET /api/policies/{id}/coverage (B3.3.2, D-35).</summary>
public sealed record PolicyCoverageDto
{
    public Guid PolicyId { get; init; }
    public string PolicyNumber { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public DateOnly EffectiveDate { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public PolicyStatus Status { get; init; }
    public IReadOnlyList<string> CoverageTypes { get; init; } = [];
}

// ------------------------------------------------------------------ read side

/// <summary>Read-only reference data access (no tracking, projections). Implemented in Persistence.</summary>
public interface IReferenceDataReader
{
    Task<IReadOnlyList<CauseOfLossCodeDto>> ListCauseOfLossCodesAsync(bool includeInactive, PerilCategory? perilCategory, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimStatusTransitionDto>> ListStatusTransitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicySummaryDto>> SearchPoliciesAsync(string? term, int limit, DateOnly today, CancellationToken cancellationToken);

    Task<PolicyCoverageDto?> GetPolicyCoverageAsync(Guid policyId, DateOnly today, CancellationToken cancellationToken);
}

// ------------------------------------------------------------------ queries

/// <summary>
/// GET /api/reference/cause-of-loss-codes, optionally for one peril category (S10.3, B3.3.4). Inactive
/// codes are hidden unless asked for (BR-C-05).
/// </summary>
public sealed record GetCauseOfLossCodesQuery(bool IncludeInactive = false, PerilCategory? PerilCategory = null) : IQuery<IReadOnlyList<CauseOfLossCodeDto>>;

internal sealed class GetCauseOfLossCodesHandler(IReferenceDataReader reader)
    : IRequestHandler<GetCauseOfLossCodesQuery, IReadOnlyList<CauseOfLossCodeDto>>
{
    public Task<IReadOnlyList<CauseOfLossCodeDto>> Handle(GetCauseOfLossCodesQuery request, CancellationToken cancellationToken) =>
        reader.ListCauseOfLossCodesAsync(request.IncludeInactive, request.PerilCategory, cancellationToken);
}

/// <summary>GET /api/reference/claim-statuses: the allowed transitions, read from the database table (FR-27).</summary>
public sealed record GetClaimStatusTransitionsQuery : IQuery<IReadOnlyList<ClaimStatusTransitionDto>>;

internal sealed class GetClaimStatusTransitionsHandler(IReferenceDataReader reader)
    : IRequestHandler<GetClaimStatusTransitionsQuery, IReadOnlyList<ClaimStatusTransitionDto>>
{
    public Task<IReadOnlyList<ClaimStatusTransitionDto>> Handle(GetClaimStatusTransitionsQuery request, CancellationToken cancellationToken) =>
        reader.ListStatusTransitionsAsync(cancellationToken);
}

/// <summary>One claim status and the statuses it can move to (S10.3 "status values with their valid next-status transitions").</summary>
public sealed record ClaimStatusDto(ClaimStatus Status, IReadOnlyList<ClaimStatusTransitionDto> NextStatuses);

/// <summary>
/// GET /api/reference/claim-statuses: every status, in lifecycle order, with its allowed transitions from
/// the ClaimStatusTransitions table (FR-27). Terminal statuses are listed with no next statuses.
/// </summary>
public sealed record GetClaimStatusesQuery : IQuery<IReadOnlyList<ClaimStatusDto>>;

internal sealed class GetClaimStatusesHandler(IReferenceDataReader reader) : IRequestHandler<GetClaimStatusesQuery, IReadOnlyList<ClaimStatusDto>>
{
    public async Task<IReadOnlyList<ClaimStatusDto>> Handle(GetClaimStatusesQuery request, CancellationToken cancellationToken)
    {
        var transitions = await reader.ListStatusTransitionsAsync(cancellationToken);
        return Enum.GetValues<ClaimStatus>()
            .Select(status => new ClaimStatusDto(status, transitions.Where(t => t.FromStatus == status).OrderBy(t => t.ToStatus).ToList()))
            .ToList();
    }
}

/// <summary>GET /api/policies?search= : policy number or client name, contains-match (S10.3, FR-14).</summary>
public sealed record SearchPoliciesQuery(string? Term, int Limit = SearchPoliciesQuery.DefaultLimit) : IQuery<IReadOnlyList<PolicySummaryDto>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxTermLength = 100;
}

internal sealed class SearchPoliciesQueryValidator : AbstractValidator<SearchPoliciesQuery>
{
    public SearchPoliciesQueryValidator()
    {
        RuleFor(q => q.Term).MaximumLength(SearchPoliciesQuery.MaxTermLength).WithErrorCode("SEARCH_TERM_TOO_LONG");
        RuleFor(q => q.Limit).InclusiveBetween(1, SearchPoliciesQuery.MaxLimit).WithErrorCode("LIMIT_OUT_OF_RANGE");
    }
}

internal sealed class SearchPoliciesHandler(IReferenceDataReader reader, TimeProvider clock)
    : IRequestHandler<SearchPoliciesQuery, IReadOnlyList<PolicySummaryDto>>
{
    public Task<IReadOnlyList<PolicySummaryDto>> Handle(SearchPoliciesQuery request, CancellationToken cancellationToken) =>
        reader.SearchPoliciesAsync(request.Term?.Trim(), request.Limit, Today(clock), cancellationToken);

    internal static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

/// <summary>GET /api/policies/{id}/coverage (B3.3.2).</summary>
public sealed record GetPolicyCoverageQuery(Guid PolicyId) : IQuery<PolicyCoverageDto>;

internal sealed class GetPolicyCoverageHandler(IReferenceDataReader reader, TimeProvider clock)
    : IRequestHandler<GetPolicyCoverageQuery, PolicyCoverageDto>
{
    public async Task<PolicyCoverageDto> Handle(GetPolicyCoverageQuery request, CancellationToken cancellationToken) =>
        await reader.GetPolicyCoverageAsync(request.PolicyId, SearchPoliciesHandler.Today(clock), cancellationToken)
        ?? throw new NotFoundException("Policy", request.PolicyId);
}
