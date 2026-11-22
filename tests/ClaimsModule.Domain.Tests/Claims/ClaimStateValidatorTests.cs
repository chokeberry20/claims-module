using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Claims.Workflow;
using static ClaimsModule.Domain.Tests.Claims.ClaimFactory;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

public sealed class ClaimStateValidatorTests
{
    private static readonly ClaimValidationContext MeridianContext = new(MeridianPeriod, CauseOfLossCodeIsActive: true);

    [Fact]
    public void Complete_draft_within_policy_period_has_no_findings() =>
        ClaimStateValidator.Evaluate(NewCompleteDraft(), MeridianContext).ShouldBeEmpty();

    [Fact]
    public void New_draft_without_parties_or_risk_objects_reports_critical_claimant_and_risk_warning()
    {
        var findings = ClaimStateValidator.Evaluate(NewDraft(), MeridianContext);

        findings.Select(f => (f.Code, f.Severity)).ShouldBe(
        [
            (ValidationIssueCodes.NoClaimant, ValidationSeverity.Critical),
            (ValidationIssueCodes.NoRiskObjects, ValidationSeverity.Warning),
        ],
        ignoreOrder: true);
        findings.Single(f => f.Code == ValidationIssueCodes.NoClaimant).Message
            .ShouldBe("At least one Claimant party is required to open a claim.");
    }

    [Fact]
    public void BR_C_06_no_policy_is_a_warning_and_skips_the_period_check()
    {
        var claim = NewDraftWithoutPolicy();
        claim.AddParty(Claimant(), Handler1, Now);
        claim.AddRiskObject(Vehicle(), Handler1, Now);

        var finding = ClaimStateValidator.Evaluate(claim, new ClaimValidationContext(null, true)).ShouldHaveSingleItem();

        finding.Code.ShouldBe(ValidationIssueCodes.NoPolicy);
        finding.Severity.ShouldBe(ValidationSeverity.Warning);
        finding.Message.ShouldBe("No policy linked. Policy must be associated before reserves can be set.");
    }

    [Fact]
    public void BR_C_02_expired_policy_seed_POL_2023_000099_gives_outside_period_warning()
    {
        var claim = NewDraft(ArchivedPolicy);
        claim.AddParty(Claimant(), Handler1, Now);
        claim.AddRiskObject(Vehicle(), Handler1, Now);

        var finding = ClaimStateValidator.Evaluate(claim, new ClaimValidationContext(ArchivedPeriod, true)).ShouldHaveSingleItem();

        finding.Code.ShouldBe(ValidationIssueCodes.LossDateOutsidePolicyPeriod);
        finding.Severity.ShouldBe(ValidationSeverity.Warning);
        finding.Message.ShouldBe("Loss date is outside the policy effective period.");
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00+00:00", false)] // effective date, inclusive
    [InlineData("2026-12-31T23:59:59+00:00", false)] // expiration date, inclusive
    [InlineData("2023-12-31T23:59:59+00:00", true)]
    [InlineData("2027-01-01T00:00:00+00:00", true)]
    [InlineData("2023-12-31T20:00:00-05:00", false)] // = 2024-01-01T01:00Z, inside in UTC (D-15)
    public void Policy_period_bounds_are_inclusive_and_compared_in_utc(string lossDate, bool expectWarning)
    {
        var claim = Claim.Create(
            ClaimNumber.Create(2026, 7),
            ValidLoss(DateTimeOffset.Parse(lossDate, System.Globalization.CultureInfo.InvariantCulture)),
            "Auto",
            MeridianPolicy,
            ClaimSeverity.Standard,
            null,
            Handler1,
            new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero));
        claim.AddParty(Claimant(), Handler1, Now);
        claim.AddRiskObject(Vehicle(), Handler1, Now);

        var findings = ClaimStateValidator.Evaluate(claim, MeridianContext);

        findings.Any(f => f.Code == ValidationIssueCodes.LossDateOutsidePolicyPeriod).ShouldBe(expectWarning);
    }

    [Fact]
    public void BR_C_05_inactive_cause_of_loss_code_is_critical()
    {
        var finding = ClaimStateValidator.Evaluate(NewCompleteDraft(), new ClaimValidationContext(MeridianPeriod, false))
            .ShouldHaveSingleItem();

        finding.Code.ShouldBe(ValidationIssueCodes.CauseOfLossInactive);
        finding.Severity.ShouldBe(ValidationSeverity.Critical);
    }

    [Fact]
    public void Linked_policy_without_period_in_context_is_a_programming_error() =>
        Should.Throw<ArgumentException>(() => ClaimStateValidator.Evaluate(NewDraft(), new ClaimValidationContext(null, true)));

    [Fact]
    public void Revalidate_persists_findings_and_fixing_the_data_resolves_them_end_to_end()
    {
        var claim = NewDraft();
        claim.Revalidate(MeridianContext, Now);
        claim.ValidationIssues.Count.ShouldBe(2);

        Should.Throw<ClaimsModule.Domain.Common.DomainRuleViolationException>(() => ScenarioOpen(claim));

        claim.AddParty(Claimant(), Handler1, Now);
        claim.AddRiskObject(Vehicle(), Handler1, Now);
        claim.Revalidate(MeridianContext, Now).ShouldBeEmpty();

        claim.ValidationIssues.ShouldAllBe(i => i.Status == ValidationIssueStatus.Resolved);
        ScenarioOpen(claim);
        claim.Status.ShouldBe(ClaimStatus.Open);
    }

    private static void ScenarioOpen(Claim claim) =>
        claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now);
}
