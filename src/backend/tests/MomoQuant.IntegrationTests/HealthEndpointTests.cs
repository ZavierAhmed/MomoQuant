using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class HealthEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
