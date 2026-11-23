using System.Security.Claims;
using System.Text;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using ClaimsModule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// JWT Bearer authentication (S3, S10, D-32, task 32). Tokens come from <c>POST /api/auth/token</c>
/// (mock identity, HS256). The ASP.NET layer only requires an authenticated caller; role rules are
/// enforced once, in the MediatR pipeline, from <c>[RequiresRole]</c> on each command (D-72).
/// </summary>
internal static class AuthenticationSetup
{
    public static IServiceCollection AddClaimsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // The signing key is checked at startup in every environment: without it no token could be issued
        // or validated, and the app would fail on the first request instead.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.SigningKey is not null && Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumKeyBytes,
                $"Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes (Key Vault in Azure; scripts/set-dev-secrets.ps1 locally).")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, ITenantProvider>((bearer, jwt, tenant) =>
            {
                var settings = jwt.Value;
                bearer.MapInboundClaims = false; // keep the token's short claim names (sub, name, role, org)
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = settings.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = TokenClaims.Name,
                    RoleClaimType = TokenClaims.Role,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                bearer.Events = new JwtBearerEvents
                {
                    // Single-tenant deployment (D-01): a token for another organisation is not valid here.
                    OnTokenValidated = context =>
                    {
                        if (!HttpCurrentUser.TryRead(context.Principal, out _)
                            || context.Principal!.FindFirstValue(TokenClaims.Organisation) != tenant.OrganisationId.ToString())
                        {
                            context.Fail("The token's user or organisation is not valid for this deployment.");
                        }

                        return Task.CompletedTask;
                    },
                    // The S10.4 error body instead of an empty 401/403 (the WWW-Authenticate header stays).
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                        await ApiErrors.WriteAsync(context.HttpContext, ApiErrors.Unauthorized());
                    },
                    OnForbidden = context => ApiErrors.WriteAsync(context.HttpContext, ApiErrors.Forbidden("You do not have access to this resource.")),
                };
            });

        // Every endpoint needs an authenticated caller unless it opts out ([AllowAnonymous]).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}

/// <summary>The caller from the validated bearer token (sub, name, role). Unauthenticated outside a request (jobs).</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private readonly Lazy<(Guid Id, string Name, UserRole Role)?> _user = new(() =>
        TryRead(accessor.HttpContext?.User, out var user) ? user : null);

    public bool IsAuthenticated => _user.Value is not null;

    public Guid? UserId => _user.Value?.Id;

    public string? DisplayName => _user.Value?.Name;

    public UserRole? Role => _user.Value?.Role;

    public Actor GetActor() =>
        _user.Value is { } user
            ? new Actor(user.Id, user.Name, user.Role)
            : throw new ClaimsModule.Application.Common.Exceptions.UnauthenticatedException();

    public static bool TryRead(ClaimsPrincipal? principal, out (Guid Id, string Name, UserRole Role) user)
    {
        user = default;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(TokenClaims.Subject), out var id) || id == Guid.Empty
            || principal.FindFirstValue(TokenClaims.Name) is not { Length: > 0 } name
            || !Enum.TryParse<UserRole>(principal.FindFirstValue(TokenClaims.Role), ignoreCase: false, out var role)
            || !Enum.IsDefined(role))
        {
            return false;
        }

        user = (id, name, role);
        return true;
    }
}
