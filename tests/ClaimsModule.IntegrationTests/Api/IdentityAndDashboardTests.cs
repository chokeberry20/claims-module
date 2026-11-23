using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClaimsModule.API.Hosting;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using ClaimsModule.Infrastructure.Identity;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>Task 31: demo user directory, JWT issuer, Hangfire dashboard Basic auth (D-32, D-41).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class IdentityAndDashboardTests(SqlServerDatabaseFixture db)
{
    private static readonly Guid Handler1Id = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private const string SigningKey = "test-signing-key-test-signing-key-0123456789";

    private static ConfiguredUserDirectory Directory(params (string User, string Password)[] passwords)
    {
        var configured = new DemoUserPasswordsOptions();
        foreach (var (user, password) in passwords)
        {
            configured[user] = password;
        }

        return new ConfiguredUserDirectory(
            Options.Create(new DemoUsersOptions
            {
                Users =
                [
                    new DemoUser { Id = Handler1Id, UserName = "handler1", DisplayName = "Hannah Handler", Role = UserRole.Handler },
                    new DemoUser { Id = Guid.NewGuid(), UserName = "manager1", DisplayName = "Mia Manager", Role = UserRole.Manager },
                ],
            }),
            Options.Create(configured));
    }

    // ------------------------------------------------------------------ directory and authenticator

    [Fact]
    public async Task Directory_finds_users_and_authenticates_only_with_the_configured_password()
    {
        var directory = Directory(("handler1", "s3cret-h1"));

        (await directory.FindAsync(Handler1Id, default)).ShouldNotBeNull().DisplayName.ShouldBe("Hannah Handler");
        (await directory.FindAsync(Guid.NewGuid(), default)).ShouldBeNull();
        directory.Authenticate("HANDLER1", "s3cret-h1").ShouldNotBeNull().Role.ShouldBe(UserRole.Handler); // user name is case-insensitive
        directory.Authenticate("handler1", "wrong").ShouldBeNull();
        directory.Authenticate("handler1", "S3CRET-H1").ShouldBeNull(); // the password is not
        directory.Authenticate("nobody", "s3cret-h1").ShouldBeNull();
        directory.Authenticate("manager1", "").ShouldBeNull(); // no password configured: can't sign in
        directory.ListUsers().Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------ JWT

    [Fact]
    public async Task Issued_token_validates_and_carries_sub_name_role_and_org()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var issuer = new JwtTokenIssuer(
            Options.Create(new JwtOptions { SigningKey = SigningKey, Lifetime = TimeSpan.FromHours(8) }),
            new TestTenantProvider(SqlServerDatabaseFixture.OrganisationId),
            clock);
        var user = new DirectoryUser(Handler1Id, "Hannah Handler", UserRole.Handler);

        var issued = issuer.Issue(user);

        issued.ExpiresAt.ShouldBe(clock.Now.AddHours(8));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "claims-module",
            ValidAudience = "claims-module-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        });
        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        var token = (JsonWebToken)result.SecurityToken;
        token.Subject.ShouldBe(Handler1Id.ToString());
        token.GetClaim("name").Value.ShouldBe("Hannah Handler");
        token.Claims.Single(c => c.Type.EndsWith("role", StringComparison.Ordinal)).Value.ShouldBe("Handler");
        token.GetClaim("org").Value.ShouldBe(SqlServerDatabaseFixture.OrganisationId.ToString());
        token.Alg.ShouldBe(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var issued = new JwtTokenIssuer(Options.Create(new JwtOptions { SigningKey = SigningKey }), new TestTenantProvider(Guid.NewGuid()), TimeProvider.System)
            .Issue(new DirectoryUser(Handler1Id, "Hannah Handler", UserRole.Handler));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "claims-module",
            ValidAudience = "claims-module-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 48))),
        });

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    public void A_missing_or_short_signing_key_is_refused(string? key)
    {
        var issuer = new JwtTokenIssuer(Options.Create(new JwtOptions { SigningKey = key }), new TestTenantProvider(Guid.NewGuid()), TimeProvider.System);

        Should.Throw<InvalidOperationException>(() => issuer.Issue(new DirectoryUser(Handler1Id, "H", UserRole.Handler)))
            .Message.ShouldContain("at least 32 bytes");
    }

    // ------------------------------------------------------------------ Hangfire dashboard (D-41)

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    [Fact]
    public async Task Dashboard_asks_for_credentials_and_accepts_only_the_configured_ones()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { ["Hangfire:Dashboard:Password"] = "dash-p4ss" });
        using var client = factory.CreateClient();

        var anonymous = await client.GetAsync(new Uri("/hangfire", UriKind.Relative));
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.WwwAuthenticate.Single().Scheme.ShouldBe("Basic"); // makes the browser prompt
        anonymous.Headers.WwwAuthenticate.Single().Parameter.ShouldNotBeNull().ShouldContain("realm=\"Hangfire\"");

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/hangfire") { Headers = { Authorization = Basic("admin", "nope") } };
        (await client.SendAsync(wrong)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var right = new HttpRequestMessage(HttpMethod.Get, "/hangfire") { Headers = { Authorization = Basic("admin", "dash-p4ss") } };
        var ok = await client.SendAsync(right);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).ShouldContain("Hangfire");
    }

    [Fact]
    public async Task Dashboard_is_not_mapped_without_a_password_outside_development()
    {
        await using var factory = new ClaimsApiFactory(db); // Testing environment, no password
        using var client = factory.CreateClient();

        (await client.GetAsync(new Uri("/hangfire", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("Basic not-base64!!")]
    [InlineData("Basic YWRtaW4=")] // "admin" without a colon
    [InlineData("")]
    public void Malformed_authorization_headers_are_refused(string header)
    {
        var filter = new BasicAuthDashboardFilter(new HangfireDashboardOptions { Password = "dash-p4ss" });

        filter.IsAuthorized(header).ShouldBeFalse();
    }
}
