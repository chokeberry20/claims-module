namespace ClaimsModule.Application.Abstractions.Persistence;

/// <summary>A stored command result for an Idempotency-Key (S10), scoped to organisation and user.</summary>
public sealed record StoredIdempotentResponse(
    Guid UserId,
    string IdempotencyKey,
    string RequestType,
    string RequestHash,
    string ResponseJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The IdempotencyRecords table (D-66). <see cref="Add"/> joins the current Unit of Work, so the
/// record commits atomically with the command's changes. A concurrent duplicate fails on the
/// unique (organisation, user, key) index and surfaces as IdempotencyConflictException.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>The unexpired record for this user and key, or null.</summary>
    Task<StoredIdempotentResponse?> FindAsync(Guid userId, string idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(StoredIdempotentResponse response);
}
