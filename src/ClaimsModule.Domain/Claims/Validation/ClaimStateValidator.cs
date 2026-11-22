namespace ClaimsModule.Domain.Claims.Validation;

/// <summary>
/// Reference facts that state validation needs but the Claim aggregate doesn't own. The application
/// layer loads them: the linked policy's period from the Policies table, and whether the cause of
/// loss code is still active in CauseOfLossCodes.
/// </summary>
public sealed record ClaimValidationContext(PolicyPeriod? LinkedPolicyPeriod, bool CauseOfLossCodeIsActive);

/// <summary>
/// State-level validation of a claim (S5.4, D-05). The findings are stored as ClaimValidationIssues
/// and re-checked on every change and by POST /claims/{id}/validate. Request-level rules
/// (BR-C-01 future date, BR-C-07 description, BR-C-05 at intake) are rejected with 422 before
/// anything is saved, so they are not here. The one exception is a cause code that becomes
/// inactive after intake.
/// </summary>
public static class ClaimStateValidator
{
    public static IReadOnlyList<ValidationFinding> Evaluate(Claim claim, ClaimValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(context);

        if (claim.PolicyId is not null && context.LinkedPolicyPeriod is null)
        {
            throw new ArgumentException("The linked policy's period is required when a policy is linked.", nameof(context));
        }

        var findings = new List<ValidationFinding>();

        // BR-C-03 / BR-P-01
        if (!claim.HasActiveClaimant)
        {
            findings.Add(new(
                ValidationIssueCodes.NoClaimant,
                ValidationSeverity.Critical,
                "At least one Claimant party is required to open a claim.",
                "ClaimParties"));
        }

        // BR-C-05: the code must still exist and be active
        if (!context.CauseOfLossCodeIsActive)
        {
            findings.Add(new(
                ValidationIssueCodes.CauseOfLossInactive,
                ValidationSeverity.Critical,
                "Cause of loss code is not recognised or is inactive.",
                "CauseOfLossCode"));
        }

        if (claim.PolicyId is null)
        {
            // BR-C-06
            findings.Add(new(
                ValidationIssueCodes.NoPolicy,
                ValidationSeverity.Warning,
                "No policy linked. Policy must be associated before reserves can be set.",
                "PolicyId"));
        }
        else if (!context.LinkedPolicyPeriod!.Contains(claim.LossEvent.LossDate))
        {
            // BR-C-02
            findings.Add(new(
                ValidationIssueCodes.LossDateOutsidePolicyPeriod,
                ValidationSeverity.Warning,
                "Loss date is outside the policy effective period.",
                "LossDate"));
        }

        // S5.4
        if (claim.RiskObjects.Count == 0)
        {
            findings.Add(new(
                ValidationIssueCodes.NoRiskObjects,
                ValidationSeverity.Warning,
                "No risk objects are linked to the claim.",
                "RiskObjects"));
        }

        return findings;
    }
}
