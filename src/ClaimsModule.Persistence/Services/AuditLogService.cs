using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Domain.Common;
using ClaimsModule.Persistence.Entities;

namespace ClaimsModule.Persistence.Services;

/// <summary>
/// The only writer of ClaimAuditLog (S14.2). Entries are added to the current Unit of Work, so they
/// commit atomically with the change they describe. There is no update or delete API (BR-A-01).
/// Every entry is stamped with the tenant, the request CorrelationId, the actor and a UTC timestamp.
/// </summary>
internal sealed class AuditLogService(
    ClaimsDbContext context,
    ITenantProvider tenantProvider,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IAuditLogService
{
    private const int MaxEventTypeLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.EventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Description);
        if (entry.EventType.Length > MaxEventTypeLength)
        {
            throw new ArgumentException($"Event type must be at most {MaxEventTypeLength} characters.", nameof(entry));
        }

        context.AuditLog.Add(new ClaimAuditLog
        {
            AuditLogId = SequentialGuid.NewGuid(),
            OrganisationId = tenantProvider.OrganisationId,
            ClaimId = entry.ClaimId,
            EventType = entry.EventType,
            Description = entry.Description,
            OldValue = Serialize(entry.OldValue),
            NewValue = Serialize(entry.NewValue),
            RelatedEntityId = entry.RelatedEntityId,
            RelatedEntityType = entry.RelatedEntityType,
            CorrelationId = correlationContext.CorrelationId,
            CreatedAt = clock.GetUtcNow(),
            CreatedByUserId = entry.ActorUserId ?? currentUser.UserId,
        });
    }

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
}
