using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Admin.Dtos;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class AdminDataCleanupEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public AdminDataCleanupEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_RequiresAdminRole()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var viewerEmail = $"viewer_{Guid.NewGuid():N}@momoquant.local";

        await CreateUserAsync(adminToken, new CreateUserRequest
        {
            FullName = "Viewer User",
            Email = viewerEmail,
            Password = "Viewer123!",
            Role = UserRole.Viewer
        });

        var viewerToken = await LoginAsync(viewerEmail, "Viewer123!");
        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/admin/data-cleanup/fake-market-data/preview",
            viewerToken,
            new FakeMarketDataCleanupRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Execute_RejectsInvalidConfirmation()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/admin/data-cleanup/fake-market-data/execute",
            adminToken,
            new FakeMarketDataCleanupRequest
            {
                Confirmation = "WRONG",
                IncludeBacktests = true,
                IncludeReplay = true,
                IncludePaperTrading = true,
                IncludeAiDecisions = true,
                IncludeRiskDecisions = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanPreviewFakeMarketDataCleanup()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/admin/data-cleanup/fake-market-data/preview",
            adminToken,
            new FakeMarketDataCleanupRequest
            {
                IncludeBacktests = true,
                IncludeReplay = true,
                IncludePaperTrading = true,
                IncludeAiDecisions = true,
                IncludeRiskDecisions = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<FakeMarketDataCleanupPreviewDto>>(IntegrationTestJson.Options);
        Assert.NotNull(payload?.Data);
        Assert.Contains(payload.Data.Items, item => item.EntityName == "Candles");
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(IntegrationTestJson.Options);
        return payload?.Data?.AccessToken
            ?? throw new InvalidOperationException("Login did not return an access token.");
    }

    private async Task CreateUserAsync(string adminToken, CreateUserRequest request)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Post, "/api/v1/users", adminToken, request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string url,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }
}
