using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Claims;
using FluentValidation;

namespace ClaimsModule.Application.Claims;

/// <summary>The loss-event fields shared by FNOL (CreateClaimCommand) and the Draft correction (UpdateLossDetailsCommand).</summary>
public interface ILossDetailsInput
{
    DateTimeOffset LossDate { get; }
    string LossDescription { get; }
    string CauseOfLossCode { get; }
    string? LossLocation { get; }
    decimal? EstimatedLossAmount { get; }
    string? PoliceReportNumber { get; }
}

/// <summary>
/// S8 loss-event rows (BR-C-01, BR-C-05, BR-C-07, D-15), with the domain's codes and messages.
/// Included by both commands' validators (IValidator is contravariant).
/// </summary>
internal sealed class LossDetailsValidator : AbstractValidator<ILossDetailsInput>
{
    public const int MaxLossDescriptionLength = 4000;

    public LossDetailsValidator(TimeProvider clock, ICauseOfLossCodeRepository causeCodes)
    {
        RuleFor(c => c.LossDate)
            .NotEqual(default(DateTimeOffset)).WithErrorCode("LOSS_DATE_REQUIRED").WithMessage("Loss date is required.")
            .Must(date => date <= clock.GetUtcNow() + LossEvent.FutureDateTolerance)
            .WithErrorCode("LOSS_DATE_IN_FUTURE").WithMessage("Loss date cannot be in the future.");

        RuleFor(c => c.LossDescription)
            .Must(d => d is not null && d.Trim().Length >= LossEvent.MinDescriptionLength)
            .WithErrorCode("LOSS_DESCRIPTION_TOO_SHORT").WithMessage("Loss description is required and must be at least 20 characters.")
            .MaximumLength(MaxLossDescriptionLength).WithErrorCode("LOSS_DESCRIPTION_TOO_LONG")
            .WithMessage($"Loss description must be at most {MaxLossDescriptionLength} characters.");

        RuleFor(c => c.CauseOfLossCode)
            .MustAsync(async (code, ct) =>
                !string.IsNullOrWhiteSpace(code) && (await causeCodes.GetByCodeAsync(code.Trim(), ct))?.IsActive == true)
            .WithErrorCode("CAUSE_OF_LOSS_INVALID").WithMessage("Cause of loss code is not recognised or is inactive.");

        RuleFor(c => c.LossLocation).MaximumLength(LossEvent.MaxLocationLength).WithErrorCode("LOSS_LOCATION_TOO_LONG")
            .WithMessage($"Loss location must be at most {LossEvent.MaxLocationLength} characters.");
        RuleFor(c => c.EstimatedLossAmount).GreaterThanOrEqualTo(0m).WithErrorCode("ESTIMATED_LOSS_NEGATIVE")
            .WithMessage("Estimated loss amount cannot be negative.");
        RuleFor(c => c.PoliceReportNumber).MaximumLength(LossEvent.MaxPoliceReportNumberLength).WithErrorCode("POLICE_REPORT_NUMBER_TOO_LONG")
            .WithMessage($"Police report number must be at most {LossEvent.MaxPoliceReportNumberLength} characters.");
    }
}
