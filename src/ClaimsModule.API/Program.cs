var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness only. The DB-backed health check, Swagger, auth, Hangfire etc. arrive in P2.5/P5.
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
