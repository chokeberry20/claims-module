using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using static ClaimsModule.Domain.Tests.Claims.ClaimFactory;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

public sealed class ClaimValidationIssueTests
{
    private const string SomeCritical = ValidationIssueCodes.CauseOfLossInactive;

    [Fact]
    public void New_findings_raise_open_issues_and_VALIDATION_ISSUE_ADDED_events()
    {
        var claim = NewDraft();
        claim.ClearDomainEvents();

        claim.ApplyValidationFindings([Critical(ValidationIssueCodes.NoClaimant), Warning(ValidationIssueCodes.NoRiskObjects)], Now);

        claim.ValidationIssues.Count.ShouldBe(2);
        claim.ValidationIssues.ShouldAllBe(i => i.Status == ValidationIssueStatus.Open);
        claim.HasOpenCriticalIssues.ShouldBeTrue();
        claim.DomainEvents.OfType<ValidationIssueRaised>().Select(e => e.Code)
            .ShouldBe([ValidationIssueCodes.NoClaimant, ValidationIssueCodes.NoRiskObjects], ignoreOrder: true);
    }

    [Fact]
    public void Re_applying_the_same_findings_does_not_duplicate_issues_or_events()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(SomeCritical)], Now);
        claim.ClearDomainEvents();

        claim.ApplyValidationFindings([Critical(SomeCritical)], Now.AddMinutes(1));

        claim.ValidationIssues.ShouldHaveSingleItem();
        claim.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Issue_is_resolved_when_its_condition_disappears_and_reraised_if_it_returns()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(SomeCritical)], Now);

        claim.ApplyValidationFindings([], Now.AddMinutes(1));

        var first = claim.ValidationIssues.ShouldHaveSingleItem();
        first.Status.ShouldBe(ValidationIssueStatus.Resolved);
        first.ResolvedAt.ShouldBe(Now.AddMinutes(1));
        claim.HasOpenCriticalIssues.ShouldBeFalse();

        claim.ApplyValidationFindings([Critical(SomeCritical)], Now.AddMinutes(2));

        claim.ValidationIssues.Count.ShouldBe(2);
        claim.HasOpenCriticalIssues.ShouldBeTrue();
    }

    [Fact]
    public void BR_C_02_acknowledging_warnings_marks_them_acknowledged_and_they_are_not_reraised()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Warning(ValidationIssueCodes.LossDateOutsidePolicyPeriod), Critical(SomeCritical)], Now);
        claim.ClearDomainEvents();

        claim.AcknowledgeWarnings(Handler1, Now).ShouldBe(1);
        claim.ApplyValidationFindings([Warning(ValidationIssueCodes.LossDateOutsidePolicyPeriod), Critical(SomeCritical)], Now);

        var warning = claim.ValidationIssues.Single(i => i.Severity == ValidationSeverity.Warning);
        warning.Status.ShouldBe(ValidationIssueStatus.Acknowledged);
        warning.AcknowledgedByUserId.ShouldBe(Handler1.UserId);
        claim.ValidationIssues.Count.ShouldBe(2);
        claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ValidationWarningsAcknowledged>();
        claim.AcknowledgeWarnings(Handler1, Now).ShouldBe(0);
    }

    [Fact]
    public void D_07_supervisor_can_waive_an_open_critical_with_a_reason()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(SomeCritical)], Now);
        var issue = claim.ValidationIssues.Single();
        claim.ClearDomainEvents();

        claim.WaiveValidationIssue(issue.Id, "  Code retired after intake; confirmed with underwriting.  ", Supervisor, Now);

        issue.Status.ShouldBe(ValidationIssueStatus.Waived);
        issue.WaivedByUserId.ShouldBe(Supervisor.UserId);
        issue.WaiverReason.ShouldBe("Code retired after intake; confirmed with underwriting.");
        claim.HasOpenCriticalIssues.ShouldBeFalse();
        var waived = claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ValidationIssueWaived>();
        waived.Code.ShouldBe(SomeCritical);
    }

    [Fact]
    public void D_07_waived_issue_is_not_reraised_by_revalidation()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(SomeCritical)], Now);
        claim.WaiveValidationIssue(claim.ValidationIssues.Single().Id, "Accepted risk.", Manager, Now);

        claim.ApplyValidationFindings([Critical(SomeCritical)], Now.AddHours(1));

        claim.ValidationIssues.ShouldHaveSingleItem().Status.ShouldBe(ValidationIssueStatus.Waived);
        claim.HasOpenCriticalIssues.ShouldBeFalse();
    }

    [Fact]
    public void D_07_handler_cannot_waive() =>
        WithOpenIssue(Critical(SomeCritical), (claim, id) =>
            Should.Throw<DomainForbiddenException>(() => claim.WaiveValidationIssue(id, "reason", Handler1, Now))
                .Code.ShouldBe("VALIDATION_WAIVE_ROLE"));

    [Fact]
    public void D_07_NO_CLAIMANT_is_not_waivable() =>
        WithOpenIssue(Critical(ValidationIssueCodes.NoClaimant), (claim, id) =>
        {
            var ex = Should.Throw<DomainRuleViolationException>(() => claim.WaiveValidationIssue(id, "reason", Manager, Now));
            ex.Violations.ShouldHaveSingleItem().Message.ShouldBe("This issue cannot be waived.");
        });

    [Fact]
    public void D_07_warnings_are_acknowledged_not_waived() =>
        WithOpenIssue(Warning(ValidationIssueCodes.NoPolicy), (claim, id) =>
            Should.Throw<DomainRuleViolationException>(() => claim.WaiveValidationIssue(id, "reason", Supervisor, Now))
                .Violations.ShouldHaveSingleItem().Code.ShouldBe("VALIDATION_WAIVE_NOT_CRITICAL"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void D_07_waiver_reason_is_mandatory(string? reason) =>
        WithOpenIssue(Critical(SomeCritical), (claim, id) =>
            Should.Throw<DomainRuleViolationException>(() => claim.WaiveValidationIssue(id, reason!, Supervisor, Now))
                .Violations.ShouldHaveSingleItem().Code.ShouldBe("REASON_REQUIRED"));

    [Fact]
    public void D_07_waiver_reason_is_limited_to_500_characters() =>
        WithOpenIssue(Critical(SomeCritical), (claim, id) =>
            Should.Throw<DomainRuleViolationException>(() => claim.WaiveValidationIssue(id, new string('r', 501), Supervisor, Now))
                .Violations.ShouldHaveSingleItem().Code.ShouldBe("REASON_TOO_LONG"));

    [Fact]
    public void Resolved_issue_cannot_be_waived()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(SomeCritical)], Now);
        var id = claim.ValidationIssues.Single().Id;
        claim.ApplyValidationFindings([], Now);

        Should.Throw<DomainRuleViolationException>(() => claim.WaiveValidationIssue(id, "reason", Supervisor, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("VALIDATION_WAIVE_NOT_OPEN");
    }

    private static void WithOpenIssue(ValidationFinding finding, Action<ClaimsModule.Domain.Claims.Claim, Guid> assert)
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([finding], Now);
        assert(claim, claim.ValidationIssues.Single().Id);
    }
}
