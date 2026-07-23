using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class AiEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public AiEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AiHealth_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/ai/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DetectRegime_WithAdminToken_ReturnsFallbackOrSuccess()
    {
        var token = await LoginAsync("admin@momoquant.local", "Admin123!");
        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/ai/regime/detect",
            token,
            new DetectRegimeRequestDto
            {
                Symbol = "BTCUSDT",
                Timeframe = "3m",
                Ema20 = 100m,
                Ema50 = 99m,
                Ema200 = 95m,
                Close = 101m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<DetectRegimeResponseDto>>(IntegrationTestJson.Options);
        Assert.NotNull(payload?.Data);
        Assert.False(string.IsNullOrWhiteSpace(payload.Data.Regime));
    }

    [Fact]
    public async Task ScoreConfidence_WithAdminToken_ReturnsResponse()
    {
        var token = await LoginAsync("admin@momoquant.local", "Admin123!");
        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/ai/confidence/score",
            token,
            new ScoreConfidenceRequestDto
            {
                Symbol = "BTCUSDT",
                Timeframe = "3m",
                StrategyCode = "EMA_PULLBACK",
                SignalDirection = "Long",
                MarketRegime = "Trending",
                StrategyStrength = 72m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ScoreConfidenceResponseDto>>(IntegrationTestJson.Options);
        Assert.NotNull(payload?.Data);
        Assert.True(payload.Data.ConfidenceScore >= 0);
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
        return payload!.Data!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string url,
        string token,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }
}
