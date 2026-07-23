using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Risk.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class RiskEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public RiskEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RiskProfiles_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/risk/profiles");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanListRulesUpdateAndEvaluateRisk()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");

        var profilesResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/risk/profiles", adminToken);
        Assert.Equal(HttpStatusCode.OK, profilesResponse.StatusCode);

        var profilesPayload = await profilesResponse.Content.ReadFromJsonAsync<ApiResponse<List<RiskProfileDto>>>(IntegrationTestJson.Options);
        Assert.NotNull(profilesPayload?.Data);
        Assert.True(profilesPayload.Data.Count >= 3);

        var balanced = profilesPayload.Data.First(profile => profile.Name == "Balanced");

        var rulesResponse = await SendAuthorizedAsync(HttpMethod.Get, $"/api/v1/risk/profiles/{balanced.Id}/rules", adminToken);
        Assert.Equal(HttpStatusCode.OK, rulesResponse.StatusCode);

        var rulesPayload = await rulesResponse.Content.ReadFromJsonAsync<ApiResponse<List<RiskRuleDto>>>(IntegrationTestJson.Options);
        Assert.NotNull(rulesPayload?.Data);
        Assert.True(rulesPayload.Data.Count >= 10);

        var updateResponse = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/v1/risk/profiles/{balanced.Id}/rules",
            adminToken,
            new UpdateRiskRulesRequest
            {
                Rules =
                [
                    new UpdateRiskRuleItem
                    {
                        RuleKey = "MinConfidenceScore",
                        RuleValue = "82",
                        ValueType = SettingValueType.Decimal
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var symbolId = await GetAnySymbolIdAsync(adminToken);

        var approvedResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/risk/evaluate",
            adminToken,
            CreateEvaluationRequest(balanced.Id, symbolId, confidenceScore: 85m, emergencyStopEnabled: false));

        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        var approvedPayload = await approvedResponse.Content.ReadFromJsonAsync<ApiResponse<RiskEvaluationResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(approvedPayload?.Data);
        Assert.True(approvedPayload.Data.Approved);

        var lowConfidenceResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/risk/evaluate",
            adminToken,
            CreateEvaluationRequest(balanced.Id, symbolId, confidenceScore: 70m, emergencyStopEnabled: false));

        var lowConfidencePayload = await lowConfidenceResponse.Content.ReadFromJsonAsync<ApiResponse<RiskEvaluationResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(lowConfidencePayload?.Data);
        Assert.False(lowConfidencePayload.Data.Approved);
        Assert.Equal(RiskDecisionType.Rejected.ToString(), lowConfidencePayload.Data.Decision);

        var emergencyResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/risk/evaluate",
            adminToken,
            CreateEvaluationRequest(balanced.Id, symbolId, confidenceScore: 90m, emergencyStopEnabled: true));

        var emergencyPayload = await emergencyResponse.Content.ReadFromJsonAsync<ApiResponse<RiskEvaluationResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(emergencyPayload?.Data);
        Assert.False(emergencyPayload.Data.Approved);
        Assert.Equal(RiskDecisionType.EmergencyBlocked.ToString(), emergencyPayload.Data.Decision);
    }

    private static RiskEvaluationRequest CreateEvaluationRequest(
        long profileId,
        long symbolId,
        decimal confidenceScore,
        bool emergencyStopEnabled) => new()
    {
        RiskProfileId = profileId,
        SymbolId = symbolId,
        Direction = "Long",
        EntryPrice = 65000m,
        SuggestedStopLoss = 64500m,
        SuggestedTakeProfit = 66000m,
        ConfidenceScore = confidenceScore,
        AccountBalance = 10000m,
        SpreadPercent = 0.01m,
        AtrPercent = 1.2m,
        EmergencyStopEnabled = emergencyStopEnabled
    };

    private async Task<long> GetAnySymbolIdAsync(string adminToken)
    {
        var exchangesResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/exchanges?pageSize=5", adminToken);
        exchangesResponse.EnsureSuccessStatusCode();
        var exchangesPayload = await exchangesResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<Application.Exchanges.Dtos.ExchangeDto>>>(IntegrationTestJson.Options);
        var exchangeId = exchangesPayload?.Data?.Items.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("No exchange available for risk tests.");

        var symbolsResponse = await SendAuthorizedAsync(HttpMethod.Get, $"/api/v1/symbols?exchangeId={exchangeId}&pageSize=5", adminToken);
        symbolsResponse.EnsureSuccessStatusCode();
        var symbolsPayload = await symbolsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<Application.Symbols.Dtos.SymbolDto>>>(IntegrationTestJson.Options);
        return symbolsPayload?.Data?.Items.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("No symbol available for risk tests.");
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
