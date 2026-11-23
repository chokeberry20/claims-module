using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using FluentValidation;

namespace ClaimsModule.Application.Claims;

// Request-level rules shared by several commands (D-05 layer a). Codes and messages match the
// domain's own checks (brief §3.4: rules live in validators AND in the domain), so a 422 looks the
// same whichever layer raises it.

internal sealed class PartyInputValidator : AbstractValidator<PartyInput>
{
    public PartyInputValidator()
    {
        RuleFor(p => p.Role).IsInEnum().WithErrorCode("PARTY_ROLE_INVALID").WithMessage("Party role is not supported.");
        RuleFor(p => p.Type).IsInEnum().WithErrorCode("PARTY_TYPE_INVALID").WithMessage("Party type must be Person or Company.");

        When(p => p.Type == PartyType.Person, () =>
        {
            RuleFor(p => p.FirstName).NotEmpty().WithErrorCode("PARTY_NAME_REQUIRED").WithMessage("First name is required.")
                .MaximumLength(ClaimParty.MaxPersonNameLength).WithErrorCode("PARTY_NAME_TOO_LONG")
                .WithMessage($"First name must be at most {ClaimParty.MaxPersonNameLength} characters.");
            RuleFor(p => p.LastName).NotEmpty().WithErrorCode("PARTY_NAME_REQUIRED").WithMessage("Last name is required.")
                .MaximumLength(ClaimParty.MaxPersonNameLength).WithErrorCode("PARTY_NAME_TOO_LONG")
                .WithMessage($"Last name must be at most {ClaimParty.MaxPersonNameLength} characters.");
        });

        When(p => p.Type == PartyType.Company, () =>
            RuleFor(p => p.CompanyName).NotEmpty().WithErrorCode("PARTY_NAME_REQUIRED").WithMessage("Company name is required.")
                .MaximumLength(ClaimParty.MaxCompanyNameLength).WithErrorCode("PARTY_NAME_TOO_LONG")
                .WithMessage($"Company name must be at most {ClaimParty.MaxCompanyNameLength} characters."));

        RuleFor(p => p.Email).EmailAddress().WithErrorCode("PARTY_EMAIL_INVALID").WithMessage("Email address is not valid.")
            .MaximumLength(ClaimParty.MaxEmailLength).WithErrorCode("PARTY_EMAIL_INVALID")
            .When(p => !string.IsNullOrWhiteSpace(p.Email));
        RuleFor(p => p.Phone).MaximumLength(ClaimParty.MaxPhoneLength).WithErrorCode("PARTY_PHONE_TOO_LONG")
            .WithMessage($"Phone must be at most {ClaimParty.MaxPhoneLength} characters.");
    }
}

internal sealed class RiskObjectInputValidator : AbstractValidator<RiskObjectInput>
{
    public RiskObjectInputValidator()
    {
        RuleFor(r => r.AssetType).IsInEnum().WithErrorCode("ASSET_TYPE_INVALID").WithMessage("Asset type is not supported.");
        RuleFor(r => r.AssetDescription).NotEmpty().WithErrorCode("ASSET_DESCRIPTION_REQUIRED").WithMessage("Asset description is required.")
            .MaximumLength(ClaimRiskObject.MaxAssetDescriptionLength).WithErrorCode("ASSET_DESCRIPTION_TOO_LONG")
            .WithMessage($"Asset description must be at most {ClaimRiskObject.MaxAssetDescriptionLength} characters.");
        RuleFor(r => r.AssetReference).MaximumLength(ClaimRiskObject.MaxAssetReferenceLength).WithErrorCode("ASSET_REFERENCE_TOO_LONG")
            .WithMessage($"Asset reference must be at most {ClaimRiskObject.MaxAssetReferenceLength} characters.");
    }
}

internal static class ReserveRules
{
    /// <summary>BR-R-01: an opening (Add) or target (Adjust) amount must be positive, except SubrogationRecoverable.</summary>
    public static IRuleBuilderOptions<T, decimal?> ValidReserveAmount<T>(this IRuleBuilder<T, decimal?> rule, Func<T, ReserveComponentType> componentType) =>
        rule.NotNull().WithErrorCode("RESERVE_AMOUNT_REQUIRED").WithMessage("Reserve amount is required.")
            .Must((request, amount) => componentType(request).AllowsNegativeAmounts() ? amount != 0m : amount > 0m)
            .WithErrorCode("RESERVE_AMOUNT_NOT_POSITIVE").WithMessage("Reserve amount must be greater than zero.");

    public static IRuleBuilderOptions<T, string> ValidChangeReason<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithErrorCode("CHANGE_REASON_REQUIRED").WithMessage("Change reason is required.")
            .MaximumLength(ReserveTransaction.MaxChangeReasonLength).WithErrorCode("CHANGE_REASON_TOO_LONG")
            .WithMessage($"Change reason must be at most {ReserveTransaction.MaxChangeReasonLength} characters.");
}
