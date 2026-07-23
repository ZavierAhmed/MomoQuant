using Microsoft.Extensions.Options;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.MarketSituation.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Application.StrategyRecommendations;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Infrastructure.MarketData;

namespace MomoQuant.UnitTests.LiveMarket;

public class BinanceWebSocketKlineParserTests
{
    private const string SampleMessage = """
        {
          "e": "kline",
          "E": 1499644799999,
          "s": "BNBUSDT",
          "k": {
            "t": 1499040000000,
            "T": 1499644799999,
            "s": "BNBUSDT",
            "i": "3m",
            "o": "548.10",
            "c": "548.85",
            "h": "549.00",
            "l": "547.50",
            "v": "1000",
            "q": "548000",
            "n": 120,
            "x": true
          }
        }
        """;

    [Fact]
    public void BuildStreamName_UsesLowercaseSymbolAndTimeframe()
    {
        Assert.Equal("bnbusdt@kline_3m", BinanceWebSocketKlineParser.BuildStreamName("BNBUSDT", "3m"));
    }

    [Fact]
    public void TryParseKlineMessage_ParsesClosedCandle()
    {
        var parsed = BinanceWebSocketKlineParser.TryParseKlineMessage(SampleMessage, out var update);

        Assert.True(parsed);
        Assert.NotNull(update);
        Assert.Equal("BNBUSDT", update.Symbol);
        Assert.Equal(Timeframe.M3, update.Timeframe);
        Assert.Equal(548.85m, update.Close);
        Assert.True(update.IsClosed);
        Assert.Equal("BinanceWebSocket", update.Source);
    }

    [Fact]
    public void TryParseKlineMessage_ParsesIncompleteCandle()
    {
        var incomplete = SampleMessage.Replace("\"x\": true", "\"x\": false");
        var parsed = BinanceWebSocketKlineParser.TryParseKlineMessage(incomplete, out var update);

        Assert.True(parsed);
        Assert.NotNull(update);
        Assert.False(update.IsClosed);
        Assert.Equal(548.85m, update.Close);
    }

    [Fact]
    public void TryParseKlineMessage_ParsesCombinedStreamPayload()
    {
        var combined = """
            {
              "stream": "bnbusdt@kline_3m",
              "data": {
                "e": "kline",
                "E": 1499644799999,
                "s": "BNBUSDT",
                "k": {
                  "t": 1499040000000,
                  "T": 1499644799999,
                  "s": "BNBUSDT",
                  "i": "3m",
                  "o": "548.10",
                  "c": "548.85",
                  "h": "549.00",
                  "l": "547.50",
                  "v": "1000",
                  "q": "548000",
                  "n": 120,
                  "x": false
                }
              }
            }
            """;

        var parsed = BinanceWebSocketKlineParser.TryParseKlineMessage(combined, out var update);

        Assert.True(parsed);
        Assert.NotNull(update);
        Assert.Equal("BNBUSDT", update.Symbol);
        Assert.Equal(Timeframe.M3, update.Timeframe);
        Assert.False(update.IsClosed);
        Assert.Equal(548.10m, update.Open);
        Assert.Equal(548.85m, update.Close);
    }

    [Fact]
    public void TryParseKlineMessage_IgnoresSubscribeAck()
    {
        var ack = """{"result":null,"id":1}""";
        var parsed = BinanceWebSocketKlineParser.TryParseKlineMessage(ack, out var update);

        Assert.False(parsed);
        Assert.Null(update);
    }
}

public class MarketSituationServiceTests
{
    [Fact]
    public void Analyze_ReturnsUnknown_WhenSnapshotMissing()
    {
        var analysis = MarketSituationService.Analyze(null, null, null);

        Assert.Equal(MarketRegime.Unknown, analysis.MarketRegime);
        Assert.Equal(TrendDirection.Unknown, analysis.TrendDirection);
    }

    [Fact]
    public void Analyze_DetectsRanging_FromMixedEmaAlignment()
    {
        var candle = CreateCandle();
        var snapshot = new IndicatorSnapshot
        {
            SymbolId = 1,
            Timeframe = Timeframe.M3,
            CandleId = 1,
            Ema20 = 100.1m,
            Ema50 = 99.9m,
            Ema200 = 100m,
            Atr14 = 1m,
            Rsi14 = 50m,
            CalculatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var analysis = MarketSituationService.Analyze(snapshot, candle, 100m);

        Assert.Equal(MarketRegime.Ranging, analysis.MarketRegime);
    }

    [Fact]
    public void Analyze_DetectsTrending_FromEmaStack()
    {
        var candle = CreateCandle();
        var snapshot = new IndicatorSnapshot
        {
            SymbolId = 1,
            Timeframe = Timeframe.M3,
            CandleId = 1,
            Ema20 = 102m,
            Ema50 = 101m,
            Ema200 = 99m,
            Atr14 = 1m,
            Rsi14 = 55m,
            CalculatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var analysis = MarketSituationService.Analyze(snapshot, candle, 100m);

        Assert.Equal(MarketRegime.Trending, analysis.MarketRegime);
    }

    private static Candle CreateCandle() => new()
    {
        Id = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        Close = 100m,
        Volume = 1000m,
        OpenTimeUtc = DateTime.UtcNow,
        CloseTimeUtc = DateTime.UtcNow
    };
}

public class StrategyRecommendationServiceTests
{
    [Fact]
    public async Task GetCurrent_RejectsLiveMode()
    {
        var service = new StrategyRecommendationService(
            new StubMarketSituationService(),
            new Mock<IStrategyRepository>().Object,
            new Mock<ISymbolRepository>().Object,
            new Mock<IExchangeRepository>().Object);

        var result = await service.GetCurrentAsync(1, 1, "3m", "Live");

        Assert.False(result.Succeeded);
        Assert.Contains("LivePaper only", result.ErrorMessage);
    }

    [Fact]
    public async Task GetCurrent_ReturnsWarningWhenMarketSituationUnavailable()
    {
        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Symbol
        {
            Id = 1,
            ExchangeId = 1,
            SymbolName = "BNBUSDT",
            CreatedAtUtc = DateTime.UtcNow
        });

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Exchange
        {
            Id = 1,
            Name = "Binance Futures",
            Code = "BINANCE_FUTURES",
            CreatedAtUtc = DateTime.UtcNow
        });

        var service = new StrategyRecommendationService(
            new FailingMarketSituationService(),
            new Mock<IStrategyRepository>().Object,
            symbolRepository.Object,
            exchangeRepository.Object);

        var result = await service.GetCurrentAsync(1, 1, "3m", "LivePaper");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data.RecommendedStrategies);
        Assert.Contains("market data", result.Data.Warning, StringComparison.OrdinalIgnoreCase);
    }
}

public class LiveMarketBootstrapServiceTests
{
    [Fact]
    public async Task EnsureWarmupAsync_ReturnsStoredHistorical_WhenEnoughCandlesAndIndicatorsExist()
    {
        var symbol = new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BNBUSDT", CreatedAtUtc = DateTime.UtcNow };
        var exchange = new Exchange { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES", CreatedAtUtc = DateTime.UtcNow };
        var candle = new Candle
        {
            Id = 10,
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M3,
            CloseTimeUtc = DateTime.UtcNow,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(-3),
            Close = 100m,
            Open = 99m,
            High = 101m,
            Low = 98m,
            Volume = 1m,
            CreatedAtUtc = DateTime.UtcNow
        };

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.CountCandlesAsync(1, Timeframe.M3, It.IsAny<CancellationToken>())).ReturnsAsync(300);
        candleRepository.Setup(repo => repo.GetLatestCandleAsync(1, Timeframe.M3, It.IsAny<CancellationToken>())).ReturnsAsync(candle);

        var indicatorRepository = new Mock<IIndicatorSnapshotRepository>();
        indicatorRepository
            .Setup(repo => repo.GetByKeyAsync(1, Timeframe.M3, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndicatorSnapshot { SymbolId = 1, Timeframe = Timeframe.M3, CandleId = 10, CalculatedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow });

        var service = new LiveMarketBootstrapService(
            exchangeRepository.Object,
            symbolRepository.Object,
            candleRepository.Object,
            indicatorRepository.Object,
            new Mock<IHistoricalCandleProvider>().Object,
            new Mock<IIndicatorCalculationService>().Object,
            Options.Create(new MarketDataSettings()));

        var result = await service.EnsureWarmupAsync(1, 1, Timeframe.M3);

        Assert.True(result.Succeeded);
        Assert.Equal(nameof(MarketSituationDataSource.StoredHistorical), result.Data!.DataSource);
        Assert.Equal(300, result.Data.CandleCountUsed);
        Assert.True(result.Data.IndicatorsAvailable);
    }
}

file sealed class FailingMarketSituationService : IMarketSituationService
{
    public Task<ServiceResult<MarketSituationDto>> GetCurrentAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<MarketSituationDto>.Fail(
            "Could not load recent market data for BNBUSDT 3m. Check Binance public data connectivity.",
            "candles"));
}

file sealed class StubMarketSituationService : IMarketSituationService
{
    public Task<ServiceResult<MarketSituationDto>> GetCurrentAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<MarketSituationDto>.Ok(new MarketSituationDto
        {
            ExchangeName = "Binance Futures",
            Symbol = "BNBUSDT",
            Timeframe = "3m",
            MarketRegime = "Ranging",
            TrendDirection = "Neutral",
            VolatilityState = "Normal",
            MomentumState = "Neutral",
            VolumeState = "Normal",
            RiskState = "Normal",
            Summary = "Test",
            Signals = [],
            Warnings = [],
            DataSource = "StoredHistorical",
            CandleCountUsed = 300,
            IndicatorsAvailable = true
        }));
}
