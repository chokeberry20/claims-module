using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Common;
using static ClaimsModule.Domain.Tests.Claims.ClaimFactory;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

public sealed class ClaimCreationTests
{
    [Fact]
    public void New_claim_is_Draft_assigned_to_its_creator_and_raises_ClaimCreated()
    {
        var claim = NewDraft(createdBy: Handler2);

        claim.Status.ShouldBe(ClaimStatus.Draft);
        claim.ClaimNumber.Value.ShouldBe("CLM-2026-0000142");
        claim.AssignedHandlerId.ShouldBe(Handler2.UserId);
        claim.AssignedHandlerName.ShouldBe(Handler2.DisplayName);
        claim.ReportedDate.ShouldBe(Now);
        claim.Severity.ShouldBe(ClaimSeverity.Standard);
        claim.ClaimType.ShouldBe("Auto");

        var created = claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimCreated>();
        created.ClaimId.ShouldBe(claim.Id);
        created.ClaimNumber.ShouldBe("CLM-2026-0000142");
        created.ActorUserId.ShouldBe(Handler2.UserId);
    }

    [Fact]
    public void Linked_policy_is_denormalised_onto_the_claim()
    {
        var claim = NewDraft(MeridianPolicy);

        claim.PolicyId.ShouldBe(MeridianPolicy.PolicyId);
        claim.PolicyNumber.ShouldBe("POL-2024-001001");
        claim.ClientName.ShouldBe("Meridian Transport LLC");
    }

    [Fact]
    public void Claim_can_be_created_without_a_policy()
    {
        var claim = NewDraftWithoutPolicy();

        claim.PolicyId.ShouldBeNull();
        claim.PolicyNumber.ShouldBeNull();
    }

    [Fact]
    public void Loss_event_is_stored_in_utc_with_trimmed_text()
    {
        var localLossDate = new DateTimeOffset(2026, 11, 13, 8, 30, 0, TimeSpan.FromHours(-5));
        var details = ValidLoss(localLossDate) with { LossDescription = "   Rear-end collision on the exit ramp.   " };

        var claim = Claim.Create(ClaimNumber.Create(2026, 1), details, "Auto", null, ClaimSeverity.Minor, null, Handler1, Now);

        claim.LossEvent.LossDate.Offset.ShouldBe(TimeSpan.Zero);
        claim.LossEvent.LossDate.ShouldBe(localLossDate);
        claim.LossEvent.LossDescription.ShouldBe("Rear-end collision on the exit ramp.");
        claim.LossEvent.ReportDate.ShouldBe(Now);
        claim.LossEvent.EstimatedLossAmount.ShouldBe(Money.Of(12_500m));
        claim.LossEvent.ClaimId.ShouldBe(claim.Id);
    }

    [Fact]
    public void BR_C_01_loss_date_in_the_future_is_rejected()
    {
        var ex = Should.Throw<DomainRuleViolationException>(() =>
            Claim.Create(ClaimNumber.Create(2026, 1), ValidLoss(Now.AddHours(1)), "Auto", null, ClaimSeverity.Standard, null, Handler1, Now));

        var violation = ex.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe("LOSS_DATE_IN_FUTURE");
        violation.Message.ShouldBe("Loss date cannot be in the future.");
        violation.Field.ShouldBe("LossDate");
    }

    [Fact]
    public void Loss_date_within_the_clock_skew_tolerance_is_accepted() =>
        Should.NotThrow(() =>
            Claim.Create(ClaimNumber.Create(2026, 1), ValidLoss(Now.AddMinutes(4)), "Auto", null, ClaimSeverity.Standard, null, Handler1, Now));

    [Theory]
    [InlineData("Too short")]
    [InlineData("                         ")]
    [InlineData("   nineteen chars!!   ")]
    public void BR_C_07_description_shorter_than_20_characters_is_rejected(string description)
    {
        var ex = Should.Throw<DomainRuleViolationException>(() =>
            Claim.Create(ClaimNumber.Create(2026, 1), ValidLoss() with { LossDescription = description }, "Auto", null, ClaimSeverity.Standard, null, Handler1, Now));

        ex.Violations.ShouldHaveSingleItem().Message.ShouldBe("Loss description is required and must be at least 20 characters.");
    }

    [Fact]
    public void Description_of_exactly_20_characters_is_accepted() =>
        Should.NotThrow(() =>
            Claim.Create(ClaimNumber.Create(2026, 1), ValidLoss() with { LossDescription = new string('x', 20) }, "Auto", null, ClaimSeverity.Standard, null, Handler1, Now));

    [Fact]
    public void All_loss_event_violations_are_reported_together()
    {
        var details = new LossEventDetails(Now.AddDays(2), "short", " ", EstimatedLossAmount: -1m);

        var ex = Should.Throw<DomainRuleViolationException>(() =>
            Claim.Create(ClaimNumber.Create(2026, 1), details, "Auto", null, ClaimSeverity.Standard, null, Handler1, Now));

        ex.Violations.Select(v => v.Code).ShouldBe(
            ["LOSS_DATE_IN_FUTURE", "LOSS_DESCRIPTION_TOO_SHORT", "CAUSE_OF_LOSS_INVALID", "ESTIMATED_LOSS_NEGATIVE"],
            ignoreOrder: true);
    }

    [Fact]
    public void Missing_loss_date_is_reported_as_required() =>
        Should.Throw<DomainRuleViolationException>(() =>
                Claim.Create(ClaimNumber.Create(2026, 1), ValidLoss() with { LossDate = default }, "Auto", null, ClaimSeverity.Standard, null, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Message.ShouldBe("Loss date is required.");

    [Fact]
    public void Loss_details_can_be_corrected_while_Draft()
    {
        var claim = NewDraft();
        claim.ClearDomainEvents();

        claim.UpdateLossDetails(ValidLoss(cause: "COL-FIRE"), "Property", Handler1, Now);

        claim.LossEvent.CauseOfLossCode.ShouldBe("COL-FIRE");
        claim.ClaimType.ShouldBe("Property");
        claim.LossEvent.ReportDate.ShouldBe(Now);
        claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimLossDetailsUpdated>();
    }

    [Fact]
    public void Severity_change_is_recorded_once()
    {
        var claim = NewDraft();
        claim.ClearDomainEvents();

        claim.ChangeSeverity(ClaimSeverity.Critical, Handler1, Now);
        claim.ChangeSeverity(ClaimSeverity.Critical, Handler1, Now);

        claim.Severity.ShouldBe(ClaimSeverity.Critical);
        claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimSeverityChanged>();
    }
}
