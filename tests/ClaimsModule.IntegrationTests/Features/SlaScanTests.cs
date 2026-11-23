using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>
/// Task 27: the SLA scan (S12.2, D-13) with a fake clock. The scan sees the whole shared test
/// database, so assertions look only at this test's own claim.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class SlaScanTests(SqlServerDatabaseFixture db) : IAsyncLifetime
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
    public async Task Sla_breach_after_48_hours_is_flagged_once_per_24_hours_without_changing_status()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));
        var start = Now;
        Task<ClaimDetailDto> Detail() => _host.SendAsync(new GetClaimQuery(created.ClaimId));

        _host.Clock.Now = start.AddHours(47);
        await _host.SendAsync(new RunSlaScanCommand());
        (await Detail()).IsSlaBreached.ShouldBeFalse();

        _host.Clock.Now = start.AddHours(49);
        await _host.SendAsync(new RunSlaScanCommand());
        var flagged = await Detail();
        flagged.IsSlaBreached.ShouldBeTrue();
        flagged.LastSlaBreachAt.ShouldBe(start.AddHours(49));
        flagged.Status.ShouldBe(ClaimStatus.Draft);

        _host.Clock.Now = start.AddHours(60); // within 24 h of the last flag
        await _host.SendAsync(new RunSlaScanCommand());
        (await Detail()).LastSlaBreachAt.ShouldBe(start.AddHours(49));

        _host.Clock.Now = start.AddHours(74); // 25 h later
        await _host.SendAsync(new RunSlaScanCommand());
        (await Detail()).LastSlaBreachAt.ShouldBe(start.AddHours(74));
    }

    [Fact]
    public async Task A_real_update_clears_the_flag_and_restarts_the_48_hours()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));
        var start = Now;
        _host.Clock.Now = start.AddHours(49);
        await _host.SendAsync(new RunSlaScanCommand());

        await _host.SendAsync(new UpdateNotesCommand(created.ClaimId, "Followed up with the insured."));
        (await _host.SendAsync(new GetClaimQuery(created.ClaimId))).IsSlaBreached.ShouldBeFalse();

        _host.Clock.Now = start.AddHours(49 + 47);
        await _host.SendAsync(new RunSlaScanCommand());
        (await _host.SendAsync(new GetClaimQuery(created.ClaimId))).IsSlaBreached.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_draft_and_open_claims_are_monitored()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));
        await _host.SendAsync(new TransitionClaimStatusCommand(created.ClaimId, ClaimStatus.Open));
        await _host.SendAsync(new TransitionClaimStatusCommand(created.ClaimId, ClaimStatus.UnderInvestigation));

        _host.Clock.Now += TimeSpan.FromHours(100);
        await _host.SendAsync(new RunSlaScanCommand());

        (await _host.SendAsync(new GetClaimQuery(created.ClaimId))).IsSlaBreached.ShouldBeFalse();
    }
}
