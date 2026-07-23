using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using MomoQuant.Infrastructure.Exchanges;

namespace MomoQuant.UnitTests.Exchanges;

public class BinanceFuturesSymbolDiscoveryServiceTests
{
    private const string ExchangeInfoJson =
        """
        {
          "symbols": [
            {
              "symbol": "BTCUSDT",
              "baseAsset": "BTC",
              "quoteAsset": "USDT",
              "marginAsset": "USDT",
              "contractType": "PERPETUAL",
              "status": "TRADING",
              "pricePrecision": 2,
              "quantityPrecision": 3,
              "filters": [
                { "filterType": "PRICE_FILTER", "tickSize": "0.10" },
                { "filterType": "LOT_SIZE", "stepSize": "0.001", "minQty": "0.001" },
                { "filterType": "MIN_NOTIONAL", "notional": "5" }
              ]
            },
            {
              "symbol": "ETHUSDT",
              "baseAsset": "ETH",
              "quoteAsset": "USDT",
              "marginAsset": "USDT",
              "contractType": "PERPETUAL",
              "status": "TRADING",
              "pricePrecision": 2,
              "quantityPrecision": 3,
              "filters": []
            },
            {
              "symbol": "OLDCOINUSDT",
              "baseAsset": "OLD",
              "quoteAsset": "USDT",
              "marginAsset": "USDT",
              "contractType": "PERPETUAL",
              "status": "SETTLING",
              "pricePrecision": 2,
              "quantityPrecision": 3,
              "filters": []
            },
            {
              "symbol": "BTCUSD_PERP",
              "baseAsset": "BTC",
              "quoteAsset": "USD",
              "marginAsset": "BTC",
              "contractType": "PERPETUAL",
              "status": "TRADING",
              "pricePrecision": 1,
              "quantityPrecision": 0,
              "filters": []
            }
          ]
        }
        """;

    private const string TickerJson =
        """
        [
          { "symbol": "BTCUSDT", "lastPrice": "42000.5", "priceChangePercent": "1.5", "quoteVolume": "1000000000", "count": 500000 },
          { "symbol": "ETHUSDT", "lastPrice": "2200.25", "priceChangePercent": "-0.8", "quoteVolume": "3000000000", "count": 800000 }
        ]
        """;

    private static HttpClient BuildClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                var body = path.Contains("ticker", StringComparison.OrdinalIgnoreCase) ? TickerJson : ExchangeInfoJson;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            });

        return new HttpClient(handler.Object) { BaseAddress = new Uri("https://fapi.binance.com/") };
    }

    [Fact]
    public async Task DiscoverTopSymbolsAsync_SortsByQuoteVolumeDescendingAndExcludesNonTradable()
    {
        var service = new BinanceFuturesSymbolDiscoveryService(
            BuildClient(),
            NullLogger<BinanceFuturesSymbolDiscoveryService>.Instance);

        var results = await service.DiscoverTopSymbolsAsync(100);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.Symbol == "OLDCOINUSDT");
        Assert.DoesNotContain(results, r => r.Symbol == "BTCUSD_PERP");

        Assert.Equal("ETHUSDT", results[0].Symbol);
        Assert.Equal(1, results[0].Rank);
        Assert.Equal("BTCUSDT", results[1].Symbol);
        Assert.Equal(2, results[1].Rank);
        Assert.True(results[0].QuoteVolume24h > results[1].QuoteVolume24h);
    }

    [Fact]
    public async Task DiscoverTopSymbolsAsync_MapsTickerAndFilterMetadata()
    {
        var service = new BinanceFuturesSymbolDiscoveryService(
            BuildClient(),
            NullLogger<BinanceFuturesSymbolDiscoveryService>.Instance);

        var results = await service.DiscoverTopSymbolsAsync(100);
        var btc = results.Single(r => r.Symbol == "BTCUSDT");

        Assert.Equal("BTC", btc.BaseAsset);
        Assert.Equal("USDT", btc.QuoteAsset);
        Assert.Equal(42000.5m, btc.LastPrice);
        Assert.Equal(0.10m, btc.TickSize);
        Assert.Equal(0.001m, btc.StepSize);
        Assert.Equal(5m, btc.MinNotional);
        Assert.Equal(500000, btc.Trades24h);
    }
}
