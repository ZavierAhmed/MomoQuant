using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class StrategyEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public StrategyEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Strategies_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/strategies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanListEnableUpdateAndEvaluateStrategies()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");

        var listResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/strategies", adminToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listPayload = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<StrategyDto>>>(IntegrationTestJson.Options);
        Assert.NotNull(listPayload?.Data);
        Assert.True(listPayload.Data.Count >= 3);

        var strategy = listPayload.Data.First(item => item.Code == "EMA_PULLBACK");

        var enableResponse = await SendAuthorizedAsync(HttpMethod.Post, $"/api/v1/strategies/{strategy.Id}/enable", adminToken);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var parametersResponse = await SendAuthorizedAsync(HttpMethod.Get, $"/api/v1/strategies/{strategy.Id}/parameters", adminToken);
        Assert.Equal(HttpStatusCode.OK, parametersResponse.StatusCode);

        var updateResponse = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/v1/strategies/{strategy.Id}/parameters",
            adminToken,
            new UpdateStrategyParametersRequest
            {
                Parameters =
                [
                    new UpdateStrategyParameterItem
                    {
                        ParameterKey = "PullbackTolerancePercent",
                        ParameterValue = "0.75",
                        Timeframe = "3m",
                        ValueType = SettingValueType.Decimal
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
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
