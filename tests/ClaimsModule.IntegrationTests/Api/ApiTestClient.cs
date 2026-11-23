using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>HTTP helpers for the API tests: sign-in as a demo user, JSON with enum names, the S10.4 error body.</summary>
internal static class ApiTestClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<string> TokenAsync(this HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new { userName, password = ClaimsApiFactory.PasswordFor(userName) }, Json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>A client signed in as <paramref name="userName"/> (bearer token on every request).</summary>
    public static async Task<HttpClient> SignedInClientAsync(this WebApplicationFactory<Program> factory, string userName)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await client.TokenAsync(userName));
        return client;
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return body.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(body, Json);
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body, string? idempotencyKey = null) =>
        client.SendJsonAsync(HttpMethod.Post, url, body, idempotencyKey);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body, string? idempotencyKey = null) =>
        client.SendJsonAsync(HttpMethod.Put, url, body, idempotencyKey);

    public static Task<HttpResponseMessage> SendJsonAsync(this HttpClient client, HttpMethod method, string url, object? body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    /// <summary>Asserts the S10.4 shape: type, title, status (and returns the body).</summary>
    public static async Task<JsonElement> ErrorAsync(this HttpResponseMessage response, HttpStatusCode status, string type)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var error = await response.JsonAsync(status);
        error.GetProperty("type").GetString().ShouldBe(type);
        error.GetProperty("status").GetInt32().ShouldBe((int)status);
        error.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        return error;
    }

    public static string[] Messages(this JsonElement error, string field) =>
        error.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    public static string[] Codes(this JsonElement error, string field) =>
        error.GetProperty("codes").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    /// <summary>The host with an extra logger provider (Serilog forwards to the registered providers).</summary>
    public static WebApplicationFactory<Program> WithLogs(this ClaimsApiFactory factory, CapturingLoggerProvider logs) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(logs)));

    /// <summary>An in-force seeded policy and an active cause code, read through the API.</summary>
    public static async Task<(Guid PolicyId, string CauseCode, DateOnly EffectiveDate)> ReferenceAsync(this HttpClient client)
    {
        var policies = await (await client.GetAsync("/api/policies/search?q=POL")).JsonAsync(HttpStatusCode.OK);
        var policy = policies.EnumerateArray().First(p => p.GetProperty("status").GetString() == "Active");
        var codes = await (await client.GetAsync("/api/reference/cause-of-loss-codes")).JsonAsync(HttpStatusCode.OK);
        return (policy.GetProperty("policyId").GetGuid(), codes[0].GetProperty("code").GetString()!, DateOnly.Parse(policy.GetProperty("effectiveDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A valid FNOL body: claimant, primary risk object, linked in-force policy, loss date inside the period.</summary>
    public static object FnolBody(Guid policyId, string causeCode, DateOnly effectiveDate, object? initialReserve = null) => new
    {
        policyId,
        lossDate = MaxOf(effectiveDate.AddDays(1), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc),
        lossDescription = "Rear-end collision at a traffic light; bumper and tail lights damaged.",
        causeOfLossCode = causeCode,
        lossLocation = "Main Street 1",
        parties = new[] { new { role = "Claimant", type = "Person", firstName = "Ada", lastName = "Lovelace", email = "ada@example.test" } },
        riskObjects = new[] { new { assetType = "Vehicle", assetDescription = "Blue hatchback", isPrimary = true } },
        initialReserve,
    };

    private static DateOnly MaxOf(DateOnly a, DateOnly b) => a > b ? a : b;
}

internal sealed record CapturedLog(string Category, LogLevel Level, string Message);

/// <summary>Keeps every log entry the host writes (thread-safe).</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLog> _entries = [];

    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries)
            {
                owner._entries.Add(new CapturedLog(category, logLevel, formatter(state, exception)));
            }
        }
    }
}
