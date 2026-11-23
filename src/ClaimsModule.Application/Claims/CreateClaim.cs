using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

/// <summary>An optional initial reserve submitted with the FNOL (S5.2 step 3). It opens the component (Add).</summary>
public sealed record InitialReserveInput(ReserveComponentType ComponentType, decimal? Amount, string ChangeReason);

/// <summary>
/// FNOL intake in one call (S5.2, S10.1, FR-01): claim, loss event, parties, risk objects and an
/// optional initial reserve, all in one transaction. Request-level rules give a 422 and save
/// nothing (D-05 layer a). State-level findings are saved on the Draft claim as validation issues
/// (D-05 layer b).
/// </summary>
public sealed record CreateClaimCommand : ICommand<CreateClaimResult>, IIdempotentCommand, ILossDetailsInput
{
    public Guid? PolicyId { get; init; }
    public DateTimeOffset LossDate { get; init; }
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
    public string? IdempotencyKey { get; init; }
}

public sealed record CreateClaimResult(
    Guid ClaimId,
    string ClaimNumber,
    ClaimStatus Status,
    IReadOnlyList<ValidationIssueDto> ValidationIssues,
    ReserveSubmissionDto? InitialReserve);

internal sealed class CreateClaimCommandValidator : AbstractValidator<CreateClaimCommand>
{
    public CreateClaimCommandValidator(
        TimeProvider clock,
        ICauseOfLossCodeRepository causeCodes,
        IPolicyRepository policies)
    {
        Include(new LossDetailsValidator(clock, causeCodes));

        RuleFor(c => c.PolicyId)
            .MustAsync(async (id, ct) => await policies.GetByIdAsync(id!.Value, ct) is not null)
            .When(c => c.PolicyId is not null)
            .WithErrorCode("POLICY_NOT_FOUND").WithMessage("The selected policy does not exist.");

        RuleFor(c => c.Severity).IsInEnum().WithErrorCode("SEVERITY_INVALID").WithMessage("Severity is not supported.");
        RuleFor(c => c.Notes).MaximumLength(4000).WithErrorCode("NOTES_TOO_LONG").WithMessage("Notes must be at most 4000 characters.");

        RuleForEach(c => c.Parties).SetValidator(new PartyInputValidator());
        RuleForEach(c => c.RiskObjects).SetValidator(new RiskObjectInputValidator());
        RuleFor(c => c.RiskObjects).Must(r => r.Count(o => o.IsPrimary) <= 1)
            .WithErrorCode("RISK_OBJECT_MULTIPLE_PRIMARY").WithMessage("Only one risk object can be primary.");

        When(c => c.InitialReserve is not null, () =>
        {
            RuleFor(c => c.InitialReserve!.ComponentType).IsInEnum().WithErrorCode("RESERVE_COMPONENT_INVALID")
                .WithMessage("Invalid reserve component type.").OverridePropertyName("InitialReserve.ComponentType");
            RuleFor(c => c.InitialReserve!.Amount).ValidReserveAmount(c => c.InitialReserve!.ComponentType)
                .OverridePropertyName("InitialReserve.Amount");
            RuleFor(c => c.InitialReserve!.ChangeReason).ValidChangeReason().OverridePropertyName("InitialReserve.ChangeReason");
        });
    }
}

internal sealed class CreateClaimHandler(
    ICurrentUser currentUser,
    TimeProvider clock,
    IClaimNumberGenerator claimNumbers,
    IClaimRepository claims,
    IReserveComponentRepository reserves,
    IPolicyRepository policies,
    ICauseOfLossCodeRepository causeCodes,
    ClaimValidationContextBuilder validation) : IRequestHandler<CreateClaimCommand, CreateClaimResult>
{
    public async Task<CreateClaimResult> Handle(CreateClaimCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.GetActor();
        var now = clock.GetUtcNow();

        var cause = await causeCodes.GetByCodeAsync(request.CauseOfLossCode.Trim(), cancellationToken)
            ?? throw new DomainRuleViolationException("CAUSE_OF_LOSS_INVALID", "Cause of loss code is not recognised or is inactive.", "CauseOfLossCode");
        var policy = request.PolicyId is { } policyId
            ? await policies.GetByIdAsync(policyId, cancellationToken)
              ?? throw new DomainRuleViolationException("POLICY_NOT_FOUND", "The selected policy does not exist.", "PolicyId")
            : null;

        // Allocated inside the command's transaction: a rollback releases the number (D-14).
        var number = await claimNumbers.NextAsync(now.UtcDateTime.Year, cancellationToken);

        var claim = Claim.Create(
            number,
            new LossEventDetails(request.LossDate, request.LossDescription, cause.Code, request.LossLocation, request.EstimatedLossAmount, request.PoliceReportNumber),
            claimType: cause.PerilCategory.ToString(), // D-33
            policy?.ToSnapshot(),
            request.Severity,
            request.Notes,
            actor,
            now);

        foreach (var party in request.Parties)
        {
            claim.AddParty(party.ToDetails(), actor, now);
        }

        foreach (var riskObject in request.RiskObjects)
        {
            claim.AddRiskObject(riskObject.ToDetails(), actor, now);
        }

        await validation.RevalidateAsync(claim, now, cancellationToken);
        claims.Add(claim);

        ReserveSubmissionDto? initialReserve = null;
        if (request.InitialReserve is { } reserve)
        {
            // Task 25a: BR-C-06 (policy linked) and D-20 (status) before touching reserves. A failure
            // here rolls back the whole FNOL, claim number included.
            claim.EnsureReserveChangesAllowed();
            var component = ClaimReserveComponent.Create(claim.Id, reserve.ComponentType);
            var result = component.Submit(ReserveTransactionType.Add, reserve.Amount, reserve.ChangeReason, actor, Money.Zero, now);
            reserves.Add(component);
            claim.RecordReserveActivity(now);
            initialReserve = ReserveSubmissionDto.From(component, result);
        }

        return new CreateClaimResult(
            claim.Id,
            claim.ClaimNumber.Value,
            claim.Status,
            claim.ValidationIssues.Select(ValidationIssueDto.From).ToList(),
            initialReserve);
    }
}
