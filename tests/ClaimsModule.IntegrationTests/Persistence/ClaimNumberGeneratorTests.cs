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

    // ------------------------------------------------------------------ deterministic lock tests (P2 review)

    /// <summary>
    /// tx1 allocates a number and keeps its transaction open. tx2 is set to SET LOCK_TIMEOUT 500.
    /// <list type="bullet">
    /// <item><see cref="Same_year_allocation_waits_while_another_transaction_holds_the_counter"/>: tx2 allocates in the same year and must time out
    /// (error 1222), because allocations for one org and year are serialised until commit. This is
    /// the test the reviewer specified.</item>
    /// <item><see cref="HOLDLOCK_range_lock_blocks_first_allocation_of_an_adjacent_new_year"/>: tx2 allocates the first number of a different,
    /// adjacent year that doesn't exist yet. Only a HOLDLOCK key-range lock taken by tx1's first-insert
    /// MERGE can block that, so this is the deterministic signal that HOLDLOCK is in effect.</item>
    /// </list>
    /// </summary>
    private async Task<int?> Tx2AllocateWhileTx1HoldsAsync(Guid organisationId, int tx1Year, int tx2Year)
    {
        var tenant = new TestTenantProvider(organisationId);
        await using var tx1Context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);
        await using var tx1 = await tx1Context.Database.BeginTransactionAsync();
        await new ClaimNumberGenerator(tx1Context, tenant).NextAsync(tx1Year);

        await using var tx2Context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);
        await using var tx2 = await tx2Context.Database.BeginTransactionAsync();
        await tx2Context.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 500;");
        try
        {
            var number = await new ClaimNumberGenerator(tx2Context, tenant).NextAsync(tx2Year);
            return number.Sequence;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1222)
        {
            return null; // lock request time out period exceeded
        }
        finally
        {
            await tx1.RollbackAsync();
        }
    }

    [Fact]
    public async Task Same_year_allocation_waits_while_another_transaction_holds_the_counter() =>
        (await Tx2AllocateWhileTx1HoldsAsync(Guid.NewGuid(), 2401, 2401)).ShouldBeNull("tx2 must wait for tx1's open allocation");

    [Fact]
    public async Task HOLDLOCK_range_lock_blocks_first_allocation_of_an_adjacent_new_year() =>
        (await Tx2AllocateWhileTx1HoldsAsync(Guid.NewGuid(), 2402, 2403)).ShouldBeNull(
            "tx1's HOLDLOCK key-range lock must cover the empty key range, so a first insert there waits");

    [Fact]
    public async Task First_allocation_of_a_year_holds_key_range_locks()
    {
        var tenant = new TestTenantProvider(Guid.NewGuid());
        await using var context = new ClaimsModule.Persistence.ClaimsDbContext(db.Options(), tenant);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await new ClaimNumberGenerator(context, tenant).NextAsync(2404);

        var modes = await context.Database.SqlQueryRaw<string>(
                "SELECT request_mode AS [Value] FROM sys.dm_tran_locks WHERE request_session_id = @@SPID AND resource_type = 'KEY'")
            .ToListAsync();

        modes.ShouldContain(m => m.StartsWith("Range", StringComparison.Ordinal), $"KEY lock modes held: {string.Join(", ", modes)}");
        await transaction.RollbackAsync();
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
