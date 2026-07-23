using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class StrategyDiagnosticServiceTests
{
    [Fact]
    public async Task EvaluateAsync_RejectsMissingCandleId()
    {
        var service = CreateService();
        var result = await service.EvaluateAsync(new StrategyEvaluationRequest
        {
            SymbolId = 1,
            Timeframe = "3m",
            CandleId = 0,
            MarketRegime = "Trending"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Candle selection is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsCandleFromWrongTimeframe()
    {
        var candle = new Candle
        {
            Id = 10,
            SymbolId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = DateTime.UtcNow,
            CloseTimeUtc = DateTime.UtcNow
        };

        var service = CreateService(candle);
        var result = await service.EvaluateAsync(new StrategyEvaluationRequest
        {
            SymbolId = 1,
            Timeframe = "3m",
            CandleId = 10,
            MarketRegime = "Trending"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Selected candle was not found", result.ErrorMessage);
    }

    [Fact]
    public async Task EvaluateAsync_AllowsMissingIndicatorSnapshot()
    {
        var candle = CreateCandle(10, Timeframe.M3);
        var service = CreateService(candle, indicatorSnapshot: null);
        var result = await service.EvaluateAsync(new StrategyEvaluationRequest
        {
            SymbolId = 1,
            Timeframe = "3m",
            CandleId = 10,
            MarketRegime = "Trending"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(10, result.Data?.CandleId);
    }

    [Fact]
    public async Task EvaluateLatestAsync_UsesLatestCandleWithIndicatorSnapshot()
    {
        var older = CreateCandle(8, Timeframe.M3, DateTime.UtcNow.AddHours(-1));
        var latest = CreateCandle(10, Timeframe.M3, DateTime.UtcNow);
        var snapshot = CreateSnapshot(latest.Id);

        var service = CreateService(
            latest,
            indicatorSnapshot: snapshot,
            recentCandles: [older, latest],
            latestCandles: [older, latest]);

        var result = await service.EvaluateLatestAsync(new StrategyEvaluateLatestRequest
        {
            SymbolId = 1,
            Timeframe = "3m",
            MarketRegime = "Trending"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(10, result.Data?.CandleId);
    }

    private static StrategyService CreateService(
        Candle? candle = null,
        IndicatorSnapshot? indicatorSnapshot = null,
        IReadOnlyList<Candle>? recentCandles = null,
        IReadOnlyList<Candle>? latestCandles = null)
    {
        var symbol = new Symbol { Id = 1, SymbolName = "BNBUSDT", ExchangeId = 1 };
        var strategy = new Strategy
        {
            Id = 1,
            Code = StrategyCode.EmaPullback,
            Name = "EMA Pullback",
            IsEnabled = true
        };

        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        if (candle is not null)
        {
            candleRepository.Setup(repo => repo.GetByIdAsync(candle.Id, It.IsAny<CancellationToken>())).ReturnsAsync(candle);
        }

        candleRepository.Setup(repo => repo.GetRecentCandlesAsync(
                1,
                Timeframe.M3,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(recentCandles ?? (candle is null ? [] : [candle]));

        candleRepository.Setup(repo => repo.GetCandlesAsync(
                1,
                Timeframe.M3,
                null,
                null,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestCandles ?? (candle is null ? [] : [candle]));

        var indicatorRepository = new Mock<IIndicatorSnapshotRepository>();
        indicatorRepository.Setup(repo => repo.GetByKeyAsync(
                1,
                Timeframe.M3,
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicatorSnapshot);

        indicatorRepository.Setup(repo => repo.GetRecentForSymbolAsync(
                1,
                Timeframe.M3,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicatorSnapshot is null ? [] : [indicatorSnapshot]);

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(reg => reg.GetByCode(StrategyCode.EmaPullback)).Returns(new EmaPullbackStrategy());

        var engine = new Mock<IStrategyEngine>();
        engine.Setup(e => e.EvaluateAsync(It.IsAny<IReadOnlyCollection<ITradingStrategy>>(), It.IsAny<StrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var parameterProvider = new Mock<IStrategyParameterProvider>();
        parameterProvider.Setup(provider => provider.GetParametersAsync(It.IsAny<long>(), It.IsAny<Timeframe>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        return new StrategyService(
            strategyRepository.Object,
            new Mock<IStrategyParameterRepository>().Object,
            registry.Object,
            engine.Object,
            parameterProvider.Object,
            new Mock<IStrategyDataRequirementService>().Object,
            new Mock<IStrategyParameterDefinitionProvider>().Object,
            candleRepository.Object,
            indicatorRepository.Object,
            symbolRepository.Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<IAuditService>().Object);
    }

    private static Candle CreateCandle(long id, Timeframe timeframe, DateTime? openTimeUtc = null) => new()
    {
        Id = id,
        SymbolId = 1,
        Timeframe = timeframe,
        Open = 100m,
        Close = 101m,
        High = 102m,
        Low = 99m,
        Volume = 100m,
        OpenTimeUtc = openTimeUtc ?? DateTime.UtcNow,
        CloseTimeUtc = (openTimeUtc ?? DateTime.UtcNow).AddMinutes(3)
    };

    private static IndicatorSnapshot CreateSnapshot(long candleId) => new()
    {
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        CandleId = candleId,
        Ema20 = 100m,
        Ema50 = 99m,
        CalculatedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        MarketStructure = MarketStructure.Neutral
    };
}
