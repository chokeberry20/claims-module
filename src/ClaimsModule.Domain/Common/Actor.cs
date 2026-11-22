namespace ClaimsModule.Domain.Common;

/// <summary>
/// Spec S3 roles. The values are ordered: each role inherits every capability of the roles below it
/// (handler &lt; supervisor &lt; manager).
/// </summary>
public enum UserRole
{
    Handler = 0,
    Supervisor = 1,
    Manager = 2,
}

/// <summary>The authenticated user performing a domain action.</summary>
public sealed record Actor
{
    public Actor(Guid userId, string displayName, UserRole role)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id must not be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Actor display name is required.", nameof(displayName));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");
        }

        UserId = userId;
        DisplayName = displayName.Trim();
        Role = role;
    }

    public Guid UserId { get; }

    public string DisplayName { get; }

    public UserRole Role { get; }

    public bool HasAtLeast(UserRole role) => Role >= role;
}
