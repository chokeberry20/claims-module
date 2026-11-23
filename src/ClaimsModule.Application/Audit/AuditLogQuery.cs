using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Audit;

/// <summary>One ClaimAuditLog entry (S9.8). Old and new values are JSON text, as stored.</summary>
public sealed record AuditLogEntryDto(
    Guid AuditLogId,
    string EventType,
    string Description,
    string? OldValue,
    string? NewValue,
    Guid? RelatedEntityId,
    string? RelatedEntityType,
    Guid? CorrelationId,
    Guid? CreatedByUserId,
    DateTimeOffset CreatedAt);

public interface IAuditLogReader
{
    /// <summary>Null when the claim doesn't exist (in this organisation).</summary>
    Task<PagedResult<AuditLogEntryDto>?> ListAsync(Guid claimId, string? eventType, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>GET /api/claims/{id}/audit-log: newest first, paginated (S10.1, FR-71, BR-A-02).</summary>
public sealed record GetAuditLogQuery(Guid ClaimId) : IQuery<PagedResult<AuditLogEntryDto>>
{
    public string? EventType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

internal sealed class GetAuditLogQueryValidator : AbstractValidator<GetAuditLogQuery>
{
    public GetAuditLogQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithErrorCode("PAGE_OUT_OF_RANGE").WithMessage("Page must be 1 or greater.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200).WithErrorCode("PAGE_SIZE_OUT_OF_RANGE").WithMessage("Page size must be between 1 and 200.");
    }
}

internal sealed class GetAuditLogHandler(IAuditLogReader reader) : IRequestHandler<GetAuditLogQuery, PagedResult<AuditLogEntryDto>>
{
    public async Task<PagedResult<AuditLogEntryDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken) =>
        await reader.ListAsync(request.ClaimId, string.IsNullOrWhiteSpace(request.EventType) ? null : request.EventType.Trim(),
            request.Page, request.PageSize, cancellationToken)
        ?? throw new NotFoundException("Claim", request.ClaimId);
}
