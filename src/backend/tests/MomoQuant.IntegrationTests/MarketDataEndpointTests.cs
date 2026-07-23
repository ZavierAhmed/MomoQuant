using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class MarketDataEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public MarketDataEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Candles_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/market-data/candles?symbolId=1&timeframe=3m");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanImportQuerySnapshotAndPreventDuplicates()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var exchangeId = await GetSeededExchangeIdAsync(adminToken);
        var symbolId = await EnsureBtcSymbolAsync(adminToken, exchangeId);

        var importRequest = new ImportCandlesRequest
        {
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = "3m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        var firstImportResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/market-data/candles/import",
            adminToken,
            importRequest);

        Assert.Equal(HttpStatusCode.Created, firstImportResponse.StatusCode);

        var firstImport = await firstImportResponse.Content.ReadFromJsonAsync<ApiResponse<MarketDataImportDto>>(IntegrationTestJson.Options);
        Assert.NotNull(firstImport?.Data);
        Assert.Equal(MarketDataImportStatus.Completed, firstImport.Data.Status);
        Assert.True(firstImport.Data.InsertedCount > 0 || firstImport.Data.TotalReceived > 0);

        var importStatusResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/market-data/imports/{firstImport.Data.ImportId}",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, importStatusResponse.StatusCode);

        var candlesResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/market-data/candles?symbolId={symbolId}&timeframe=3m&limit=10",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, candlesResponse.StatusCode);

        var candlesPayload = await candlesResponse.Content.ReadFromJsonAsync<ApiResponse<List<CandleDto>>>(IntegrationTestJson.Options);
        Assert.NotNull(candlesPayload?.Data);
        Assert.NotEmpty(candlesPayload.Data);

        var snapshotResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/market-data/snapshot?symbolId={symbolId}&timeframe=3m",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        var snapshotPayload = await snapshotResponse.Content.ReadFromJsonAsync<ApiResponse<MarketSnapshotDto>>(IntegrationTestJson.Options);
        Assert.NotNull(snapshotPayload?.Data);
        Assert.Equal("BTCUSDT", snapshotPayload.Data.Symbol);
        Assert.True(snapshotPayload.Data.CandleCountAvailable > 0);
        Assert.False(snapshotPayload.Data.IndicatorsAvailable);

        var secondImportResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/market-data/candles/import",
            adminToken,
            importRequest);

        Assert.Equal(HttpStatusCode.Created, secondImportResponse.StatusCode);

        var secondImport = await secondImportResponse.Content.ReadFromJsonAsync<ApiResponse<MarketDataImportDto>>(IntegrationTestJson.Options);
        Assert.NotNull(secondImport?.Data);
        Assert.Equal(0, secondImport.Data.InsertedCount);
        Assert.True(secondImport.Data.SkippedDuplicateCount > 0);
    }

    [Fact]
    public async Task Viewer_CannotImportCandles()
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

        var exchangeId = await GetSeededExchangeIdAsync(adminToken);
        var symbolId = await EnsureBtcSymbolAsync(adminToken, exchangeId);
        var viewerToken = await LoginAsync(viewerEmail, "Viewer123!");

        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/market-data/candles/import",
            viewerToken,
            new ImportCandlesRequest
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Timeframe = "3m",
                FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<long> GetSeededExchangeIdAsync(string adminToken)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/exchanges?pageSize=20", adminToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<Application.Exchanges.Dtos.ExchangeDto>>>(IntegrationTestJson.Options);
        var exchange = payload?.Data?.Items.FirstOrDefault(item => item.Code == "BINANCE_FUTURES")
            ?? payload?.Data?.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("No exchange available for market data tests.");

        return exchange.Id;
    }

    private async Task<long> EnsureBtcSymbolAsync(string adminToken, long exchangeId)
    {
        var symbolsResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/symbols?exchangeId={exchangeId}&pageSize=20&search=BTCUSDT",
            adminToken);

        symbolsResponse.EnsureSuccessStatusCode();

        var symbolsPayload = await symbolsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<Application.Symbols.Dtos.SymbolDto>>>(IntegrationTestJson.Options);
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

        symbolsPayload = await symbolsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<Application.Symbols.Dtos.SymbolDto>>>(IntegrationTestJson.Options);
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
