using ClaimsModule.Application;
using ClaimsModule.API.Hosting;
using ClaimsModule.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to stdout. App Service and Container Apps collect them, and App Insights is wired in 17c.
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

var connectionString = builder.Configuration.GetConnectionString("ClaimsDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:ClaimsDb is not configured (appsettings.Development.json locally; Key Vault reference on Azure).");
}

builder.Services.AddClaimsHosting(builder.Configuration);
builder.Services.AddPersistence(connectionString);
builder.Services.AddApplication();

// Fail-fast checks: their own short-timeout connection, outside the EF retry strategy.
builder.Services.AddSingleton(new HealthCheckDatabase(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", timeout: HealthCheckDatabase.CheckTimeout)
    .AddCheck<MigrationsHealthCheck>("migrations", timeout: HealthCheckDatabase.CheckTimeout);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Claims Module API",
    Version = "v1",
    Description = "FNOL intake and reserve management (DICEUS technical assessment).",
}));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors(HostingServiceCollectionExtensions.CorsPolicy);

// Swagger stays on in every environment: the brief requires it on the deployed backend (B4.3).
app.UseSwagger();
app.UseSwaggerUI(options => options.DocumentTitle = "Claims Module API");

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Liveness")
    .WithTags("Health")
    .WithSummary("Liveness probe: the process is up. No dependencies are checked.");

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
