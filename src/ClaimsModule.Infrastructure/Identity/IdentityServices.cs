using System.Security.Cryptography;
using System.Text;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.Infrastructure.Identity;

/// <summary>
/// Demo users (D-32, B4.3). Configuration section <c>DemoUsers</c>: ids, user names, display names and
/// roles (not secret, committed). Passwords live in <c>DemoUserPasswords:{userName}</c>: Key Vault in
/// Azure, user secrets or environment variables locally. A user without a password can't sign in.
/// </summary>
public sealed class DemoUsersOptions
{
    public const string SectionName = "DemoUsers";

    public List<DemoUser> Users { get; set; } = [];
}

public sealed class DemoUser
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

/// <summary>Configuration section <c>DemoUserPasswords</c>: user name → password.</summary>
public sealed class DemoUserPasswordsOptions : Dictionary<string, string>
{
    public const string SectionName = "DemoUserPasswords";

    public DemoUserPasswordsOptions()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}

/// <summary>Checks a demo user's credentials. Mock authentication (S11.4): no real identity provider.</summary>
public interface IDemoUserAuthenticator
{
    /// <summary>The user when the user name and password match; otherwise null.</summary>
    DirectoryUser? Authenticate(string userName, string password);

    IReadOnlyList<DirectoryUser> ListUsers();

    /// <summary>The demo accounts with their user names (for the UI's role switcher). Never includes passwords.</summary>
    IReadOnlyList<DemoAccount> ListAccounts();
}

public sealed record DemoAccount(Guid UserId, string UserName, string DisplayName, UserRole Role);

/// <summary>The configuration-backed user directory (replaces the P3 placeholder) and the demo authenticator.</summary>
internal sealed class ConfiguredUserDirectory(IOptions<DemoUsersOptions> users, IOptions<DemoUserPasswordsOptions> passwords)
    : IUserDirectory, IDemoUserAuthenticator
{
    public Task<DirectoryUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(users.Value.Users.Where(u => u.Id == userId).Select(ToDirectoryUser).FirstOrDefault());

    public IReadOnlyList<DirectoryUser> ListUsers() => users.Value.Users.Select(ToDirectoryUser).ToList();

    public IReadOnlyList<DemoAccount> ListAccounts() =>
        users.Value.Users.Select(u => new DemoAccount(u.Id, u.UserName, u.DisplayName, u.Role)).ToList();

    public DirectoryUser? Authenticate(string userName, string password)
    {
        var user = users.Value.Users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        var expected = user is not null && passwords.Value.TryGetValue(user.UserName, out var configured) && configured.Length > 0
            ? configured
            : null;

        // Compare even when the user or the password is unknown, so timing doesn't reveal which (D-41 style).
        var matches = SecretComparer.Equal(password ?? string.Empty, expected ?? "\0no-password-configured");
        return matches && expected is not null ? ToDirectoryUser(user!) : null;
    }

    private static DirectoryUser ToDirectoryUser(DemoUser u) => new(u.Id, u.DisplayName, u.Role);
}

/// <summary>Constant-time comparison of secrets of any length (hash first, then FixedTimeEquals).</summary>
public static class SecretComparer
{
    public static bool Equal(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}

// ------------------------------------------------------------------ JWT (S3, S11.4, D-32)

/// <summary>Configuration section <c>Jwt</c>. The signing key comes from Key Vault in Azure.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "claims-module";
    public string Audience { get; set; } = "claims-module-api";

    /// <summary>HMAC-SHA256 key, at least 32 bytes of UTF-8.</summary>
    public string? SigningKey { get; set; }

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);

    public SymmetricSecurityKey CreateSigningKey()
    {
        if (string.IsNullOrEmpty(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < MinimumKeyBytes)
        {
            throw new InvalidOperationException($"Jwt:SigningKey must be at least {MinimumKeyBytes} bytes (a Key Vault secret in Azure).");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
    }
}

/// <summary>The claim names in the bearer token (short JWT names; inbound claim mapping is off in the API).</summary>
public static class TokenClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Name = JwtRegisteredClaimNames.Name;
    public const string Role = "role";
    public const string Organisation = "org";
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt, DirectoryUser User);

public interface IJwtTokenIssuer
{
    IssuedToken Issue(DirectoryUser user);
}

/// <summary>Issues the HS256 bearer token with sub, name, role and org claims (D-32).</summary>
internal sealed class JwtTokenIssuer(IOptions<JwtOptions> options, ITenantProvider tenant, TimeProvider clock) : IJwtTokenIssuer
{
    public IssuedToken Issue(DirectoryUser user)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expires = now + settings.Lifetime;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [TokenClaims.Subject] = user.UserId.ToString(),
                [TokenClaims.Name] = user.DisplayName,
                [TokenClaims.Role] = user.Role.ToString(),
                [TokenClaims.Organisation] = tenant.OrganisationId.ToString(),
            },
        });
        return new IssuedToken(token, expires, user);
    }
}
