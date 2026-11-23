using System.Net.Http.Headers;
using System.Text;
using ClaimsModule.Infrastructure.Identity;
using Hangfire.Dashboard;

namespace ClaimsModule.API.Hosting;

/// <summary>Configuration section <c>Hangfire:Dashboard</c>. The password is a Key Vault secret in Azure (D-41).</summary>
public sealed class HangfireDashboardOptions
{
    public const string SectionName = "Hangfire:Dashboard";
    public const string Path = "/hangfire";

    public string Username { get; set; } = "admin";

    public string? Password { get; set; }

    public bool IsConfigured => !string.IsNullOrEmpty(Password);
}

/// <summary>
/// HTTP Basic authentication for the Hangfire dashboard (D-41). A browser navigating to /hangfire can't
/// send the API's bearer token, so the dashboard has its own credentials. On failure it answers 401
/// with <c>WWW-Authenticate: Basic</c>, which makes the browser show its sign-in prompt (a filter that
/// only returns false gives a bare 401 and no prompt). Credentials are compared in constant time.
/// </summary>
internal sealed class BasicAuthDashboardFilter(HangfireDashboardOptions options) : IDashboardAuthorizationFilter
{
    public const string Realm = "Hangfire";

    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (IsAuthorized(http.Request.Headers.Authorization.ToString()))
        {
            return true;
        }

        http.Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        return false;
    }

    internal bool IsAuthorized(string? authorizationHeader)
    {
        if (!options.IsConfigured
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        // Both parts are always compared, so timing doesn't reveal which one was wrong.
        var userOk = SecretComparer.Equal(decoded[..separator], options.Username);
        var passwordOk = SecretComparer.Equal(decoded[(separator + 1)..], options.Password!);
        return userOk & passwordOk;
    }
}
