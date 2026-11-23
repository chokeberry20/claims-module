using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>
/// Task 21: claim list and detail queries. Each test creates its claims as a unique handler and
/// filters by that handler, so data from other test classes in the shared database doesn't interfere.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class ClaimQueryTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;
    private Actor _handler = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        _handler = new Actor(Guid.NewGuid(), $"Query Handler {Guid.NewGuid():N}", UserRole.Handler);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<CreateClaimResult> CreateAsync(CreateClaimCommand command, TimeSpan? advance = null)
    {
        _host.Clock.Now += advance ?? TimeSpan.FromMinutes(1);
        return await _host.SendAsync(command, _handler);
    }

    private Task<PagedResult<ClaimSummaryDto>> MineAsync(ListClaimsQuery query) =>
        _host.SendAsync(query with { AssignedHandlerId = _handler.UserId }, _handler);

    [Fact]
    public async Task List_pages_newest_first_with_total_count()
    {
        var created = new List<CreateClaimResult>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await CreateAsync(Fnol.Complete(_host.Clock.Now)));
        }

        var page1 = await MineAsync(new ListClaimsQuery { PageSize = 2 });
        var page3 = await MineAsync(new ListClaimsQuery { PageSize = 2, Page = 3 });

        page1.TotalCount.ShouldBe(5);
        page1.Items.Select(i => i.ClaimId).ShouldBe([created[4].ClaimId, created[3].ClaimId]);
        page3.Items.Select(i => i.ClaimId).ShouldBe([created[0].ClaimId]);
        page1.Items[0].AssignedHandlerName.ShouldBe(_handler.DisplayName);
        page1.Items[0].PrimaryClaimantName.ShouldBe("Jane Doe");
        page1.Items[0].CauseOfLossName.ShouldNotBeNullOrWhiteSpace();
        page1.Items[0].ClientName.ShouldBe("Meridian Transport LLC");
    }

    [Fact]
    public async Task List_sorts_by_claim_number_ascending_when_asked()
    {
        var a = await CreateAsync(Fnol.Complete(_host.Clock.Now));
        var b = await CreateAsync(Fnol.Complete(_host.Clock.Now));

        var result = await MineAsync(new ListClaimsQuery { SortBy = ClaimSortField.ClaimNumber, Descending = false });

        result.Items.Select(i => i.ClaimNumber).ShouldBe([a.ClaimNumber, b.ClaimNumber]);
    }

    [Fact]
    public async Task List_filters_by_status_loss_date_range_and_cause_code()
    {
        var now = _host.Clock.Now;
        var old = await CreateAsync(Fnol.Complete(now) with { LossDate = now.AddDays(-30) });
        var theft = await CreateAsync(Fnol.Complete(now) with { CauseOfLossCode = "COL-THEFT", LossDate = now.AddDays(-2) });

        (await MineAsync(new ListClaimsQuery { Statuses = [ClaimStatus.Draft] })).TotalCount.ShouldBe(2);
        (await MineAsync(new ListClaimsQuery { Statuses = [ClaimStatus.Open, ClaimStatus.Closed] })).TotalCount.ShouldBe(0);
        (await MineAsync(new ListClaimsQuery { LossDateFrom = now.AddDays(-10) })).Items.Single().ClaimId.ShouldBe(theft.ClaimId);
        (await MineAsync(new ListClaimsQuery { LossDateTo = now.AddDays(-10) })).Items.Single().ClaimId.ShouldBe(old.ClaimId);
        (await MineAsync(new ListClaimsQuery { CauseOfLossCode = "COL-THEFT" })).Items.Single().ClaimId.ShouldBe(theft.ClaimId);
    }

    [Fact]
    public async Task List_searches_claim_number_and_client_name_and_handler_text()
    {
        var mine = await CreateAsync(Fnol.Complete(_host.Clock.Now));

        (await _host.SendAsync(new ListClaimsQuery { Search = mine.ClaimNumber })).Items.Single().ClaimId.ShouldBe(mine.ClaimId);
        (await MineAsync(new ListClaimsQuery { Search = "meridian" })).Items.Single().ClaimId.ShouldBe(mine.ClaimId);
        (await MineAsync(new ListClaimsQuery { Search = "nobody-at-all" })).TotalCount.ShouldBe(0);

        // D-26: free-text handler filter (case-insensitive contains).
        var byName = await _host.SendAsync(new ListClaimsQuery { AssignedHandler = _handler.DisplayName[^12..].ToUpperInvariant() });
        byName.Items.Single().ClaimId.ShouldBe(mine.ClaimId);
    }

    [Fact]
    public async Task Search_matches_a_partial_claim_number()
    {
        // S10.1: "search (partial claim number or client name)".
        var mine = await CreateAsync(Fnol.Complete(_host.Clock.Now));
        var other = await CreateAsync(Fnol.Complete(_host.Clock.Now));
        var digits = mine.ClaimNumber[^7..]; // e.g. "0000142"

        (await MineAsync(new ListClaimsQuery { Search = digits })).Items.Select(i => i.ClaimId).ShouldBe([mine.ClaimId]);
        (await MineAsync(new ListClaimsQuery { Search = "2026-00" })).Items.Select(i => i.ClaimId)
            .ShouldBe([mine.ClaimId, other.ClaimId], ignoreOrder: true);
        (await MineAsync(new ListClaimsQuery { Search = "clm-2026" })).TotalCount.ShouldBe(2); // case-insensitive
        (await MineAsync(new ListClaimsQuery { Search = mine.ClaimNumber[4..12] })).Items.ShouldContain(i => i.ClaimId == mine.ClaimId); // middle
        (await MineAsync(new ListClaimsQuery { Search = "CLM-1999" })).TotalCount.ShouldBe(0);
        (await MineAsync(new ListClaimsQuery { Search = "%" })).TotalCount.ShouldBe(0); // LIKE wildcards are escaped
    }

    [Fact]
    public async Task Total_reserves_is_the_approved_gross_total()
    {
        var claim = await CreateAsync(Fnol.WithReserve(_host.Clock.Now, 8_000m));
        // A 50k pending transaction on another claim must not count either; here: pending on the same claim is not yet approved.
        var pending = await CreateAsync(Fnol.WithReserve(_host.Clock.Now, 50_000m));
        var recovery = await CreateAsync(Fnol.WithReserve(_host.Clock.Now, -3_000m, ReserveComponentType.SubrogationRecoverable));

        var items = (await MineAsync(new ListClaimsQuery())).Items.ToDictionary(i => i.ClaimId);

        items[claim.ClaimId].TotalReserves.ShouldBe(8_000m);
        items[pending.ClaimId].TotalReserves.ShouldBe(0m); // PendingApproval isn't approved (D-18)
        items[recovery.ClaimId].TotalReserves.ShouldBe(0m); // subrogation excluded from gross (D-19)
    }

    [Fact]
    public async Task Detail_returns_every_section_with_reserve_totals()
    {
        var created = await CreateAsync(Fnol.WithReserve(_host.Clock.Now, 50_000m) with
        {
            Parties = [Fnol.Claimant, new PartyInput(PartyRole.Witness, PartyType.Company, CompanyName: "Acme Dashcams Ltd")],
            Notes = "Insured called at 08:40.",
        });

        var detail = await _host.SendAsync(new GetClaimQuery(created.ClaimId));

        detail.ClaimNumber.ShouldBe(created.ClaimNumber);
        detail.Status.ShouldBe(ClaimStatus.Draft);
        detail.ClaimType.ShouldBe("Auto");
        detail.Notes.ShouldBe("Insured called at 08:40.");
        detail.LossEvent.CauseOfLossCode.ShouldBe("COL-VEH-COL");
        detail.LossEvent.CauseOfLossName.ShouldNotBeNullOrWhiteSpace();
        detail.LossEvent.EstimatedLossAmount.ShouldBe(12_500m);
        detail.Parties.Select(p => p.DisplayName).ShouldBe(["Jane Doe", "Acme Dashcams Ltd"]);
        detail.RiskObjects.Single().AssetType.ShouldBe(AssetType.Vehicle);
        detail.ValidationIssues.ShouldBeEmpty();
        detail.Reserves.ApprovedGross.ShouldBe(0m);
        detail.Reserves.HasPendingApproval.ShouldBeTrue();
        var component = detail.Reserves.Components.Single();
        component.ComponentType.ShouldBe(ReserveComponentType.Indemnity);
        component.PendingAmount.ShouldBe(50_000m);
        component.PendingTransactionId.ShouldBe(created.InitialReserve!.TransactionId);
    }

    [Fact]
    public async Task Detail_of_an_unknown_claim_is_404()
    {
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new GetClaimQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task Detail_of_another_organisations_claim_is_404()
    {
        var created = await CreateAsync(Fnol.Complete(_host.Clock.Now));
        _host.Tenant.OrganisationId = Guid.NewGuid();
        try
        {
            await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new GetClaimQuery(created.ClaimId)));
        }
        finally
        {
            _host.Tenant.OrganisationId = SqlServerDatabaseFixture.OrganisationId;
        }
    }

    [Theory]
    [InlineData(0, 20, "PAGE_OUT_OF_RANGE")]
    [InlineData(1, 101, "PAGE_SIZE_OUT_OF_RANGE")]
    public async Task Invalid_paging_is_rejected(int page, int pageSize, string code)
    {
        var error = await Should.ThrowAsync<RequestValidationException>(() =>
            _host.SendAsync(new ListClaimsQuery { Page = page, PageSize = pageSize }));

        error.Violations.Single().Code.ShouldBe(code);
    }
}
