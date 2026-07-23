using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;

namespace MomoQuant.UnitTests.Replay;

public class ReplayChartServiceTests
{
    [Fact]
    public async Task GetChartAsync_StrictMode_DoesNotIncludeFutureCandles()
    {
        var session = CreateSession(currentFrameIndex: 5, totalFrames: 20);
        var dataset = CreateDataset(totalEvaluationFrames: 20);
        var state = CreateRuntimeState(session, dataset);

        var sessionRepository = new Mock<IReplaySessionRepository>();
        sessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var frameRepository = new Mock<IReplayFrameRepository>();
        frameRepository.Setup(repo => repo.GetBySessionIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, SymbolName = "BNBUSDT", ExchangeId = 1 });

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES" });

        var service = new ReplayChartService(
            sessionRepository.Object,
            frameRepository.Object,
            new Mock<IReplayDataLoader>().Object,
            stateStore.Object,
            symbolRepository.Object,
            exchangeRepository.Object,
            new Mock<IIndicatorSnapshotRepository>().Object,
            new Mock<IRiskDecisionRepository>().Object,
            new Mock<IOrderRepository>().Object,
            new Mock<ITradeRepository>().Object,
            new Mock<IMissedOrderRepository>().Object,
            new FourHourRangeService());

        var result = await service.GetChartAsync(1, new ReplayChartQuery { IncludeFutureContext = false });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.StrictReplayMode);
        Assert.DoesNotContain(result.Data.Candles, candle => candle.IsFutureContext);
        Assert.All(result.Data.Candles, candle => Assert.True(candle.FrameIndex <= 5));
    }

    [Fact]
    public async Task GetChartAsync_IncludesIndicators_WhenSnapshotsExist()
    {
        var session = CreateSession(currentFrameIndex: 2, totalFrames: 5);
        var dataset = CreateDataset(totalEvaluationFrames: 5);
        var state = CreateRuntimeState(session, dataset);

        var sessionRepository = new Mock<IReplaySessionRepository>();
        sessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var frameRepository = new Mock<IReplayFrameRepository>();
        frameRepository.Setup(repo => repo.GetBySessionIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, SymbolName = "BNBUSDT", ExchangeId = 1 });

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES" });

        var service = new ReplayChartService(
            sessionRepository.Object,
            frameRepository.Object,
            new Mock<IReplayDataLoader>().Object,
            stateStore.Object,
            symbolRepository.Object,
            exchangeRepository.Object,
            new Mock<IIndicatorSnapshotRepository>().Object,
            new Mock<IRiskDecisionRepository>().Object,
            new Mock<IOrderRepository>().Object,
            new Mock<ITradeRepository>().Object,
            new Mock<IMissedOrderRepository>().Object,
            new FourHourRangeService());

        var result = await service.GetChartAsync(1, new ReplayChartQuery());

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Data!.Indicators);
        Assert.False(result.Data.IndicatorsMissing);
    }

    [Fact]
    public async Task GetChartAsync_ChartWindow_ReturnsLimitedCandleCount()
    {
        var session = CreateSession(currentFrameIndex: 100, totalFrames: 200);
        var dataset = CreateDataset(totalEvaluationFrames: 200);
        var state = CreateRuntimeState(session, dataset);

        var sessionRepository = new Mock<IReplaySessionRepository>();
        sessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var frameRepository = new Mock<IReplayFrameRepository>();
        frameRepository.Setup(repo => repo.GetBySessionIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, SymbolName = "BNBUSDT", ExchangeId = 1 });

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES" });

        var service = new ReplayChartService(
            sessionRepository.Object,
            frameRepository.Object,
            new Mock<IReplayDataLoader>().Object,
            stateStore.Object,
            symbolRepository.Object,
            exchangeRepository.Object,
            new Mock<IIndicatorSnapshotRepository>().Object,
            new Mock<IRiskDecisionRepository>().Object,
            new Mock<IOrderRepository>().Object,
            new Mock<ITradeRepository>().Object,
            new Mock<IMissedOrderRepository>().Object,
            new FourHourRangeService());

        var result = await service.GetChartAsync(1, new ReplayChartQuery
        {
            CurrentFrameIndex = 100,
            CandlesBefore = 150,
            CandlesAfter = 0,
            IncludeFutureContext = false
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(100, result.Data.CurrentFrameIndex);
        Assert.Equal(200, result.Data.TotalFrames);
        Assert.InRange(result.Data.Candles.Count, 1, 151);
        Assert.All(result.Data.Candles, candle => Assert.True(candle.FrameIndex <= 100));
    }

    [Fact]
    public void DeserializeStrategyResults_ReadsPersistedJson()
    {
        var json = """
                   [{"strategyCode":"EMA_PULLBACK","strategyName":"EMA Pullback","signalType":"NoTrade","direction":"None","strength":0,"confidenceContribution":0,"entryPrice":null,"suggestedStopLoss":null,"suggestedTakeProfit":null,"reason":"Market regime Ranging is not supported.","isValid":true}]
                   """;

        var results = ReplayMapper.DeserializeStrategyResultDtos(json);

        Assert.Single(results);
        Assert.Equal("EMA_PULLBACK", results[0].StrategyCode);
        Assert.Equal("NoTrade", results[0].SignalType);
    }

    private static ReplaySession CreateSession(int currentFrameIndex, int totalFrames) => new()
    {
        Id = 1,
        Name = "Test",
        TradingSessionId = 10,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        FromUtc = DateTime.UtcNow.AddDays(-1),
        ToUtc = DateTime.UtcNow,
        InitialBalance = 10_000m,
        CurrentBalance = 10_000m,
        CurrentEquity = 10_000m,
        CurrentFrameIndex = currentFrameIndex,
        TotalFrames = totalFrames,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static BacktestDataset CreateDataset(int totalEvaluationFrames)
    {
        var candles = new List<Candle>();
        var evaluationIndices = new List<int>();
        var snapshots = new Dictionary<long, IndicatorSnapshot>();
        var openTime = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < totalEvaluationFrames; i++)
        {
            var candleOpen = openTime.AddMinutes(i * 3);
            var candle = new Candle
            {
                Id = 100 + i,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M3,
                Open = 100m + i,
                High = 101m + i,
                Low = 99m + i,
                Close = 100.5m + i,
                Volume = 1000m,
                OpenTimeUtc = candleOpen,
                CloseTimeUtc = candleOpen.AddMinutes(3),
                CreatedAtUtc = DateTime.UtcNow
            };

            candles.Add(candle);
            evaluationIndices.Add(i);
            snapshots[candle.Id] = new IndicatorSnapshot
            {
                SymbolId = 1,
                Timeframe = Timeframe.M3,
                CandleId = candle.Id,
                Ema20 = 100m,
                Ema50 = 99m,
                Ema200 = 98m,
                Vwap = 100.2m,
                CalculatedAtUtc = candle.CloseTimeUtc,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        return new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BNBUSDT",
            Timeframe = Timeframe.M3,
            Candles = candles,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = evaluationIndices
        };
    }

    private static ReplayRuntimeState CreateRuntimeState(ReplaySession session, BacktestDataset dataset) => new()
    {
        Session = session,
        Settings = new ReplaySessionSettings
        {
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0005m,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 80m,
            SlippagePercent = 0m,
            ExecutionMode = ExecutionMode.MarketFill,
            StrategyIds = [1]
        },
        Symbol = new Symbol { Id = 1, SymbolName = "BNBUSDT", ExchangeId = 1 },
        RiskRules = [],
        Context = new BacktestContext
        {
            BacktestRunId = 1,
            SimulationMode = TradingMode.Replay,
            TradingSessionId = session.TradingSessionId,
            ExchangeId = session.ExchangeId,
            RiskProfileId = 1,
            Settings = new RunBacktestSettings
            {
                Name = "Test",
                SymbolIds = [1],
                Timeframes = [Timeframe.M3],
                FromUtc = session.FromUtc,
                ToUtc = session.ToUtc,
                InitialBalance = 10_000m,
                StrategyIds = [1],
                ExecutionMode = ExecutionMode.MarketFill,
                MakerFeeRate = 0.0002m,
                TakerFeeRate = 0.0005m,
                OrderExpiryCandles = 3,
                UseAiScoring = false,
                MinConfidenceScore = 80m,
                SlippagePercent = 0m
            },
            RiskRules = [],
            Strategies = [],
            Symbols = new Dictionary<long, Symbol> { [1] = new() { Id = 1, SymbolName = "BNBUSDT", ExchangeId = 1 } },
            Balance = session.CurrentBalance,
            PeakEquity = session.CurrentEquity
        },
        Dataset = dataset,
        Strategies = [],
        CurrentFrameIndex = session.CurrentFrameIndex
    };
}
