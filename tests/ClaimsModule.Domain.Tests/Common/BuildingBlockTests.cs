using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Tests.Common;

public sealed class BuildingBlockTests
{
    private sealed record SomethingHappened(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

    private sealed class TestAggregate(Guid id) : AggregateRoot(id)
    {
        public void DoSomething(DateTimeOffset now) => Raise(new SomethingHappened(now));
    }

    private sealed class OtherAggregate(Guid id) : AggregateRoot(id);

    [Fact]
    public void Aggregate_collects_and_clears_domain_events()
    {
        var aggregate = new TestAggregate(SequentialGuid.NewGuid());

        aggregate.DoSomething(TestData.Now);
        aggregate.DoSomething(TestData.Now);

        aggregate.DomainEvents.Count.ShouldBe(2);
        aggregate.DomainEvents.Select(e => e.EventId).Distinct().Count().ShouldBe(2);
        aggregate.ClearDomainEvents();
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Entities_are_equal_by_type_and_id()
    {
        var id = SequentialGuid.NewGuid();

        (new TestAggregate(id) == new TestAggregate(id)).ShouldBeTrue();
        new TestAggregate(id).Equals(new OtherAggregate(id)).ShouldBeFalse();
        (new TestAggregate(id) != new TestAggregate(SequentialGuid.NewGuid())).ShouldBeTrue();
    }

    [Fact]
    public void Entity_rejects_empty_id() =>
        Should.Throw<ArgumentException>(() => new TestAggregate(Guid.Empty));

    [Theory]
    [InlineData(UserRole.Handler, UserRole.Handler, true)]
    [InlineData(UserRole.Handler, UserRole.Supervisor, false)]
    [InlineData(UserRole.Supervisor, UserRole.Handler, true)]
    [InlineData(UserRole.Supervisor, UserRole.Manager, false)]
    [InlineData(UserRole.Manager, UserRole.Supervisor, true)]
    [InlineData(UserRole.Manager, UserRole.Manager, true)]
    public void Roles_are_hierarchical(UserRole actorRole, UserRole required, bool expected) =>
        new Actor(Guid.NewGuid(), "User", actorRole).HasAtLeast(required).ShouldBe(expected);

    [Fact]
    public void Rule_violation_exception_requires_at_least_one_violation() =>
        Should.Throw<ArgumentException>(() => new DomainRuleViolationException([]));

    [Fact]
    public void Rule_violation_exception_message_joins_violation_messages()
    {
        var ex = new DomainRuleViolationException(
        [
            new RuleViolation("A", "First."),
            new RuleViolation("B", "Second.", "Field"),
        ]);

        ex.Message.ShouldBe("First. Second.");
        ex.Contains("B").ShouldBeTrue();
    }
}
