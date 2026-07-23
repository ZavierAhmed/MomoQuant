using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Infrastructure.MarketData;

namespace MomoQuant.UnitTests.MarketData;

public class BinanceHistoricalCandleProviderTests
{
    [Fact]
    public async Task GetCandlesAsync_Accepts4hIntervalAndSendsBinance4hQuery()
    {
        var capturedRequestUris = new List<string>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          [
                            1704067200000,
                            "42000.0",
                            "42100.0",
                            "41900.0",
                            "42050.0",
                            "100.0",
                            1704081599999,
                            "4205000.0",
                            100,
                            "50.0",
                            "2102500.0",
                            "0"
                          ]
                        ]
                        """)
                };
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://fapi.binance.com/")
        };

        var provider = new BinanceHistoricalCandleProvider(
            httpClient,
            Options.Create(new MarketDataSettings
            {
                Binance = new BinanceMarketDataSettings
                {
                    RequestDelayMs = 0,
                    Limit = 1500,
                    AllowedSymbols = ["BNBUSDT"],
                    AllowedIntervals = ["3m", "5m", "15m", "4h"]
                }
            }),
            NullLogger<BinanceHistoricalCandleProvider>.Instance);

        var fromUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = await provider.GetCandlesAsync(
            "BINANCE_FUTURES",
            "BNBUSDT",
            Timeframe.H4,
            fromUtc,
            fromUtc.AddHours(12));

        Assert.Single(candles);
        Assert.Contains(capturedRequestUris, uri => uri.Contains("interval=4h", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetCandlesAsync_RejectsUnsupportedInterval()
    {
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object)
        {
            BaseAddress = new Uri("https://fapi.binance.com/")
        };

        var provider = new BinanceHistoricalCandleProvider(
            httpClient,
            Options.Create(new MarketDataSettings
            {
                Binance = new BinanceMarketDataSettings
                {
                    RequestDelayMs = 0,
                    Limit = 1500,
                    AllowedSymbols = ["BNBUSDT"],
                    AllowedIntervals = ["3m", "5m", "15m"]
                }
            }),
            NullLogger<BinanceHistoricalCandleProvider>.Instance);

        var fromUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetCandlesAsync("BINANCE_FUTURES", "BNBUSDT", Timeframe.H4, fromUtc, fromUtc.AddHours(8)));

        Assert.Contains("Timeframe '4h' is not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCandlesAsync_RetriesWhenRateLimited()
    {
        var handler = new Mock<HttpMessageHandler>();
        var callCount = 0;

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          [
                            1704067200000,
                            "42000.0",
                            "42100.0",
                            "41900.0",
                            "42050.0",
                            "100.0",
                            1704067379999,
                            "4205000.0",
                            100,
                            "50.0",
                            "2102500.0",
                            "0"
                          ]
                        ]
                        """)
                };
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://fapi.binance.com/")
        };

        var provider = new BinanceHistoricalCandleProvider(
            httpClient,
            Options.Create(new MarketDataSettings
            {
                Binance = new BinanceMarketDataSettings
                {
                    RequestDelayMs = 0,
                    Limit = 1500,
                    AllowedSymbols = ["BTCUSDT"],
                    AllowedIntervals = ["3m"]
                }
            }),
            NullLogger<BinanceHistoricalCandleProvider>.Instance);

        var fromUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = await provider.GetCandlesAsync(
            "BINANCE_FUTURES",
            "BTCUSDT",
            Timeframe.M3,
            fromUtc,
            fromUtc.AddHours(1));

        Assert.Single(candles);
        Assert.Equal(2, callCount);
    }
}
