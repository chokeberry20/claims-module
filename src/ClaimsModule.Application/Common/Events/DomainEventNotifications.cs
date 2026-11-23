using System.Collections.Concurrent;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Common;
using MediatR;

namespace ClaimsModule.Application.Common.Events;

/// <summary>
/// A domain event published inside the open transaction, before commit. Handlers write audit
/// entries (S14.2) that commit atomically with the change. Handle it with
/// <c>INotificationHandler&lt;BeforeCommit&lt;TEvent&gt;&gt;</c>.
/// </summary>
public sealed record BeforeCommit<TEvent>(TEvent Event) : INotification
    where TEvent : IDomainEvent;

/// <summary>
/// A domain event published after a successful commit (Hangfire enqueueing). A failure here is
/// logged by the Unit of Work and never rolls back committed data; the stale-posting sweep
/// recovers a lost GL enqueue (D-56, task 27a).
/// </summary>
public sealed record AfterCommit<TEvent>(TEvent Event) : INotification
    where TEvent : IDomainEvent;

/// <summary>
/// Publishes domain events through MediatR in the two Unit of Work phases (PLAN §4). Each event is
/// wrapped in <see cref="BeforeCommit{TEvent}"/> or <see cref="AfterCommit{TEvent}"/> of its
/// runtime type, so handlers are plain, strongly typed MediatR notification handlers.
/// </summary>
internal sealed class MediatorDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<(Type Wrapper, Type Event), Func<IDomainEvent, INotification>> Factories = new();

    public async Task DispatchBeforeCommitAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await publisher.Publish((object)Wrap(typeof(BeforeCommit<>), domainEvent), cancellationToken);
        }
    }

    public async Task DispatchAfterCommitAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await publisher.Publish((object)Wrap(typeof(AfterCommit<>), domainEvent), cancellationToken);
        }
    }

    private static INotification Wrap(Type wrapper, IDomainEvent domainEvent) =>
        Factories.GetOrAdd((wrapper, domainEvent.GetType()), static key =>
        {
            var closed = key.Wrapper.MakeGenericType(key.Event);
            return e => (INotification)Activator.CreateInstance(closed, e)!;
        })(domainEvent);
}
