using ClaimsModule.Domain.Common;

namespace ClaimsModule.Application.Common.Exceptions;

/// <summary>
/// The request itself is invalid (FluentValidation, D-05 layer a). Nothing is saved. The API maps
/// this to HTTP 422 with the S10.4 body, the same shape as a <see cref="DomainRuleViolationException"/>.
/// </summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IReadOnlyCollection<RuleViolation> violations)
        : base(string.Join(" ", violations.Select(v => v.Message)))
    {
        Violations = violations;
    }

    public IReadOnlyCollection<RuleViolation> Violations { get; }
}

/// <summary>No authenticated user. The API maps this to HTTP 401.</summary>
public sealed class UnauthenticatedException() : Exception("Authentication is required.");

/// <summary>The caller's role is below the request's minimum role. The API maps this to HTTP 403.</summary>
public sealed class ForbiddenAccessException(UserRole requiredRole)
    : Exception($"This action requires the {requiredRole} role or higher.")
{
    public UserRole RequiredRole { get; } = requiredRole;
}

/// <summary>An aggregate or record referenced by id does not exist (in this organisation). The API maps this to HTTP 404.</summary>
public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} '{key}' was not found.")
{
    public string EntityName { get; } = entityName;

    public object Key { get; } = key;
}

/// <summary>
/// The Idempotency-Key was already used with a different request, or a request with the same key
/// is still being processed. The API maps this to HTTP 409.
/// </summary>
public sealed class IdempotencyConflictException(string message) : Exception(message);
