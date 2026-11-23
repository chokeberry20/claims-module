using Hangfire;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure;
using ClaimsModule.Application.Abstractions.Storage;
using ClaimsModule.Application;
using ClaimsModule.API.Hosting;
using ClaimsModule.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to stdout (App Service collects them). writeToProviders passes the same events
// to the other logging providers, so Azure Monitor (below) receives them too.
builder.Host.UseSerilog(
    (context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter()),
    writeToProviders: true);

// Azure Monitor OpenTelemetry when the App Insights connection string is set (Azure only); D-71.
builder.Services.AddClaimsTelemetry(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("ClaimsDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:ClaimsDb is not configured (appsettings.Development.json locally; Key Vault reference on Azure).");
}

builder.Services.AddClaimsHosting(builder.Configuration, builder.Environment);
builder.Services.AddClaimsAuthentication(builder.Configuration);
builder.Services.AddClaimsApi(builder.Configuration);
builder.Services.AddPersistence(connectionString);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Warm the managed-identity token, SQL connection and EF model as the host starts, before the
// server listens (D-65, P3 incident). Registered before the Hangfire server, so it runs first.
builder.Services.AddHostedService<StartupWarmUp.HostedService>();

// Hangfire (P4 task 30): SQL Server storage in the HangFire schema; the server can be switched off (tests).
var jobsOptions = builder.Configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>() ?? new JobsOptions();
builder.Services.AddClaimsJobs(connectionString, jobsOptions);

// Fail-fast checks: their own short-timeout connection, outside the EF retry strategy.
builder.Services.AddSingleton(new HealthCheckDatabase(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", timeout: HealthCheckDatabase.CheckTimeout)
    .AddCheck<MigrationsHealthCheck>("migrations", timeout: HealthCheckDatabase.CheckTimeout)
    .AddCheck<StorageHealthCheck>("storage", timeout: HealthCheckDatabase.CheckTimeout);

builder.Services.AddClaimsSwagger();

var app = builder.Build();

// Order matters: the correlation id is in the log context before the request log is written, and the
// request log sees the final status code that the exception handler produces (task 33).
app.UseMiddleware<CorrelationMiddleware>();
app.UseSerilogRequestLogging();
app.UseCors(HostingServiceCollectionExtensions.CorsPolicy);
app.UseExceptionHandler();

// Development: the LocalFileSystem provider's plain read links are served from /uploads (D-30).
var storageOptions = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
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
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Claims Module API";
    options.EnablePersistAuthorization();
});

// Hangfire dashboard (D-41): Basic auth when a password is configured (Key Vault in Azure); local
// requests only in Development without one; otherwise not mapped at all (fail closed). It is
// middleware, so it must come before UseAuthorization: the bearer fallback policy also applies to
// requests that match no endpoint, and would otherwise answer the dashboard with a Bearer challenge.
var dashboard = app.Services.GetRequiredService<IOptions<HangfireDashboardOptions>>().Value;
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

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", version = BuildInfo.Version }))
    .WithName("Liveness")
    .WithTags("Health")
    .WithSummary("Liveness probe: the process is up, and which build it runs. No dependencies are checked.")
    .AllowAnonymous();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync }).AllowAnonymous();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription().AllowAnonymous();

if (jobsOptions.ServerEnabled)
{
    JobsRegistration.RegisterRecurringJobs(app.Services.GetRequiredService<Hangfire.IRecurringJobManager>());
}

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
