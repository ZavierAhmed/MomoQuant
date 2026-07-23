using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class IndicatorEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public IndicatorEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Snapshot_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/indicators/snapshot?symbolId=1&timeframe=3m&candleId=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanRecalculateQuerySnapshotAndUpdateWithoutDuplicates()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var exchangeId = await GetSeededExchangeIdAsync(adminToken);
        var symbolId = await EnsureBtcSymbolWithCandlesAsync(adminToken, exchangeId);

        var fromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var recalculateRequest = new RecalculateIndicatorsRequest
        {
            SymbolId = symbolId,
            Timeframe = "3m",
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

        var firstRecalcResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/indicators/recalculate",
            adminToken,
            recalculateRequest);

        Assert.Equal(HttpStatusCode.OK, firstRecalcResponse.StatusCode);

        var firstRecalc = await firstRecalcResponse.Content.ReadFromJsonAsync<ApiResponse<RecalculateIndicatorsResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(firstRecalc?.Data);
        Assert.Equal("Completed", firstRecalc.Data.Status);
        Assert.True(firstRecalc.Data.CandlesProcessed > 0);
        Assert.True(firstRecalc.Data.SnapshotsInserted > 0 || firstRecalc.Data.SnapshotsUpdated > 0);

        var candlesResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/market-data/candles?symbolId={symbolId}&timeframe=3m&fromUtc={fromUtc:O}&toUtc={toUtc:O}&limit=1",
            adminToken);

        candlesResponse.EnsureSuccessStatusCode();
        var candlesPayload = await candlesResponse.Content.ReadFromJsonAsync<ApiResponse<List<CandleDto>>>(IntegrationTestJson.Options);
        var candleId = candlesPayload?.Data?.First().Id
            ?? throw new InvalidOperationException("Expected at least one candle for snapshot query.");

        var snapshotResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/indicators/snapshot?symbolId={symbolId}&timeframe=3m&candleId={candleId}",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        var snapshotPayload = await snapshotResponse.Content.ReadFromJsonAsync<ApiResponse<IndicatorSnapshotDto>>(IntegrationTestJson.Options);
        Assert.NotNull(snapshotPayload?.Data);
        Assert.Equal(candleId, snapshotPayload.Data.CandleId);

        var secondRecalcResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/indicators/recalculate",
            adminToken,
            recalculateRequest);

        Assert.Equal(HttpStatusCode.OK, secondRecalcResponse.StatusCode);

        var secondRecalc = await secondRecalcResponse.Content.ReadFromJsonAsync<ApiResponse<RecalculateIndicatorsResponse>>(IntegrationTestJson.Options);
        Assert.NotNull(secondRecalc?.Data);
        Assert.Equal(0, secondRecalc.Data.SnapshotsInserted);
        Assert.True(secondRecalc.Data.SnapshotsUpdated > 0);
    }

    private async Task<long> GetSeededExchangeIdAsync(string adminToken)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/exchanges?pageSize=20", adminToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<Application.Exchanges.Dtos.ExchangeDto>>>(IntegrationTestJson.Options);
        var exchange = payload?.Data?.Items.FirstOrDefault(item => item.Code == "BINANCE_FUTURES")
            ?? payload?.Data?.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("No exchange available for indicator tests.");

        return exchange.Id;
    }

    private async Task<long> EnsureBtcSymbolWithCandlesAsync(string adminToken, long exchangeId)
    {
        var symbolId = await EnsureBtcSymbolAsync(adminToken, exchangeId);

        var importRequest = new ImportCandlesRequest
        {
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = "3m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        var importResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/market-data/candles/import",
            adminToken,
            importRequest);

        importResponse.EnsureSuccessStatusCode();
        return symbolId;
    }

    private async Task<long> EnsureBtcSymbolAsync(string adminToken, long exchangeId)
    {
        var symbolsResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/symbols?exchangeId={exchangeId}&pageSize=20&search=BTCUSDT",
            adminToken);

        symbolsResponse.EnsureSuccessStatusCode();

        var symbolsPayload = await symbolsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<SymbolDto>>>(IntegrationTestJson.Options);
        var existing = symbolsPayload?.Data?.Items.FirstOrDefault(symbol => symbol.Symbol == "BTCUSDT");
        if (existing is not null)
        {
            return existing.Id;
        }

        var syncResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/symbols/sync",
            adminToken,
            new SyncSymbolsRequest { ExchangeId = exchangeId });

        syncResponse.EnsureSuccessStatusCode();

        symbolsResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/symbols?exchangeId={exchangeId}&pageSize=20&search=BTCUSDT",
            adminToken);

        symbolsPayload = await symbolsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<SymbolDto>>>(IntegrationTestJson.Options);
        return symbolsPayload?.Data?.Items.First(symbol => symbol.Symbol == "BTCUSDT").Id
            ?? throw new InvalidOperationException("BTCUSDT symbol was not found after sync.");
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
