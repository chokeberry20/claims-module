using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Messaging;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

/// <summary>The result of adding a child to a claim: its new id plus the re-evaluated claim state.</summary>
public sealed record ClaimChildAddedResult(Guid ClaimId, Guid ChildId, IReadOnlyList<ValidationIssueDto> ValidationIssues);

// ------------------------------------------------------------------ parties (S10.1, BR-P-01, BR-P-02)

/// <summary>POST /api/claims/{id}/parties. Duplicate roles are allowed (BR-P-02).</summary>
public sealed record AddPartyCommand(Guid ClaimId, PartyInput Party) : IdempotentCommand, ICommand<ClaimChildAddedResult>;

internal sealed class AddPartyValidator : AbstractValidator<AddPartyCommand>
{
    public AddPartyValidator() => RuleFor(c => c.Party).NotNull().SetValidator(new PartyInputValidator());
}

internal sealed class AddPartyHandler(
    IClaimRepository claims,
    ClaimValidationContextBuilder validation,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<AddPartyCommand, ClaimChildAddedResult>
{
    public async Task<ClaimChildAddedResult> Handle(AddPartyCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var now = clock.GetUtcNow();
        var party = claim.AddParty(request.Party.ToDetails(), currentUser.GetActor(), now);
        await validation.RevalidateAsync(claim, now, cancellationToken);
        return new ClaimChildAddedResult(claim.Id, party.Id, claim.ValidationIssues.Select(ValidationIssueDto.From).ToList());
    }
}

/// <summary>DELETE /api/claims/{id}/parties/{partyId}: soft removal (IsActive = false). The last Claimant can't be removed (BR-P-01).</summary>
public sealed record RemovePartyCommand(Guid ClaimId, Guid PartyId) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class RemovePartyHandler(
    IClaimRepository claims,
    ClaimValidationContextBuilder validation,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<RemovePartyCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(RemovePartyCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var now = clock.GetUtcNow();
        claim.RemoveParty(request.PartyId, currentUser.GetActor(), now);
        await validation.RevalidateAsync(claim, now, cancellationToken);
        return ClaimChangeResult.From(claim);
    }
}

// ------------------------------------------------------------------ risk objects (S9.4, D-31)

public sealed record AddRiskObjectCommand(Guid ClaimId, RiskObjectInput RiskObject) : IdempotentCommand, ICommand<ClaimChildAddedResult>;

internal sealed class AddRiskObjectValidator : AbstractValidator<AddRiskObjectCommand>
{
    public AddRiskObjectValidator() => RuleFor(c => c.RiskObject).NotNull().SetValidator(new RiskObjectInputValidator());
}

internal sealed class AddRiskObjectHandler(
    IClaimRepository claims,
    ClaimValidationContextBuilder validation,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<AddRiskObjectCommand, ClaimChildAddedResult>
{
    public async Task<ClaimChildAddedResult> Handle(AddRiskObjectCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var now = clock.GetUtcNow();
        var riskObject = claim.AddRiskObject(request.RiskObject.ToDetails(), currentUser.GetActor(), now);
        await validation.RevalidateAsync(claim, now, cancellationToken);
        return new ClaimChildAddedResult(claim.Id, riskObject.Id, claim.ValidationIssues.Select(ValidationIssueDto.From).ToList());
    }
}

public sealed record RemoveRiskObjectCommand(Guid ClaimId, Guid RiskObjectId) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class RemoveRiskObjectHandler(
    IClaimRepository claims,
    ClaimValidationContextBuilder validation,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<RemoveRiskObjectCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(RemoveRiskObjectCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var now = clock.GetUtcNow();
        claim.RemoveRiskObject(request.RiskObjectId, currentUser.GetActor(), now);
        await validation.RevalidateAsync(claim, now, cancellationToken);
        return ClaimChangeResult.From(claim);
    }
}
