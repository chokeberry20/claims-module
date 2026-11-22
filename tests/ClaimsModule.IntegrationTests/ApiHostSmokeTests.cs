using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaimsModule.IntegrationTests;

/// <summary>The composition root boots and answers the liveness probe.</summary>
public sealed class ApiHostSmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Liveness_endpoint_returns_200()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
