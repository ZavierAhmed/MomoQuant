using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Infrastructure.Ai;

namespace MomoQuant.UnitTests.Ai;

public class AiServiceClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetHealthAsync_MapsHealthResponse()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            status = "healthy",
            service = "momo-ai",
            version = "1.0.0"
        }));

        var client = CreateClient(handler);
        var result = await client.GetHealthAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("healthy", result.Data.Status);
        Assert.Equal("momo-ai", result.Data.Service);
        Assert.Equal("1.0.0", result.Data.Version);
    }

    [Fact]
    public async Task DetectRegimeAsync_MapsRegimeResponse()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            regime = "Trending",
            confidence = 82,
            reasons = new[] { "EMA alignment supports trend." }
        }));

        var client = CreateClient(handler);
        var result = await client.DetectRegimeAsync(new DetectRegimeRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Trending", result.Data.Regime);
        Assert.Equal(82, result.Data.Confidence);
        Assert.Single(result.Data.Reasons);
    }

    [Fact]
    public async Task ScoreConfidenceAsync_MapsConfidenceResponse()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            confidenceScore = 76,
            classification = "High",
            reasons = new[] { "Strategy matches market regime." },
            warnings = new[] { "Spread is elevated." }
        }));

        var client = CreateClient(handler);
        var result = await client.ScoreConfidenceAsync(new ScoreConfidenceRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m",
            StrategyCode = "EMA_PULLBACK",
            SignalDirection = "Long",
            MarketRegime = "Trending",
            StrategyStrength = 70m
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(76, result.Data.ConfidenceScore);
        Assert.Equal("High", result.Data.Classification);
        Assert.Single(result.Data.Warnings);
    }

    [Fact]
    public async Task DetectRegimeAsync_ReturnsFailure_WhenServiceUnavailable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var client = CreateClient(handler);

        var result = await client.DetectRegimeAsync(new DetectRegimeRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("AI service is unavailable.", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectRegimeAsync_ReturnsFailure_OnTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = CreateClient(handler, timeoutSeconds: 1);

        var result = await client.DetectRegimeAsync(new DetectRegimeRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("AI service request timed out.", result.ErrorMessage);
    }

    private static AiServiceClient CreateClient(HttpMessageHandler handler, int timeoutSeconds = 10)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8001/"),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        var options = Options.Create(new AiIntegrationOptions
        {
            BaseUrl = "http://127.0.0.1:8001",
            TimeoutSeconds = timeoutSeconds,
            EnableFallback = true
        });

        return new AiServiceClient(httpClient, options, NullLogger<AiServiceClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
