namespace ClaimsModule.Domain.Common;

/// <summary>Something significant that happened in the domain. Free of any messaging framework.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>Base record for domain events. <paramref name="OccurredAt"/> is supplied by the caller's clock.</summary>
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; init; } = SequentialGuid.NewGuid();
}
