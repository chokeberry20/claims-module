using ClaimsModule.Application;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Jobs;
using ClaimsModule.Application.Abstractions.Storage;
using ClaimsModule.Infrastructure;
using ClaimsModule.Domain.Common;
using ClaimsModule.IntegrationTests.Persistence;
using ClaimsModule.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>
/// The real Application + Persistence wiring (AddApplication, AddPersistence) on the SQL Server test
/// database, with test doubles for what the API host normally supplies. Each <see cref="SendAsync{T}"/>
/// runs in a fresh DI scope, like one HTTP request, as <see cref="User"/>.
/// </summary>
internal sealed class ApplicationHost : IAsyncDisposable
{
    public static readonly Actor Handler1 = new(Guid.Parse("a1000000-0000-0000-0000-000000000001"), "Hannah Handler", UserRole.Handler);
    public static readonly Actor Handler2 = new(Guid.Parse("a1000000-0000-0000-0000-000000000002"), "Harry Handler", UserRole.Handler);
    public static readonly Actor Supervisor1 = new(Guid.Parse("a2000000-0000-0000-0000-000000000001"), "Sam Supervisor", UserRole.Supervisor);
    public static readonly Actor Supervisor2 = new(Guid.Parse("a2000000-0000-0000-0000-000000000002"), "Sally Supervisor", UserRole.Supervisor);
    public static readonly Actor Manager1 = new(Guid.Parse("a3000000-0000-0000-0000-000000000001"), "Mia Manager", UserRole.Manager);

    private readonly ServiceProvider _root;

    public ApplicationHost(SqlServerDatabaseFixture db, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(User);
        services.AddSingleton<ITenantProvider>(Tenant);
        services.AddSingleton<ICorrelationContext>(Correlation);
        services.AddPersistence(db.ConnectionString);
        services.AddApplication();
        services.Configure<StorageOptions>(o =>
        {
            o.Provider = StorageProvider.LocalFileSystem;
            o.LocalFileSystem.RootPath = StorageRoot;
            o.LocalFileSystem.PublicBaseUrl = "http://localhost:5180/uploads";
        });
        services.AddInfrastructure();
        services.AddSingleton<IUserDirectory>(new InMemoryUserDirectory([Handler1, Handler2, Supervisor1, Supervisor2, Manager1]));
        services.AddSingleton<IJobScheduler>(Jobs);
        configure?.Invoke(services);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Starts at a fixed instant; tests move it by setting <see cref="TestClock.Now"/>.</summary>
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    public TestCurrentUser User { get; } = new() { Actor = Handler1 };

    public TestTenantProvider Tenant { get; } = new(SqlServerDatabaseFixture.OrganisationId);

    public TestCorrelationContext Correlation { get; } = new();

    /// <summary>Local document storage for this host (a fresh temp folder).</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "claims-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Records every enqueued job instead of running it.</summary>
    public RecordingJobScheduler Jobs { get; } = new();

    public IServiceProvider Services => _root;

    public async Task<T> SendAsync<T>(IRequest<T> request, Actor? actor = null)
    {
        var previous = User.Actor;
        if (actor is not null)
        {
            User.Actor = actor;
        }

        try
        {
            await using var scope = _root.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
        }
        finally
        {
            User.Actor = previous;
        }
    }

    /// <summary>Sends with no authenticated user (expects 401 unless the request is a system request).</summary>
    public async Task<T> SendAnonymousAsync<T>(IRequest<T> request)
    {
        var previous = User.Actor;
        User.Actor = null;
        try
        {
            await using var scope = _root.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
        }
        finally
        {
            User.Actor = previous;
        }
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _root.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    /// <summary>Replace a registration (for example with a fake) before the provider is built.</summary>
    public static Action<IServiceCollection> Replace<TService>(TService instance)
        where TService : class =>
        services => services.Replace(ServiceDescriptor.Singleton(instance));
}

/// <summary>The test user directory: the known actors above.</summary>
internal sealed class InMemoryUserDirectory(IEnumerable<Actor> actors) : IUserDirectory
{
    private readonly Dictionary<Guid, DirectoryUser> _users = actors.ToDictionary(a => a.UserId, a => new DirectoryUser(a.UserId, a.DisplayName, a.Role));

    public Task<DirectoryUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.GetValueOrDefault(userId));
}

/// <summary>Records enqueued GL posting jobs; can be told to fail, to simulate a lost enqueue.</summary>
internal sealed class RecordingJobScheduler : IJobScheduler
{
    private readonly List<(Guid TransactionId, string IdempotencyKey)> _enqueued = [];

    public bool FailEnqueue { get; set; }

    public IReadOnlyList<(Guid TransactionId, string IdempotencyKey)> Enqueued
    {
        get
        {
            lock (_enqueued)
            {
                return [.. _enqueued];
            }
        }
    }

    public void EnqueueGlPosting(Guid transactionId, string idempotencyKey)
    {
        if (FailEnqueue)
        {
            throw new InvalidOperationException("Simulated: the job storage is unavailable.");
        }

        lock (_enqueued)
        {
            _enqueued.Add((transactionId, idempotencyKey));
        }
    }
}
