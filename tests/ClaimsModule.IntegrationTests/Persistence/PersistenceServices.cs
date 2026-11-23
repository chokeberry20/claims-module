using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Common;
using ClaimsModule.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>Records both dispatch phases. Optional hooks act as before-commit handlers.</summary>
internal sealed class RecordingDispatcher : IDomainEventDispatcher
{
    public List<(string Phase, IDomainEvent Event)> Dispatched { get; } = [];

    public Func<IReadOnlyCollection<IDomainEvent>, Task>? BeforeCommit { get; set; }

    public Func<IReadOnlyCollection<IDomainEvent>, Task>? AfterCommit { get; set; }

    public async Task DispatchBeforeCommitAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        Dispatched.AddRange(domainEvents.Select(e => ("before", e)));
        if (BeforeCommit is not null)
        {
            await BeforeCommit(domainEvents);
        }
    }

    public async Task DispatchAfterCommitAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        Dispatched.AddRange(domainEvents.Select(e => ("after", e)));
        if (AfterCommit is not null)
        {
            await AfterCommit(domainEvents);
        }
    }
}

/// <summary>A DI container wired with the real AddPersistence() and test doubles for the host-provided services.</summary>
internal sealed class PersistenceServices : IAsyncDisposable
{
    private readonly ServiceProvider _root;

    public PersistenceServices(SqlServerDatabaseFixture db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(User);
        services.AddSingleton<ITenantProvider>(Tenant);
        services.AddSingleton<ICorrelationContext>(Correlation);
        services.AddSingleton<IDomainEventDispatcher>(Dispatcher);
        services.AddPersistence(db.ConnectionString);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));

    public TestCurrentUser User { get; } = new() { Actor = PersistenceHarness.Handler };

    public TestTenantProvider Tenant { get; } = new(SqlServerDatabaseFixture.OrganisationId);

    public TestCorrelationContext Correlation { get; } = new();

    public RecordingDispatcher Dispatcher { get; } = new();

    public Actor Actor => User.GetActor();

    /// <summary>One request or unit of work.</summary>
    public AsyncServiceScope Scope() => _root.CreateAsyncScope();

    public ValueTask DisposeAsync() => _root.DisposeAsync();
}
