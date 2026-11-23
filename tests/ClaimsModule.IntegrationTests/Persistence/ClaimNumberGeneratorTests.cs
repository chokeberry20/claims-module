using ClaimsModule.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// S5.3 / BR-C-04 / D-14: claim numbers are atomic under concurrency, gapless across rollbacks, and
/// per organisation and year. Each test uses its own year, because the database is shared within a run.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ClaimNumberGeneratorTests(SqlServerDatabaseFixture db)
{
    private async Task<int> AllocateAsync(int year, bool commit = true, Guid? organisationId = null)
    {
        var tenant = new TestTenantProvider(organisationId ?? SqlServerDatabaseFixture.OrganisationId);
        await using var context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var number = await new ClaimNumberGenerator(context, tenant).NextAsync(year);

        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        return number.Sequence;
    }

    [Fact]
    public async Task Numbers_start_at_one_per_year_and_increase_by_one()
    {
        (await AllocateAsync(2101)).ShouldBe(1);
        (await AllocateAsync(2101)).ShouldBe(2);
        (await AllocateAsync(2102)).ShouldBe(1);
        (await AllocateAsync(2101)).ShouldBe(3);
    }

    [Fact]
    public async Task Formatted_number_follows_the_spec()
    {
        var tenant = new TestTenantProvider(SqlServerDatabaseFixture.OrganisationId);
        await using var context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var number = await new ClaimNumberGenerator(context, tenant).NextAsync(2103);

        number.Value.ShouldBe("CLM-2103-0000001");
    }

    [Fact]
    public async Task Fifty_concurrent_submissions_get_distinct_consecutive_numbers()
    {
        var numbers = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => AllocateAsync(2104))));

        numbers.Order().ShouldBe(Enumerable.Range(1, 50));
    }

    /// <summary>
    /// Targets the race that HOLDLOCK prevents: several transactions allocating the first number of a
    /// new year at the same instant. Without the key-range lock, both see NOT MATCHED and try to
    /// INSERT, which ends in a PK violation (or duplicate numbers). An async gate releases all
    /// allocators together, after each has opened its transaction.
    /// There are 80 allocators: each holds a pooled connection while it waits, so the total must stay
    /// below SqlClient's default Max Pool Size of 100.
    /// </summary>
    [Fact]
    public async Task Simultaneous_first_allocations_of_a_new_year_do_not_collide()
    {
        const int years = 10;
        const int allocatorsPerYear = 8;
        const int allocators = years * allocatorsPerYear;
        var tenant = new TestTenantProvider(SqlServerDatabaseFixture.OrganisationId);
        var ready = 0;
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, allocators).Select(async i =>
        {
            var year = 2201 + (i % years);
            await using var context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);
            await using var transaction = await context.Database.BeginTransactionAsync();
            if (Interlocked.Increment(ref ready) == allocators)
            {
                allReady.SetResult();
            }

            await gate.Task;
            var number = await new ClaimNumberGenerator(context, tenant).NextAsync(year);
            await transaction.CommitAsync();
            return (Year: year, number.Sequence);
        }).ToList();

        await allReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
        gate.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));

        foreach (var perYear in results.GroupBy(r => r.Year))
        {
            perYear.Select(r => r.Sequence).Order().ShouldBe(Enumerable.Range(1, allocatorsPerYear), $"year {perYear.Key}");
        }
    }

    [Fact]
    public async Task Rolled_back_allocation_leaves_no_gap()
    {
        (await AllocateAsync(2105)).ShouldBe(1);
        (await AllocateAsync(2105, commit: false)).ShouldBe(2);

        (await AllocateAsync(2105)).ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_mix_of_commits_and_rollbacks_stays_gapless()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            var commit = i % 3 != 0;
            return (Commit: commit, Number: await AllocateAsync(2106, commit));
        })));

        var committed = results.Count(r => r.Commit);
        (await db.ScalarAsync<int>($"SELECT LastValue FROM ClaimNumberSequences WHERE [Year] = 2106 AND OrganisationId = '{SqlServerDatabaseFixture.OrganisationId}'"))
            .ShouldBe(committed);
        results.Where(r => r.Commit).Select(r => r.Number).Order().ShouldBe(Enumerable.Range(1, committed));
    }

    [Fact]
    public async Task Counters_are_per_organisation() =>
        (await AllocateAsync(2107, organisationId: Guid.NewGuid())).ShouldBe(1);

    [Fact]
    public async Task Allocation_outside_a_transaction_is_refused()
    {
        var tenant = new TestTenantProvider(SqlServerDatabaseFixture.OrganisationId);
        await using var context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);

        await Should.ThrowAsync<InvalidOperationException>(() => new ClaimNumberGenerator(context, tenant).NextAsync(2108));
    }
}
