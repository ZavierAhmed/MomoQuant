using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class ExchangeSymbolEndpointTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly HttpClient _client;
    public ExchangeSymbolEndpointTests(MomoQuantWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Exchanges_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/exchanges");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateExchangeAndSyncSymbols()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");

        var createResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/exchanges",
            adminToken,
            new CreateExchangeRequest
            {
                Name = "Test Futures",
                Code = $"TEST_{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                BaseUrl = "https://fapi.binance.com",
                WebSocketUrl = "wss://fstream.binance.com"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdPayload = await createResponse.Content.ReadFromJsonAsync<ApiResponse<ExchangeDto>>(IntegrationTestJson.Options);
        Assert.NotNull(createdPayload?.Data);

        var testConnectionResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/v1/exchanges/{createdPayload.Data.Id}/test-connection",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, testConnectionResponse.StatusCode);

        var connectionPayload = await testConnectionResponse.Content
            .ReadFromJsonAsync<ApiResponse<ExchangeConnectionTestDto>>(IntegrationTestJson.Options);

        Assert.NotNull(connectionPayload?.Data);
        Assert.Equal(25, connectionPayload.Data.RestLatencyMs);
        Assert.True(connectionPayload.Data.WebSocketAvailable);

        var syncResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/symbols/sync",
            adminToken,
            new SyncSymbolsRequest { ExchangeId = createdPayload.Data.Id });

        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var syncPayload = await syncResponse.Content.ReadFromJsonAsync<ApiResponse<SymbolSyncResultDto>>(IntegrationTestJson.Options);
        Assert.NotNull(syncPayload?.Data);
        Assert.Equal(5, syncPayload.Data.CreatedCount);
        Assert.Equal(5, syncPayload.Data.TotalCount);

        var symbolsResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/symbols?exchangeId={createdPayload.Data.Id}&pageSize=20",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, symbolsResponse.StatusCode);

        var symbolsPayload = await symbolsResponse.Content
            .ReadFromJsonAsync<ApiResponse<PagedResult<SymbolDto>>>(IntegrationTestJson.Options);

        Assert.NotNull(symbolsPayload?.Data);
        Assert.Equal(5, symbolsPayload.Data.TotalCount);
        Assert.Contains(symbolsPayload.Data.Items, symbol => symbol.Symbol == "BTCUSDT");
    }

    [Fact]
    public async Task Trader_CannotCreateExchange()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var traderEmail = $"trader_{Guid.NewGuid():N}@momoquant.local";

        await CreateUserAsync(adminToken, new CreateUserRequest
        {
            FullName = "Trader User",
            Email = traderEmail,
            Password = "Trader123!",
            Role = UserRole.Trader
        });

        var traderToken = await LoginAsync(traderEmail, "Trader123!");

        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/exchanges",
            traderToken,
            new CreateExchangeRequest
            {
                Name = "Blocked Exchange",
                Code = $"BLK_{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                BaseUrl = "https://fapi.binance.com",
                WebSocketUrl = "wss://fstream.binance.com"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotSyncSymbols()
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

        var exchangesResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/exchanges?pageSize=1", adminToken);
        var exchangesPayload = await exchangesResponse.Content
            .ReadFromJsonAsync<ApiResponse<PagedResult<ExchangeDto>>>(IntegrationTestJson.Options);

        Assert.NotNull(exchangesPayload?.Data?.Items.FirstOrDefault());

        var exchangeId = exchangesPayload.Data.Items.First().Id;
        var viewerToken = await LoginAsync(viewerEmail, "Viewer123!");

        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/symbols/sync",
            viewerToken,
            new SyncSymbolsRequest { ExchangeId = exchangeId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanDeleteExchangeAndSymbols()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");

        var createResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/exchanges",
            adminToken,
            new CreateExchangeRequest
            {
                Name = "Delete Me Exchange",
                Code = $"DEL_{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                BaseUrl = "https://fapi.binance.com",
                WebSocketUrl = "wss://fstream.binance.com"
            });

        createResponse.EnsureSuccessStatusCode();
        var createdPayload = await createResponse.Content.ReadFromJsonAsync<ApiResponse<ExchangeDto>>(IntegrationTestJson.Options);
        Assert.NotNull(createdPayload?.Data);

        var syncResponse = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/v1/symbols/sync",
            adminToken,
            new SyncSymbolsRequest { ExchangeId = createdPayload.Data.Id });

        syncResponse.EnsureSuccessStatusCode();

        var deleteResponse = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/v1/exchanges/{createdPayload.Data.Id}",
            adminToken);

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var deletePayload = await deleteResponse.Content.ReadFromJsonAsync<ApiResponse<DeleteExchangeResultDto>>(IntegrationTestJson.Options);
        Assert.NotNull(deletePayload?.Data);
        Assert.Equal(5, deletePayload.Data.SymbolsDeleted);

        var getResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/exchanges/{createdPayload.Data.Id}",
            adminToken);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var symbolsResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/api/v1/symbols?exchangeId={createdPayload.Data.Id}&pageSize=20",
            adminToken);

        symbolsResponse.EnsureSuccessStatusCode();
        var symbolsPayload = await symbolsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<SymbolDto>>>(IntegrationTestJson.Options);
        Assert.NotNull(symbolsPayload?.Data);
        Assert.Equal(0, symbolsPayload.Data.TotalCount);
    }

    [Fact]
    public async Task Trader_CannotDeleteExchange()
    {
        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var traderEmail = $"trader_{Guid.NewGuid():N}@momoquant.local";

        await CreateUserAsync(adminToken, new CreateUserRequest
        {
            FullName = "Trader User",
            Email = traderEmail,
            Password = "Trader123!",
            Role = UserRole.Trader
        });

        var exchangesResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/v1/exchanges?pageSize=1", adminToken);
        var exchangesPayload = await exchangesResponse.Content
            .ReadFromJsonAsync<ApiResponse<PagedResult<ExchangeDto>>>(IntegrationTestJson.Options);

        var exchangeId = exchangesPayload!.Data!.Items.First().Id;
        var traderToken = await LoginAsync(traderEmail, "Trader123!");

        var response = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/v1/exchanges/{exchangeId}",
            traderToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
