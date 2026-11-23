using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>The IdempotencyRecords store (D-66) against SQL Server.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class IdempotencyStoreTests(SqlServerDatabaseFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static StoredIdempotentResponse Response(Guid userId, string key, DateTimeOffset? expires = null) =>
        new(userId, key, "Claims.CreateClaimCommand", new string('A', 64), """{"claimId":"x"}""", Now, expires ?? Now.AddHours(24));

    private static async Task SaveAsync(PersistenceServices services, StoredIdempotentResponse response)
    {
        await using var scope = services.Scope();
        scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().Add(response);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<StoredIdempotentResponse?> FindAsync(PersistenceServices services, Guid userId, string key, DateTimeOffset now)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().FindAsync(userId, key, now, default);
    }

    [Fact]
    public async Task Stored_response_is_found_for_the_same_user_and_key_until_it_expires()
    {
        await using var services = new PersistenceServices(db);
        var userId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        await SaveAsync(services, Response(userId, key));

        var found = await FindAsync(services, userId, key, Now.AddHours(1));
        found.ShouldNotBeNull();
        found.RequestType.ShouldBe("Claims.CreateClaimCommand");
        found.ResponseJson.ShouldBe("""{"claimId":"x"}""");

        (await FindAsync(services, Guid.NewGuid(), key, Now.AddHours(1))).ShouldBeNull(); // another user
        (await FindAsync(services, userId, key, Now.AddHours(25))).ShouldBeNull(); // expired
    }

    [Fact]
    public async Task A_concurrent_duplicate_key_becomes_an_idempotency_conflict()
    {
        await using var services = new PersistenceServices(db);
        var userId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        await SaveAsync(services, Response(userId, key));

        await Should.ThrowAsync<IdempotencyConflictException>(() => SaveAsync(services, Response(userId, key)));
    }
}
