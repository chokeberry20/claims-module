using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.ReferenceData;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.IntegrationTests.Persistence;
using ClaimsModule.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Task 19: reference and policy queries through the full MediatR pipeline on SQL Server.</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class ReferenceDataQueryTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Cause_of_loss_codes_hide_inactive_codes_unless_asked()
    {
        var active = await _host.SendAsync(new GetCauseOfLossCodesQuery());
        var all = await _host.SendAsync(new GetCauseOfLossCodesQuery(IncludeInactive: true));

        active.Count.ShouldBe(10);
        active.ShouldAllBe(c => c.IsActive);
        all.Count.ShouldBe(11);
        all.Single(c => !c.IsActive).Code.ShouldBe("COL-LEGACY-SUBS");
        active.Select(c => c.SortOrder).ShouldBeInOrder();
        active.Single(c => c.Code == "COL-VEH-COL").PerilCategory.ShouldBe(PerilCategory.Auto);
    }

    [Fact]
    public async Task Status_transitions_come_from_the_table_and_match_the_domain_workflow()
    {
        var transitions = await _host.SendAsync(new GetClaimStatusTransitionsQuery());

        transitions.Select(t => (t.FromStatus, t.ToStatus, t.RequiredRole, t.RequiresReason))
            .OrderBy(t => t.FromStatus).ThenBy(t => t.ToStatus)
            .ShouldBe(ClaimWorkflow.Transitions.Select(r => (r.From, r.To, r.RequiredRole, r.RequiresReason))
                .OrderBy(t => t.From).ThenBy(t => t.To));
    }

    [Theory]
    [InlineData("POL-2024-001001", 1)]
    [InlineData("pol-2024", 2)] // case-insensitive, partial number
    [InlineData("meridian", 1)] // client name
    [InlineData("no such policy", 0)]
    public async Task Policy_search_matches_number_or_client_name(string term, int expected)
    {
        var results = await _host.SendAsync(new SearchPoliciesQuery(term));

        results.Count.ShouldBe(expected);
    }

    [Fact]
    public async Task Policy_search_respects_the_limit_and_validates_it()
    {
        (await _host.SendAsync(new SearchPoliciesQuery(null, Limit: 3))).Count.ShouldBe(3);

        var error = await Should.ThrowAsync<RequestValidationException>(() => _host.SendAsync(new SearchPoliciesQuery("x", Limit: 0)));
        error.Violations.Single().Code.ShouldBe("LIMIT_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Projected_policy_status_matches_the_domain_rule_for_every_seeded_policy()
    {
        var today = DateOnly.FromDateTime(_host.Clock.Now.UtcDateTime);
        var results = await _host.SendAsync(new SearchPoliciesQuery(null, Limit: SearchPoliciesQuery.MaxLimit));
        var policies = await _host.InScopeAsync(async sp => await sp.GetRequiredService<ClaimsDbContext>().Policies.AsNoTracking().ToListAsync());

        results.Count.ShouldBe(policies.Count);
        foreach (var policy in policies)
        {
            results.Single(r => r.PolicyId == policy.Id).Status.ShouldBe(policy.StatusOn(today), policy.PolicyNumber);
        }

        results.ShouldContain(r => r.Status == PolicyStatus.Expired); // POL-2024-001002 expired 2026-05-31 (D-34)
        results.ShouldContain(r => r.Status == PolicyStatus.Active);
    }

    [Fact]
    public async Task Coverage_returns_the_policy_coverage_types_or_404()
    {
        var policy = (await _host.SendAsync(new SearchPoliciesQuery("POL-2024-001001"))).Single();

        var coverage = await _host.SendAsync(new GetPolicyCoverageQuery(policy.PolicyId));

        coverage.PolicyNumber.ShouldBe("POL-2024-001001");
        coverage.CoverageTypes.ShouldNotBeEmpty();
        coverage.Status.ShouldBe(policy.Status);
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new GetPolicyCoverageQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task Queries_require_an_authenticated_user()
    {
        await Should.ThrowAsync<UnauthenticatedException>(() => _host.SendAnonymousAsync(new GetCauseOfLossCodesQuery()));
    }
}
