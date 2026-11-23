using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimsModule.API.Hosting;

/// <summary>Configuration section <c>RateLimits</c>.</summary>
public sealed class RateLimits
{
    public const string SectionName = "RateLimits";
    public const string SignIn = "sign-in";

    /// <summary>Sign-in attempts per client address per minute (brute-force brake on the demo passwords).</summary>
    public int SignInPerMinute { get; set; } = 10;
}

/// <summary>Controllers, JSON, the S10.4 error pipeline and rate limiting (tasks 32, 33).</summary>
internal static class ApiSetup
{
    public static IServiceCollection AddClaimsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers()
            .AddJsonOptions(o => JsonSetup.Configure(o.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiErrors.InvalidModelState);
        services.ConfigureHttpJsonOptions(o => JsonSetup.Configure(o.SerializerOptions));

        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();

        var limits = configuration.GetSection(RateLimits.SectionName).Get<RateLimits>() ?? new RateLimits();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimits.SignIn, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.SignInPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return new ValueTask(ApiErrors.WriteAsync(context.HttpContext, new ApiError
                {
                    Type = "TooManyRequests",
                    Title = "Too many sign-in attempts.",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "Wait a minute and try again.",
                }));
            };
        });

        return services;
    }
}
