using ClaimsModule.Application.Common.Events;
using ClaimsModule.Domain.Claims.Events;
using MediatR;
using NSubstitute;

namespace ClaimsModule.Application.Tests.Common;

public sealed class DomainEventDispatcherTests
{
    [Fact]
    public async Task Wraps_each_event_in_the_phase_notification_of_its_runtime_type()
    {
        var publisher = Substitute.For<IPublisher>();
        var published = new List<object>();
        publisher.Publish(Arg.Do<object>(published.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var dispatcher = new MediatorDomainEventDispatcher(publisher);
        var created = new ClaimCreated(Guid.NewGuid(), "CLM-2026-0000001", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var notes = new ClaimNotesUpdated(created.ClaimId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        await dispatcher.DispatchBeforeCommitAsync([created, notes], default);
        await dispatcher.DispatchAfterCommitAsync([created], default);

        published.Count.ShouldBe(3);
        published[0].ShouldBeOfType<BeforeCommit<ClaimCreated>>().Event.ShouldBe(created);
        published[1].ShouldBeOfType<BeforeCommit<ClaimNotesUpdated>>().Event.ShouldBe(notes);
        published[2].ShouldBeOfType<AfterCommit<ClaimCreated>>().Event.ShouldBe(created);
    }
}
