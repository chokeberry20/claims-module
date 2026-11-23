using Hangfire;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure;
using ClaimsModule.Application.Abstractions.Storage;
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

builder.Services.AddClaimsHosting(builder.Configuration, builder.Environment);
builder.Services.AddPersistence(connectionString);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Hangfire (P4 task 30): SQL Server storage in the HangFire schema; the server can be switched off (tests).
var jobsOptions = builder.Configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>() ?? new JobsOptions();
builder.Services.AddClaimsJobs(connectionString, jobsOptions);

// Fail-fast checks: their own short-timeout connection, outside the EF retry strategy.
builder.Services.AddSingleton(new HealthCheckDatabase(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", timeout: HealthCheckDatabase.CheckTimeout)
    .AddCheck<MigrationsHealthCheck>("migrations", timeout: HealthCheckDatabase.CheckTimeout)
    .AddCheck<StorageHealthCheck>("storage", timeout: HealthCheckDatabase.CheckTimeout);

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

// Development: the LocalFileSystem provider's plain read links are served from /uploads (D-30).
var storageOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
if (storageOptions.Provider == StorageProvider.LocalFileSystem)
{
    Directory.CreateDirectory(storageOptions.LocalFileSystem.RootPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storageOptions.LocalFileSystem.RootPath),
        RequestPath = "/uploads",
    });
}

// Swagger stays on in every environment: the brief requires it on the deployed backend (B4.3).
app.UseSwagger();
app.UseSwaggerUI(options => options.DocumentTitle = "Claims Module API");

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", version = BuildInfo.Version }))
    .WithName("Liveness")
    .WithTags("Health")
    .WithSummary("Liveness probe: the process is up, and which build it runs. No dependencies are checked.");

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Hangfire dashboard (D-41): Basic auth when a password is configured (Key Vault in Azure); local
// requests only in Development without one; otherwise not mapped at all (fail closed).
var dashboard = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HangfireDashboardOptions>>().Value;
if (dashboard.IsConfigured)
{
    app.UseHangfireDashboard(HangfireDashboardOptions.Path, new Hangfire.DashboardOptions
    {
        Authorization = [new BasicAuthDashboardFilter(dashboard)],
        DisplayStorageConnectionString = false,
        AppPath = null,
    });
}
else if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard(HangfireDashboardOptions.Path, new Hangfire.DashboardOptions
    {
        Authorization = [new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter()],
        DisplayStorageConnectionString = false,
        AppPath = null,
    });
}

if (jobsOptions.ServerEnabled)
{
    JobsRegistration.RegisterRecurringJobs(app.Services.GetRequiredService<Hangfire.IRecurringJobManager>());
}

// Warm the managed-identity token, SQL connection and EF model before serving (D-65, P3 incident).
await StartupWarmUp.RunAsync(app.Services, app.Configuration, app.Logger);

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
