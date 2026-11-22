using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.IntegrationTests.Persistence;

internal sealed class TestTenantProvider(Guid organisationId) : ITenantProvider
{
    public Guid OrganisationId { get; set; } = organisationId;
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public Actor? Actor { get; set; }

    public bool IsAuthenticated => Actor is not null;

    public Guid? UserId => Actor?.UserId;

    public string? DisplayName => Actor?.DisplayName;

    public UserRole? Role => Actor?.Role;

    public Actor GetActor() => Actor ?? throw new InvalidOperationException("No authenticated user.");
}

internal sealed class TestCorrelationContext : ICorrelationContext
{
    public Guid? CorrelationId { get; set; } = Guid.NewGuid();
}

/// <summary>A TimeProvider frozen at a settable instant.</summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
