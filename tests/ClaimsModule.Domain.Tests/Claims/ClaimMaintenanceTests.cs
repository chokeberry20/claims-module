using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Common;
using static ClaimsModule.Domain.Tests.Claims.ClaimFactory;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

public sealed class ClaimMaintenanceTests
{
    // ------------------------------------------------------------------ parties

    [Fact]
    public void Multiple_claimants_are_allowed_BR_P_02()
    {
        var claim = NewDraft();

        claim.AddParty(Claimant("Jane", "Doe"), Handler1, Now);
        claim.AddParty(Claimant("Joe", "Bloggs"), Handler1, Now);

        claim.ActiveClaimants.Count().ShouldBe(2);
        claim.DomainEvents.OfType<ClaimPartyAdded>().Count().ShouldBe(2);
    }

    [Fact]
    public void Person_party_requires_first_and_last_name()
    {
        var claim = NewDraft();

        var ex = Should.Throw<DomainRuleViolationException>(() =>
            claim.AddParty(new PartyDetails(PartyRole.Claimant, PartyType.Person, "Jane"), Handler1, Now));

        ex.Violations.ShouldHaveSingleItem().Field.ShouldBe("LastName");
    }

    [Fact]
    public void Company_party_requires_company_name_and_uses_it_as_display_name()
    {
        var claim = NewDraft();

        Should.Throw<DomainRuleViolationException>(() =>
            claim.AddParty(new PartyDetails(PartyRole.ThirdParty, PartyType.Company), Handler1, Now));

        var party = claim.AddParty(new PartyDetails(PartyRole.ThirdParty, PartyType.Company, CompanyName: " Acme Haulage Ltd "), Handler1, Now);
        party.DisplayName.ShouldBe("Acme Haulage Ltd");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("Jane <jane@example.com>")]
    public void Party_email_must_be_a_plain_valid_address(string email) =>
        Should.Throw<DomainRuleViolationException>(() =>
                NewDraft().AddParty(Claimant() with { Email = email }, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("PARTY_EMAIL_INVALID");

    [Fact]
    public void Removing_a_party_is_a_soft_remove()
    {
        var claim = NewDraft();
        claim.AddParty(Claimant(), Handler1, Now);
        var witness = claim.AddParty(Witness(), Handler1, Now);

        claim.RemoveParty(witness.Id, Handler1, Now);

        witness.IsActive.ShouldBeFalse();
        witness.RemovedAt.ShouldBe(Now);
        claim.Parties.Count.ShouldBe(2);
        claim.DomainEvents.OfType<ClaimPartyRemoved>().ShouldHaveSingleItem().PartyId.ShouldBe(witness.Id);
    }

    [Fact]
    public void FR_30_the_last_active_claimant_cannot_be_removed()
    {
        var claim = NewDraft();
        var claimant = claim.AddParty(Claimant(), Handler1, Now);

        var ex = Should.Throw<DomainRuleViolationException>(() => claim.RemoveParty(claimant.Id, Handler1, Now));

        ex.Violations.ShouldHaveSingleItem().Code.ShouldBe("PARTY_LAST_CLAIMANT");
        claimant.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void One_of_two_claimants_can_be_removed_but_then_not_the_other()
    {
        var claim = NewDraft();
        var first = claim.AddParty(Claimant("Jane", "Doe"), Handler1, Now);
        var second = claim.AddParty(Claimant("Joe", "Bloggs"), Handler1, Now);

        claim.RemoveParty(first.Id, Handler1, Now);

        Should.Throw<DomainRuleViolationException>(() => claim.RemoveParty(second.Id, Handler1, Now));
    }

    [Fact]
    public void Removing_an_unknown_or_already_removed_party_fails()
    {
        var claim = NewDraft();
        claim.AddParty(Claimant(), Handler1, Now);
        var witness = claim.AddParty(Witness(), Handler1, Now);
        claim.RemoveParty(witness.Id, Handler1, Now);

        Should.Throw<DomainNotFoundException>(() => claim.RemoveParty(Guid.NewGuid(), Handler1, Now));
        Should.Throw<DomainRuleViolationException>(() => claim.RemoveParty(witness.Id, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("PARTY_ALREADY_REMOVED");
    }

    // ------------------------------------------------------------------ risk objects

    [Fact]
    public void First_risk_object_becomes_primary()
    {
        var claim = NewDraft();

        var first = claim.AddRiskObject(Vehicle(), Handler1, Now);
        var second = claim.AddRiskObject(Vehicle(), Handler1, Now);

        first.IsPrimary.ShouldBeTrue();
        second.IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public void Adding_a_primary_risk_object_demotes_the_current_primary()
    {
        var claim = NewDraft();
        var first = claim.AddRiskObject(Vehicle(), Handler1, Now);

        var second = claim.AddRiskObject(Vehicle(isPrimary: true), Handler1, Now);

        first.IsPrimary.ShouldBeFalse();
        second.IsPrimary.ShouldBeTrue();
        claim.RiskObjects.Count(r => r.IsPrimary).ShouldBe(1);
    }

    [Fact]
    public void Removing_the_primary_risk_object_promotes_another()
    {
        var claim = NewDraft();
        var first = claim.AddRiskObject(Vehicle(), Handler1, Now);
        var second = claim.AddRiskObject(Vehicle(), Handler1, Now);

        claim.RemoveRiskObject(first.Id, Handler1, Now);

        second.IsPrimary.ShouldBeTrue();
        claim.RiskObjects.ShouldHaveSingleItem();
    }

    [Fact]
    public void Risk_object_requires_an_asset_description() =>
        Should.Throw<DomainRuleViolationException>(() =>
                NewDraft().AddRiskObject(new RiskObjectDetails(AssetType.Property, " "), Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("ASSET_DESCRIPTION_REQUIRED");

    // ------------------------------------------------------------------ policy, handler, notes

    [Fact]
    public void Linking_a_policy_denormalises_it_and_raises_an_event_once()
    {
        var claim = NewDraftWithoutPolicy();
        claim.ClearDomainEvents();

        claim.LinkPolicy(MeridianPolicy, Handler1, Now);
        claim.LinkPolicy(MeridianPolicy, Handler1, Now);

        claim.PolicyId.ShouldBe(MeridianPolicy.PolicyId);
        claim.ClientName.ShouldBe("Meridian Transport LLC");
        var linked = claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimPolicyLinked>();
        linked.PreviousPolicyId.ShouldBeNull();
    }

    [Fact]
    public void D_10_only_supervisor_or_manager_can_assign_the_handler()
    {
        var claim = NewDraft();

        Should.Throw<DomainForbiddenException>(() => claim.AssignHandler(Handler2.UserId, Handler2.DisplayName, Handler1, Now))
            .Code.ShouldBe("CLAIM_ASSIGN_ROLE");

        claim.AssignHandler(Handler2.UserId, Handler2.DisplayName, Supervisor, Now);

        claim.AssignedHandlerId.ShouldBe(Handler2.UserId);
        var assigned = claim.DomainEvents.OfType<ClaimHandlerAssigned>().ShouldHaveSingleItem();
        assigned.PreviousHandlerId.ShouldBe(Handler1.UserId);
    }

    [Fact]
    public void Notes_are_trimmed_and_blank_notes_clear_them()
    {
        var claim = NewDraft();

        claim.UpdateNotes("  Called claimant, awaiting photos.  ", Handler1, Now);
        claim.Notes.ShouldBe("Called claimant, awaiting photos.");

        claim.UpdateNotes("   ", Handler1, Now);
        claim.Notes.ShouldBeNull();
    }

    // ------------------------------------------------------------------ reserves preconditions

    [Fact]
    public void BR_C_06_reserves_are_blocked_until_a_policy_is_linked()
    {
        var claim = NewDraftWithoutPolicy();

        var ex = Should.Throw<DomainRuleViolationException>(claim.EnsureReserveChangesAllowed);

        var violation = ex.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe("RESERVES_REQUIRE_POLICY");
        violation.Field.ShouldBe("PolicyId");

        claim.LinkPolicy(MeridianPolicy, Handler1, Now);
        Should.NotThrow(claim.EnsureReserveChangesAllowed);
    }

    [Fact]
    public void Only_a_manager_can_set_the_reserve_limit_override()
    {
        var claim = NewDraft();

        Should.Throw<DomainForbiddenException>(() => claim.SetReserveLimitOverride(true, Supervisor, Now));

        claim.SetReserveLimitOverride(true, Manager, Now);

        claim.ReserveLimitOverride.ShouldBeTrue();
        claim.ReserveLimitOverrideSetByUserId.ShouldBe(Manager.UserId);
        claim.DomainEvents.OfType<ReserveLimitOverrideChanged>().ShouldHaveSingleItem().Enabled.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ SLA (S12.2, D-13)

    [Fact]
    public void Sla_breach_is_recorded_after_48h_of_inactivity_without_changing_status()
    {
        var claim = NewDraft();

        claim.TryRecordSlaBreach(Now.AddHours(-49), Now).ShouldBeTrue();

        claim.Status.ShouldBe(ClaimStatus.Draft);
        claim.IsSlaBreached.ShouldBeTrue();
        claim.LastSlaBreachAt.ShouldBe(Now);
        claim.DomainEvents.OfType<ClaimSlaBreachDetected>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Sla_breach_is_not_recorded_before_48h()
    {
        var claim = NewDraft();

        claim.TryRecordSlaBreach(Now.AddHours(-47), Now).ShouldBeFalse();
        claim.IsSlaBreached.ShouldBeFalse();
    }

    [Fact]
    public void Sla_breach_is_recorded_at_most_once_per_24h()
    {
        var claim = NewDraft();
        var lastActivity = Now.AddDays(-5);

        claim.TryRecordSlaBreach(lastActivity, Now).ShouldBeTrue();
        claim.TryRecordSlaBreach(lastActivity, Now.AddHours(23)).ShouldBeFalse();
        claim.TryRecordSlaBreach(lastActivity, Now.AddHours(24)).ShouldBeTrue();

        claim.DomainEvents.OfType<ClaimSlaBreachDetected>().Count().ShouldBe(2);
    }

    [Fact]
    public void Any_business_update_clears_the_sla_breach_flag()
    {
        var claim = NewDraft();
        claim.TryRecordSlaBreach(Now.AddDays(-3), Now);

        claim.UpdateNotes("Followed up with claimant.", Handler1, Now);

        claim.IsSlaBreached.ShouldBeFalse();
        claim.LastSlaBreachAt.ShouldBe(Now);
    }
}
