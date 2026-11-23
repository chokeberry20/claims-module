using AutoMapper;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Reserves;
using MediatR;

namespace ClaimsModule.Application.Reserves;

/// <summary>One row of the reserve history (S6.6, S9.6, S11.3 reserves tab).</summary>
public sealed record ReserveTransactionDto
{
    public Guid TransactionId { get; init; }
    public Guid ReserveComponentId { get; init; }
    public ReserveComponentType ComponentType { get; init; }
    public ReserveTransactionType TransactionType { get; init; }
    public int ChangeSequence { get; init; }
    public decimal Amount { get; init; }
    public decimal PreviousBalance { get; init; }
    public decimal NewBalance { get; init; }
    public string ChangeReason { get; init; } = string.Empty;
    public ApprovalStatus ApprovalStatus { get; init; }
    public ApprovalLevel RequiredApprovalLevel { get; init; }
    public bool RequiresLimitOverride { get; init; }
    public Guid SubmittedByUserId { get; init; }
    public string SubmittedByName { get; init; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public Guid? RejectedByUserId { get; init; }
    public DateTimeOffset? RejectedAt { get; init; }
    public string? RejectionReason { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public PostingStatus PostingStatus { get; init; }
    public string? PostingJobId { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public string? PostingFailureReason { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}

/// <summary>GET /api/claims/{id}/reserves: totals per component and the full history, newest first.</summary>
public sealed record ClaimReservesDto(Guid ClaimId, ReserveTotalsDto Totals, IReadOnlyList<ReserveTransactionDto> History);

public interface IReserveReader
{
    /// <summary>Null when the claim doesn't exist (in this organisation).</summary>
    Task<ClaimReservesDto?> GetForClaimAsync(Guid claimId, CancellationToken cancellationToken);
}

public sealed record GetClaimReservesQuery(Guid ClaimId) : IQuery<ClaimReservesDto>;

internal sealed class GetClaimReservesHandler(IReserveReader reader) : IRequestHandler<GetClaimReservesQuery, ClaimReservesDto>
{
    public async Task<ClaimReservesDto> Handle(GetClaimReservesQuery request, CancellationToken cancellationToken) =>
        await reader.GetForClaimAsync(request.ClaimId, cancellationToken) ?? throw new NotFoundException("Claim", request.ClaimId);
}

/// <summary>ReserveTransaction → history row: a flat map (D-67).</summary>
public sealed class ReservesProfile : Profile
{
    public ReservesProfile() =>
        CreateMap<ReserveTransaction, ReserveTransactionDto>()
            .ForMember(d => d.TransactionId, o => o.MapFrom(t => t.Id));
}
