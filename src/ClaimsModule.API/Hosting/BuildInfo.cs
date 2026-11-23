using System.Reflection;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// The running build: the assembly's informational version, which carries the commit SHA after a '+'
/// (deploy.yml publishes with -p:SourceRevisionId=$GITHUB_SHA). The smoke test waits until
/// /health/live reports the commit being deployed, so it never checks an old process (P3 incident).
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BuildInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
