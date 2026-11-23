using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Abstractions.Storage;
using ClaimsModule.Domain.Common;
using ClaimsModule.Persistence.Seed;
using Microsoft.Extensions.Options;

namespace ClaimsModule.API.Hosting;

/// <summary><c>Tenancy</c> section: the single organisation of this deployment (S15.1, D-01).</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public Guid OrganisationId { get; set; }
}

/// <summary>
/// The tenant from configuration. Startup checks that it matches the seeded organisation, so a
/// typo can't silently show an empty database. From P5 the JWT `org` claim is the source.
/// </summary>
internal sealed class ConfiguredTenantProvider(IOptions<TenancyOptions> options) : ITenantProvider
{
    public Guid OrganisationId { get; } = options.Value.OrganisationId;
}

/// <summary>Walking-skeleton caller: unauthenticated until JWT auth arrives in P5 (task 32).</summary>
internal sealed class AnonymousCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public string? DisplayName => null;

    public UserRole? Role => null;

    public Actor GetActor() => throw new InvalidOperationException("No authenticated user: authentication is added in P5.");
}

/// <summary>
/// The request correlation id (S14.2): the incoming <c>X-Correlation-Id</c> header if it is a
/// GUID, otherwise a new one, fixed for the rest of the request. P5 task 33 adds the middleware
/// that echoes it on responses.
/// </summary>
internal sealed class RequestCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    private Guid? _correlationId;

    public Guid? CorrelationId => _correlationId ??=
        Guid.TryParse(accessor.HttpContext?.Request.Headers[HeaderName].ToString(), out var fromHeader) ? fromHeader : Guid.NewGuid();
}

/// <summary>
/// Placeholder until the MediatR dispatcher arrives in P3. It <b>fails loudly</b> if any domain
/// event reaches it, so audit entries can never be dropped silently. The walking skeleton has no
/// endpoints that raise events.
/// </summary>
/// <summary>The demo user directory is configuration-backed and arrives in P4 (task 31, D-32).</summary>
internal sealed class NotYetWiredUserDirectory : IUserDirectory
{
    public Task<DirectoryUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The user directory is wired in P4 (task 31).");
}

internal static class HostingServiceCollectionExtensions
{
    public const string CorsPolicy = "frontend";

    public static IServiceCollection AddClaimsHosting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Document storage (S13, BR-D-03). Local storage is for development only: Azure always uses
        // Blob storage, so a misconfigured Production host fails at startup (D-69). A relative local
        // folder is resolved against the content root.
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .PostConfigure(o => o.LocalFileSystem.RootPath = Path.GetFullPath(o.LocalFileSystem.RootPath, environment.ContentRootPath))
            .Validate(o => !(environment.IsProduction() && o.Provider == StorageProvider.LocalFileSystem),
                "Storage:Provider LocalFileSystem is not allowed in Production; use AzureBlob.")
            .Validate(o => o.Provider != StorageProvider.AzureBlob || !string.IsNullOrWhiteSpace(o.AzureBlob.ConnectionString),
                "Storage:AzureBlob:ConnectionString is required when Storage:Provider is AzureBlob.")
            .ValidateOnStart();

        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName))
            .Validate(o => o.OrganisationId == SeedData.DefaultOrganisationId,
                $"Tenancy:OrganisationId must equal the seeded organisation {SeedData.DefaultOrganisationId}.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddSingleton<ITenantProvider, ConfiguredTenantProvider>();
        services.AddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ICorrelationContext, RequestCorrelationContext>();
        services.AddSingleton<IUserDirectory, NotYetWiredUserDirectory>();
        services.Configure<ClaimsModule.Application.Abstractions.Jobs.GlPostingOptions>(configuration.GetSection(ClaimsModule.Application.Abstractions.Jobs.GlPostingOptions.SectionName));

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(RequestCorrelationContext.HeaderName)));

        return services;
    }
}
