using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class AuthEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public AuthEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithSeededAdmin_ReturnsToken()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = "admin@momoquant.local",
            Password = "Admin123!"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(payload);
        Assert.True(payload.Success);
        Assert.False(string.IsNullOrWhiteSpace(payload.Data?.AccessToken));
        Assert.Equal("Admin", payload.Data?.Role);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = "admin@momoquant.local",
            Password = "Admin123!"
        });

        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(loginPayload?.Data?.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginPayload.Data.AccessToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<UserProfileDto>>(IntegrationTestJson.Options);
        Assert.NotNull(payload?.Data);
        Assert.Equal("admin@momoquant.local", payload.Data.Email);
    }

    [Fact]
    public async Task Users_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
