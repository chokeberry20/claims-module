using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// The retrying execution strategy (P2.5 task 17f, D-56). AddPersistence enables
/// EnableRetryOnFailure, so every other persistence test already runs under it; these tests pin the
/// registration and the replay semantics of IUnitOfWork.ExecuteInTransactionAsync. Real transient
/// SQL errors can't be produced on demand, so the replay tests swap in a strategy that treats
/// <see cref="SimulatedTransientException"/> as transient.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ExecutionStrategyTests(SqlServerDatabaseFixture db)
{
    [Fact]
    public async Task AddPersistence_enables_the_sql_server_retrying_strategy()
    {
        await using var services = new PersistenceServices(db);
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        context.Database.CreateExecutionStrategy().ShouldBeOfType<SqlServerRetryingExecutionStrategy>();
    }

    [Fact]
    public async Task A_raw_user_transaction_is_rejected_under_the_retrying_strategy()
    {
        // Why ExecuteInTransactionAsync wraps the strategy: without it, this is what happens.
        await using var services = new PersistenceServices(db);
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        await using var transaction = await context.Database.BeginTransactionAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => context.Policies.CountAsync());

        error.Message.ShouldContain("does not support user-initiated transactions");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_commits_under_the_retrying_strategy()
    {
        await using var services = new PersistenceServices(db);
        var claim = new PersistenceHarness(db).NewClaim();

        await using (var scope = services.Scope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(claim);
                return await uow.SaveChangesAsync(ct);
            });
        }

        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{claim.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Transient_failure_after_save_replays_the_whole_unit_and_keeps_numbers_gapless()
    {
        const int year = 2303;
        await using var services = new PersistenceServices(db, sql => sql.ExecutionStrategy(d => new SimulatedTransientRetryStrategy(d)));
        var created = new List<Claim>();

        await using (var scope = services.Scope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                var number = await scope.ServiceProvider.GetRequiredService<IClaimNumberGenerator>().NextAsync(year, ct);
                var claim = NewClaim(services, number);
                created.Add(claim);
                scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(claim);
                await uow.SaveChangesAsync(ct);
                if (created.Count == 1)
                {
                    throw new SimulatedTransientException(); // after the save, before the commit
                }
                return 0;
            });
        }

        created.Count.ShouldBe(2);
        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{created[0].Id}'")).ShouldBe(0);
        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{created[1].Id}'")).ShouldBe(1);
        // The first attempt's number allocation was rolled back with it: the retry gets sequence 1 again.
        created[1].ClaimNumber.Sequence.ShouldBe(1);

        // Only the committed attempt's events reach the after-commit phase (job enqueueing).
        var afterCommit = services.Dispatcher.Dispatched.Where(d => d.Phase == "after").Select(d => d.Event).OfType<ClaimCreated>().ToList();
        afterCommit.Select(e => e.ClaimId).ShouldBe([created[1].Id]);
    }

    [Fact]
    public async Task Transient_failure_before_save_does_not_carry_tracked_entities_into_the_retry()
    {
        await using var services = new PersistenceServices(db, sql => sql.ExecutionStrategy(d => new SimulatedTransientRetryStrategy(d)));
        var created = new List<Claim>();

        await using (var scope = services.Scope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                var claim = new PersistenceHarness(db).NewClaim();
                created.Add(claim);
                scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(claim);
                if (created.Count == 1)
                {
                    throw new SimulatedTransientException(); // tracked as Added, never saved
                }
                return await uow.SaveChangesAsync(ct);
            });
        }

        // Without clearing the change tracker, the retry's SaveChanges would also insert the first claim.
        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{created[0].Id}'")).ShouldBe(0);
        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{created[1].Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Non_transient_failure_is_not_retried()
    {
        await using var services = new PersistenceServices(db, sql => sql.ExecutionStrategy(d => new SimulatedTransientRetryStrategy(d)));
        var attempts = 0;

        await using var scope = services.Scope();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("business failure");
            }));

        attempts.ShouldBe(1);
    }

    private static Claim NewClaim(PersistenceServices services, ClaimNumber number) =>
        Claim.Create(number, new LossEventDetails(services.Clock.Now.AddDays(-1), "Burst pipe flooded the ground floor office.", "COL-FLOOD"),
            "Property", null, ClaimSeverity.Standard, null, services.Actor, services.Clock.Now);
}

internal sealed class SimulatedTransientException() : Exception("Simulated transient database failure.");

/// <summary>A retrying strategy that treats only <see cref="SimulatedTransientException"/> as transient, with no delay.</summary>
internal sealed class SimulatedTransientRetryStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => exception is SimulatedTransientException;
}
