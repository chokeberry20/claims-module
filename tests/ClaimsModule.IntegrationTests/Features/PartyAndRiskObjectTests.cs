using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Task 24: party and risk-object commands, including the last-claimant rule (BR-P-01).</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class PartyAndRiskObjectTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    [Fact]
    public async Task Adding_a_claimant_resolves_the_no_claimant_issue()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with { Parties = [] });

        var result = await _host.SendAsync(new AddPartyCommand(created.ClaimId, Fnol.Claimant));

        result.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.NoClaimant).Status.ShouldBe(ValidationIssueStatus.Resolved);
        (await _host.SendAsync(new GetClaimQuery(created.ClaimId))).Parties.Single().PartyId.ShouldBe(result.ChildId);
    }

    [Fact]
    public async Task Duplicate_roles_are_allowed_and_parties_are_validated()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));

        await _host.SendAsync(new AddPartyCommand(created.ClaimId, new PartyInput(PartyRole.Claimant, PartyType.Company, CompanyName: "Doe Haulage Ltd")));
        var error = await Should.ThrowAsync<RequestValidationException>(() =>
            _host.SendAsync(new AddPartyCommand(created.ClaimId, new PartyInput(PartyRole.Witness, PartyType.Company))));

        error.Violations.Single().Code.ShouldBe("PARTY_NAME_REQUIRED");
        error.Violations.Single().Field.ShouldBe("Party.CompanyName");
        (await _host.SendAsync(new GetClaimQuery(created.ClaimId))).Parties.Count(p => p.Role == PartyRole.Claimant).ShouldBe(2);
    }

    [Fact]
    public async Task The_last_active_claimant_cannot_be_removed_but_others_can_be_soft_removed()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with
        {
            Parties = [Fnol.Claimant, new PartyInput(PartyRole.Witness, PartyType.Person, "Walt", "Witness")],
        });
        var detail = await _host.SendAsync(new GetClaimQuery(created.ClaimId));
        var claimant = detail.Parties.Single(p => p.Role == PartyRole.Claimant);
        var witness = detail.Parties.Single(p => p.Role == PartyRole.Witness);

        var error = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            _host.SendAsync(new RemovePartyCommand(created.ClaimId, claimant.PartyId)));
        error.Violations.Single().Code.ShouldBe("PARTY_LAST_CLAIMANT");
        error.Violations.Single().Message.ShouldBe("The last Claimant party cannot be removed. At least one Claimant party is required.");

        await _host.SendAsync(new RemovePartyCommand(created.ClaimId, witness.PartyId));

        var after = (await _host.SendAsync(new GetClaimQuery(created.ClaimId))).Parties.Single(p => p.PartyId == witness.PartyId);
        after.IsActive.ShouldBeFalse(); // history kept (S10.1)
        after.RemovedAt.ShouldBe(Now);
        await Should.ThrowAsync<DomainNotFoundException>(() => _host.SendAsync(new RemovePartyCommand(created.ClaimId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Risk_objects_can_be_added_and_removed_and_the_warning_follows()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with { RiskObjects = [] });
        created.ValidationIssues.Single().Code.ShouldBe(ValidationIssueCodes.NoRiskObjects);

        var added = await _host.SendAsync(new AddRiskObjectCommand(created.ClaimId, Fnol.Vehicle));
        added.ValidationIssues.Single().Status.ShouldBe(ValidationIssueStatus.Resolved);

        var removed = await _host.SendAsync(new RemoveRiskObjectCommand(created.ClaimId, added.ChildId));
        removed.ValidationIssues.ShouldContain(i => i.Code == ValidationIssueCodes.NoRiskObjects && i.Status == ValidationIssueStatus.Open);
    }
}
