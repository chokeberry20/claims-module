using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Services;

/// <summary>
/// IdempotencyRecords at command level (D-66): RequestMethod holds <see cref="CommandMethod"/>,
/// RequestPath the command type, ResponseBody the command result as JSON. The API turns a replayed
/// result into the same HTTP response as the first call.
/// </summary>
internal sealed class IdempotencyStore(ClaimsDbContext context, ITenantProvider tenant) : IIdempotencyStore
{
    internal const string CommandMethod = "COMMAND";

    public async Task<StoredIdempotentResponse?> FindAsync(Guid userId, string idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords
            .AsNoTracking()
            .Where(r => r.OrganisationId == tenant.OrganisationId && r.UserId == userId && r.IdempotencyKey == idempotencyKey && r.ExpiresAt > now)
            .SingleOrDefaultAsync(cancellationToken);

        return record is null
            ? null
            : new StoredIdempotentResponse(
                record.UserId, record.IdempotencyKey, record.RequestPath, record.RequestHash,
                record.ResponseBody ?? "null", record.CreatedAt, record.ExpiresAt);
    }

    public void Add(StoredIdempotentResponse response) =>
        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            IdempotencyRecordId = Domain.Common.SequentialGuid.NewGuid(),
            OrganisationId = tenant.OrganisationId,
            UserId = response.UserId,
            IdempotencyKey = response.IdempotencyKey,
            RequestMethod = CommandMethod,
            RequestPath = response.RequestType,
            RequestHash = response.RequestHash,
            ResponseStatusCode = 200,
            ResponseBody = response.ResponseJson,
            ResponseContentType = "application/json",
            CreatedAt = response.CreatedAt,
            ExpiresAt = response.ExpiresAt,
        });
}
