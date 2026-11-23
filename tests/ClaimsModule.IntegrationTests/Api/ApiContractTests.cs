using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>
/// Tasks 32–33 over HTTP: bearer authentication, the S10.4 error body for every failure class,
/// Idempotency-Key replay and conflicts, the correlation id and the Swagger document.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ApiContractTests(SqlServerDatabaseFixture db) : IDisposable
{
    private readonly ClaimsApiFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    // ------------------------------------------------------------------ authentication (401)

    [Fact]
    public async Task Anonymous_calls_get_401_with_the_error_body_and_a_bearer_challenge()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/claims");

        await response.ErrorAsync(HttpStatusCode.Unauthorized, "Unauthorized");
        response.Headers.WwwAuthenticate.Single().Scheme.ShouldBe("Bearer");
    }

    [Fact]
    public async Task Health_swagger_and_the_user_list_stay_anonymous()
    {
        using var client = _factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var users = await (await client.GetAsync("/api/auth/users")).JsonAsync(HttpStatusCode.OK);
        users.EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ShouldBe(["handler1", "handler2", "supervisor1", "manager1"]);
        users.GetRawText().ShouldNotContain("password", Case.Insensitive);
    }

    [Fact]
    public async Task A_wrong_password_gives_401_and_a_valid_one_a_token_for_the_right_user()
    {
        using var client = _factory.CreateClient();

        var wrong = await client.PostJsonAsync("/api/auth/token", new { userName = "handler1", password = "not-the-password" });
        (await wrong.ErrorAsync(HttpStatusCode.Unauthorized, "Unauthorized")).GetProperty("title").GetString().ShouldBe("Invalid user name or password.");
        var unknown = await client.PostJsonAsync("/api/auth/token", new { userName = "nobody", password = "x" });
        await unknown.ErrorAsync(HttpStatusCode.Unauthorized, "Unauthorized");
        var empty = await client.PostJsonAsync("/api/auth/token", new { userName = "", password = "" });
        (await empty.ErrorAsync(HttpStatusCode.UnprocessableEntity, "ValidationError")).Codes("UserName").ShouldBe(["USERNAME_REQUIRED"]);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await client.TokenAsync("supervisor1"));
        var me = await (await client.GetAsync("/api/auth/me")).JsonAsync(HttpStatusCode.OK);
        me.GetProperty("displayName").GetString().ShouldBe("Sam Supervisor");
        me.GetProperty("role").GetString().ShouldBe("Supervisor");
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("other-org")]
    [InlineData("expired")]
    [InlineData("no-role")]
    public async Task Tokens_that_are_forged_for_another_organisation_expired_or_incomplete_get_401(string kind)
    {
        using var client = _factory.CreateClient();
        var key = kind == "wrong-key" ? new string('k', 48) : ClaimsApiFactory.SigningKey;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "a1000000-0000-0000-0000-000000000001",
            ["name"] = "Hannah Handler",
            ["org"] = kind == "other-org" ? Guid.NewGuid().ToString() : SqlServerDatabaseFixture.OrganisationId.ToString(),
        };
        if (kind != "no-role")
        {
            claims["role"] = "Manager";
        }

        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "claims-module",
            Audience = "claims-module-api",
            Claims = claims,
            IssuedAt = kind == "expired" ? now.AddHours(-3) : now.AddMinutes(-1),
            NotBefore = kind == "expired" ? now.AddHours(-3) : now.AddMinutes(-1),
            Expires = kind == "expired" ? now.AddHours(-2) : now.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await (await client.GetAsync("/api/claims")).ErrorAsync(HttpStatusCode.Unauthorized, "Unauthorized");
    }

    [Fact]
    public async Task Sign_in_is_rate_limited_per_client()
    {
        await using var limited = new ClaimsApiFactory(db, new Dictionary<string, string?> { ["RateLimits:SignInPerMinute"] = "2" });
        using var client = limited.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            (await client.PostJsonAsync("/api/auth/token", new { userName = "handler1", password = "wrong" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var third = await client.PostJsonAsync("/api/auth/token", new { userName = "handler1", password = ClaimsApiFactory.PasswordFor("handler1") });
        await third.ErrorAsync(HttpStatusCode.TooManyRequests, "TooManyRequests");
        third.Headers.RetryAfter.ShouldNotBeNull();
    }

    // ------------------------------------------------------------------ the S10.4 error body

    [Fact]
    public async Task Request_rule_breaks_give_422_with_errors_and_codes_per_field_and_save_nothing()
    {
        using var client = await _factory.SignedInClientAsync("handler1");
        var before = (await (await client.GetAsync("/api/claims?pageSize=1")).JsonAsync(HttpStatusCode.OK)).GetProperty("totalCount").GetInt32();

        var response = await client.PostJsonAsync("/api/claims", new
        {
            lossDate = DateTime.UtcNow.AddDays(2),
            lossDescription = "too short",
            causeOfLossCode = "NOT-A-CODE",
            parties = Array.Empty<object>(),
        });

        var error = await response.ErrorAsync(HttpStatusCode.UnprocessableEntity, "ValidationError");
        error.GetProperty("title").GetString().ShouldBe("One or more validation errors occurred.");
        error.Messages("LossDate").ShouldContain("Loss date cannot be in the future.");
        error.Codes("LossDate").ShouldNotBeEmpty();
        error.Codes("LossDescription").ShouldNotBeEmpty();
        error.Codes("CauseOfLossCode").ShouldNotBeEmpty();
        (await (await client.GetAsync("/api/claims?pageSize=1")).JsonAsync(HttpStatusCode.OK)).GetProperty("totalCount").GetInt32().ShouldBe(before);
    }

    [Fact]
    public async Task Malformed_json_and_unknown_enum_values_give_the_same_422_shape()
    {
        using var client = await _factory.SignedInClientAsync("handler1");

        var malformed = await client.PostAsync("/api/claims", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        (await malformed.ErrorAsync(HttpStatusCode.UnprocessableEntity, "ValidationError")).GetProperty("codes").GetRawText().ShouldContain("REQUEST_MALFORMED");

        var badEnum = await client.GetAsync("/api/claims?status=Sleeping");
        await badEnum.ErrorAsync(HttpStatusCode.UnprocessableEntity, "ValidationError");
    }

    [Fact]
    public async Task Unknown_ids_give_404_and_a_role_below_the_minimum_gives_403()
    {
        using var handler = await _factory.SignedInClientAsync("handler1");
        await (await handler.GetAsync($"/api/claims/{Guid.NewGuid()}")).ErrorAsync(HttpStatusCode.NotFound, "NotFound");
        await (await handler.GetAsync($"/api/policies/{Guid.NewGuid()}/coverage")).ErrorAsync(HttpStatusCode.NotFound, "NotFound");

        // Reserve-limit override is manager-only ([RequiresRole]); the role check runs before the claim is loaded.
        var forbidden = await handler.PutJsonAsync($"/api/claims/{Guid.NewGuid()}/reserve-limit-override", new { enabled = true });
        (await forbidden.ErrorAsync(HttpStatusCode.Forbidden, "Forbidden")).Codes("General").ShouldBe(["ROLE_REQUIRED"]);
    }

    [Fact]
    public async Task The_claim_list_filters_by_policy_and_by_utc_loss_dates()
    {
        using var client = await _factory.SignedInClientAsync("handler1");
        var (policyId, cause, effective) = await client.ReferenceAsync();
        var created = await (await client.PostJsonAsync("/api/claims", ApiTestClient.FnolBody(policyId, cause, effective))).JsonAsync(HttpStatusCode.Created);
        var number = created.GetProperty("claimNumber").GetString();
        var detail = await (await client.GetAsync($"/api/claims/{created.GetProperty("claimId").GetGuid()}")).JsonAsync(HttpStatusCode.OK);
        var lossDay = DateOnly.FromDateTime(detail.GetProperty("lossEvent").GetProperty("lossDate").GetDateTimeOffset().UtcDateTime);

        async Task<int> CountAsync(string query) =>
            (await (await client.GetAsync($"/api/claims?search={number}&{query}")).JsonAsync(HttpStatusCode.OK)).GetProperty("totalCount").GetInt32();

        (await CountAsync($"policyId={policyId}")).ShouldBe(1);
        (await CountAsync($"policyId={Guid.NewGuid()}")).ShouldBe(0);
        (await CountAsync($"dateFrom={lossDay:yyyy-MM-dd}&dateTo={lossDay:yyyy-MM-dd}")).ShouldBe(1); // "to" includes the whole day
        (await CountAsync($"dateFrom={lossDay.AddDays(1):yyyy-MM-dd}")).ShouldBe(0);
    }

    // ------------------------------------------------------------------ Idempotency-Key (S10, D-66)

    [Fact]
    public async Task Repeating_a_create_with_the_same_key_returns_the_first_201_and_creates_one_claim()
    {
        using var client = await _factory.SignedInClientAsync("handler1");
        var (policyId, cause, effective) = await client.ReferenceAsync();
        var body = ApiTestClient.FnolBody(policyId, cause, effective);
        var key = Guid.NewGuid().ToString();

        var first = await client.PostJsonAsync("/api/claims", body, key);
        var firstJson = await first.JsonAsync(HttpStatusCode.Created);
        var second = await client.PostJsonAsync("/api/claims", body, key);
        var secondJson = await second.JsonAsync(HttpStatusCode.Created);

        secondJson.GetProperty("claimId").GetGuid().ShouldBe(firstJson.GetProperty("claimId").GetGuid());
        secondJson.GetProperty("claimNumber").GetString().ShouldBe(firstJson.GetProperty("claimNumber").GetString());
        second.Headers.Location.ShouldBe(first.Headers.Location);
        var number = firstJson.GetProperty("claimNumber").GetString()!;
        var found = await (await client.GetAsync($"/api/claims?search={number}")).JsonAsync(HttpStatusCode.OK);
        found.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_gives_409_and_a_malformed_key_gives_422()
    {
        using var client = await _factory.SignedInClientAsync("handler1");
        var (policyId, cause, effective) = await client.ReferenceAsync();
        var key = Guid.NewGuid().ToString();
        (await client.PostJsonAsync("/api/claims", ApiTestClient.FnolBody(policyId, cause, effective), key)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var different = await client.PostJsonAsync("/api/claims", new { lossDescription = "a different request with the same key", lossDate = DateTime.UtcNow.AddDays(-1), causeOfLossCode = cause }, key);
        (await different.ErrorAsync(HttpStatusCode.Conflict, "Conflict")).Codes("General").ShouldBe(["IDEMPOTENCY_CONFLICT"]);

        var tooLong = await client.PostJsonAsync("/api/claims", ApiTestClient.FnolBody(policyId, cause, effective), new string('k', 201));
        (await tooLong.ErrorAsync(HttpStatusCode.UnprocessableEntity, "ValidationError")).Codes("Idempotency-Key").ShouldBe(["IDEMPOTENCY_KEY_INVALID"]);
    }

    // ------------------------------------------------------------------ correlation id (S14.2)

    [Fact]
    public async Task The_correlation_id_is_echoed_and_appears_in_error_bodies_and_audit_entries()
    {
        using var client = await _factory.SignedInClientAsync("handler1");
        var (policyId, cause, effective) = await client.ReferenceAsync();
        var correlation = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/claims") { Content = System.Net.Http.Json.JsonContent.Create(ApiTestClient.FnolBody(policyId, cause, effective), options: ApiTestClient.Json) };
        request.Headers.Add("X-Correlation-Id", correlation.ToString());
        var created = await client.SendAsync(request);
        var claim = await created.JsonAsync(HttpStatusCode.Created);
        created.Headers.GetValues("X-Correlation-Id").Single().ShouldBe(correlation.ToString());

        var audit = await (await client.GetAsync($"/api/claims/{claim.GetProperty("claimId").GetGuid()}/audit")).JsonAsync(HttpStatusCode.OK);
        audit.GetProperty("items").EnumerateArray().ShouldContain(e => e.GetProperty("correlationId").GetGuid() == correlation);

        var notFound = new HttpRequestMessage(HttpMethod.Get, $"/api/claims/{Guid.NewGuid()}");
        notFound.Headers.Add("X-Correlation-Id", correlation.ToString());
        var error = await (await client.SendAsync(notFound)).ErrorAsync(HttpStatusCode.NotFound, "NotFound");
        error.GetProperty("correlationId").GetGuid().ShouldBe(correlation);

        // Without the header a new id is issued.
        var fresh = await client.GetAsync("/api/reference/claim-statuses");
        Guid.TryParse(fresh.Headers.GetValues("X-Correlation-Id").Single(), out _).ShouldBeTrue();
    }

    // ------------------------------------------------------------------ Swagger (B4.3, B6.2)

    [Fact]
    public async Task Swagger_documents_every_endpoint_with_bearer_security_and_the_idempotency_header()
    {
        using var client = _factory.CreateClient();
        var doc = await (await client.GetAsync("/swagger/v1/swagger.json")).JsonAsync(HttpStatusCode.OK);
        var paths = doc.GetProperty("paths");

        string[] expected =
        [
            "/api/claims", "/api/claims/{claimId}", "/api/claims/{claimId}/status", "/api/claims/{claimId}/audit",
            "/api/claims/{claimId}/transitions", "/api/claims/{claimId}/parties", "/api/claims/{claimId}/parties/{partyId}",
            "/api/claims/{claimId}/documents", "/api/claims/{claimId}/reserves", "/api/claims/{claimId}/reserves/{componentId}",
            "/api/claims/{claimId}/reserves/{transactionId}/approve", "/api/claims/{claimId}/reserves/{transactionId}/reject",
            "/api/claims/{claimId}/reserves/{transactionId}/retract", "/api/reference/cause-of-loss-codes",
            "/api/reference/claim-statuses", "/api/policies/search", "/api/policies/{policyId}/coverage", "/api/auth/token",
        ];
        foreach (var path in expected)
        {
            paths.TryGetProperty(path, out _).ShouldBeTrue($"missing {path}");
        }

        doc.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).ShouldBeTrue();
        var create = paths.GetProperty("/api/claims").GetProperty("post");
        create.GetProperty("parameters").EnumerateArray().ShouldContain(p => p.GetProperty("name").GetString() == "Idempotency-Key");
        create.GetProperty("security").GetArrayLength().ShouldBe(1);
        create.GetProperty("responses").TryGetProperty("422", out _).ShouldBeTrue();
        paths.GetProperty("/api/auth/token").GetProperty("post").TryGetProperty("security", out _).ShouldBeFalse();
        // Enums are documented as names, the way the API sends them.
        doc.GetRawText().ShouldContain("\"PendingApproval\"");
    }

    [Fact]
    public async Task Claim_statuses_list_every_status_with_its_next_statuses()
    {
        using var client = await _factory.SignedInClientAsync("handler1");
        var statuses = await (await client.GetAsync("/api/reference/claim-statuses")).JsonAsync(HttpStatusCode.OK);

        statuses.GetArrayLength().ShouldBe(7);
        var draft = statuses.EnumerateArray().Single(s => s.GetProperty("status").GetString() == "Draft");
        draft.GetProperty("nextStatuses").EnumerateArray().Select(t => t.GetProperty("toStatus").GetString()).ShouldBe(["Open"]);
        var withdrawn = statuses.EnumerateArray().Single(s => s.GetProperty("status").GetString() == "Withdrawn");
        withdrawn.GetProperty("nextStatuses").GetArrayLength().ShouldBe(0);

        var auto = await (await client.GetAsync("/api/reference/cause-of-loss-codes?perilCategory=Auto")).JsonAsync(HttpStatusCode.OK);
        auto.GetArrayLength().ShouldBeGreaterThan(0);
        auto.EnumerateArray().ShouldAllBe(c => c.GetProperty("perilCategory").GetString() == "Auto");
    }
}
