using ClaimsModule.Domain.Common;

namespace ClaimsModule.Application.Abstractions;

/// <summary>The authenticated caller (from the JWT in the API; a system identity in background jobs).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? DisplayName { get; }

    UserRole? Role { get; }

    /// <summary>The caller as a domain actor. Throws if there is no authenticated user.</summary>
    Actor GetActor();
}

/// <summary>Tenant isolation (S15.1): the organisation every query and write is scoped to (D-01).</summary>
public interface ITenantProvider
{
    Guid OrganisationId { get; }
}

/// <summary>The request correlation id, copied onto every audit entry in the same request (S14.2).</summary>
public interface ICorrelationContext
{
    Guid? CorrelationId { get; }
}
