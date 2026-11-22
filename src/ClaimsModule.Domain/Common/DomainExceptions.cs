namespace ClaimsModule.Domain.Common;

/// <summary>A single broken business rule. <see cref="Field"/> keys the S10.4 error dictionary.</summary>
public sealed record RuleViolation(string Code, string Message, string? Field = null);

/// <summary>Base type for exceptions raised by domain logic.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One or more business rules are broken. The API maps this to HTTP 422 with the S10.4 error body.
/// </summary>
public sealed class DomainRuleViolationException : DomainException
{
    public DomainRuleViolationException(string code, string message, string? field = null)
        : this([new RuleViolation(code, message, field)])
    {
    }

    public DomainRuleViolationException(IReadOnlyCollection<RuleViolation> violations)
        : base(BuildMessage(violations))
    {
        Violations = violations;
    }

    public IReadOnlyCollection<RuleViolation> Violations { get; }

    public bool Contains(string code) => Violations.Any(v => v.Code == code);

    private static string BuildMessage(IReadOnlyCollection<RuleViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        if (violations.Count == 0)
        {
            throw new ArgumentException("At least one violation is required.", nameof(violations));
        }

        return string.Join(" ", violations.Select(v => v.Message));
    }
}

/// <summary>
/// The caller's role (or ownership) does not allow the action at all. The API maps this to HTTP 403.
/// When a role is allowed the action but not the amount, a 422 is raised instead (D-24).
/// </summary>
public sealed class DomainForbiddenException : DomainException
{
    public DomainForbiddenException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>A child entity referenced by id does not exist in the aggregate. The API maps this to HTTP 404.</summary>
public sealed class DomainNotFoundException : DomainException
{
    public DomainNotFoundException(string entityName, Guid id)
        : base($"{entityName} '{id}' was not found.")
    {
        EntityName = entityName;
        EntityId = id;
    }

    public string EntityName { get; }

    public Guid EntityId { get; }
}
