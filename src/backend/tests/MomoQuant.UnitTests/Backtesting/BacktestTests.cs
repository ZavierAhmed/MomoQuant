using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.Trades;

namespace MomoQuant.UnitTests.Backtesting;

public class BacktestMetricsCalculatorTests
{
    private readonly BacktestMetricsCalculator _calculator = new();

    [Fact]
    public void Calculate_ComputesWinRateCorrectly()
    {
        var context = CreateContext();
        context.Trades.Add(CreateClosedTrade(100m));
        context.Trades.Add(CreateClosedTrade(-50m));
        context.Trades.Add(CreateClosedTrade(25m));

        var result = _calculator.Calculate(context, backtestRunId: 1);

        Assert.Equal(3, result.TotalTrades);
        Assert.Equal(2, result.WinningTrades);
        Assert.Equal(1, result.LosingTrades);
        Assert.Equal(66.666666666666666666666666667m, result.WinRatePercent, 10);
    }

    [Fact]
    public void Calculate_ComputesMaxDrawdownFromEquityPoints()
    {
        var context = CreateContext();
        context.MaxDrawdown = 250m;
        context.MaxDrawdownPercent = 2.5m;

        var result = _calculator.Calculate(context, backtestRunId: 1);

        Assert.Equal(250m, result.MaxDrawdown);
        Assert.Equal(2.5m, result.MaxDrawdownPercent);
    }

    [Fact]
    public void Calculate_ProfitFactorHandlesZeroGrossLossSafely()
    {
        var context = CreateContext();
        context.Trades.Add(CreateClosedTrade(120m));
        context.Balance = 10_120m;

        var result = _calculator.Calculate(context, backtestRunId: 1);

        Assert.Equal(999m, result.ProfitFactor);
    }

    private static BacktestContext CreateContext(ExecutionMode executionMode = ExecutionMode.MarketFill) => new()
    {
        BacktestRunId = 1,
        TradingSessionId = 1,
        ExchangeId = 1,
        RiskProfileId = 1,
        Settings = new RunBacktestSettings
        {
            Name = "Test",
            SymbolIds = [1],
            Timeframes = [Timeframe.M3],
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow,
            InitialBalance = 10_000m,
            StrategyIds = [1],
            ExecutionMode = executionMode,
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0005m,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 80m,
            SlippagePercent = 0m
        },
        RiskRules = [],
        Strategies = [],
        Symbols = new Dictionary<long, global::MomoQuant.Domain.Exchanges.Symbol>(),
        Balance = 10_000m,
        PeakEquity = 10_000m
    };

    private static Trade CreateClosedTrade(decimal netPnl) => new()
    {
        TradingSessionId = 1,
        SymbolId = 1,
        Direction = TradeDirection.Long,
        EntryPrice = 100m,
        ExitPrice = 101m,
        Quantity = 1m,
        StopLoss = 95m,
        TakeProfit = 110m,
        Status = TradeStatus.Closed,
        OpenedAtUtc = DateTime.UtcNow.AddHours(-1),
        ClosedAtUtc = DateTime.UtcNow,
        NetPnl = netPnl,
        GrossPnl = netPnl,
        Fees = 0m,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}

public class SimulatedExecutionProviderTests
{
    private readonly SimulatedExecutionProvider _provider = new();

    [Fact]
    public void MarketFill_FillsAtNextCandleOpen()
    {
        var context = CreateContext();
        var candles = CreateCandles();
        var signal = CreateSignal();

        _provider.SubmitMarketFill(context, new PendingMarketFill
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            TakerFeeRate = 0.001m,
            SlippagePercent = 0m,
            Signal = signal,
            FillAtCandleIndex = 1,
            RequestedAtUtc = candles[0].CloseTimeUtc
        });

        _provider.ProcessPendingMarketFills(context, candles, 1);

        Assert.Single(context.OpenPositions);
        Assert.Equal(101m, context.OpenPositions[0].EntryPrice);
        Assert.Equal(TradingMode.Backtest, context.Orders[0].Mode);
    }

    [Fact]
    public void MakerOnly_LogsMissedOrderWhenPriceDoesNotTouch()
    {
        var context = CreateContext();
        var candles = CreateCandles();
        var order = new Order
        {
            TradingSessionId = 1,
            SymbolId = 1,
            Mode = TradingMode.Backtest,
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            PositionSide = PositionSide.Long,
            Price = 50m,
            Quantity = 1m,
            Status = OrderStatus.Open,
            IsPostOnly = true,
            TimeInForce = TimeInForce.Gtc,
            RequestedAtUtc = candles[0].CloseTimeUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _provider.SubmitMakerOrder(context, new PendingMakerOrder
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            LimitPrice = 50m,
            Quantity = 1m,
            StopLoss = 45m,
            TakeProfit = 60m,
            MakerFeeRate = 0.0002m,
            Signal = CreateSignal(),
            PlacedAtCandleIndex = 0,
            ExpiryCandleIndex = 1,
            RequestedAtUtc = candles[0].CloseTimeUtc,
            Order = order,
            ExecutionMode = ExecutionMode.MakerOnly
        });

        _provider.ProcessPendingMakerOrders(context, candles[1], 1);

        Assert.Empty(context.OpenPositions);
        Assert.Single(context.MissedOrderLinks);
        Assert.Equal(1, context.MissedOrders);
    }

    [Fact]
    public void StopLoss_ClosesTrade()
    {
        var context = CreateContext();
        var candle = new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M3,
            Open = 100m,
            High = 101m,
            Low = 94m,
            Close = 95m,
            OpenTimeUtc = DateTime.UtcNow,
            CloseTimeUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var entryOrder = new Order
        {
            TradingSessionId = 1,
            SymbolId = 1,
            Mode = TradingMode.Backtest,
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            PositionSide = PositionSide.Long,
            Price = 100m,
            Quantity = 1m,
            Status = OrderStatus.Filled,
            TimeInForce = TimeInForce.Gtc,
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var trade = new Trade
        {
            TradingSessionId = 1,
            SymbolId = 1,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            Status = TradeStatus.Open,
            OpenedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.OpenPositions.Add(new SimulatedPosition
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            EntryFees = 0.1m,
            OpenedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            Signal = CreateSignal(),
            EntryOrder = entryOrder,
            EntryFill = new OrderFill
            {
                FillPrice = 100m,
                FillQuantity = 1m,
                Fee = 0.1m,
                FeeAsset = "USDT",
                LiquidityType = LiquidityType.SimulatedTaker,
                FilledAtUtc = DateTime.UtcNow.AddMinutes(-3),
                CreatedAtUtc = DateTime.UtcNow
            },
            Trade = trade
        });

        _provider.UpdateOpenPositions(context, candle);

        Assert.Empty(context.OpenPositions);
        Assert.Equal(TradeStatus.Closed, trade.Status);
        Assert.Equal(CloseReason.StopLoss, trade.CloseReason);
    }

    [Fact]
    public void MakerOnly_FillsWhenPriceTouchesLimit()
    {
        var context = CreateContext(ExecutionMode.MakerOnly);
        var candles = CreateCandles();
        var order = new Order
        {
            TradingSessionId = 1,
            SymbolId = 1,
            Mode = TradingMode.Backtest,
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            PositionSide = PositionSide.Long,
            Price = 100m,
            Quantity = 1m,
            Status = OrderStatus.Open,
            IsPostOnly = true,
            TimeInForce = TimeInForce.Gtc,
            RequestedAtUtc = candles[0].CloseTimeUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _provider.SubmitMakerOrder(context, new PendingMakerOrder
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            LimitPrice = 100m,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            MakerFeeRate = 0.0002m,
            Signal = CreateSignal(),
            PlacedAtCandleIndex = 0,
            ExpiryCandleIndex = 5,
            RequestedAtUtc = candles[0].CloseTimeUtc,
            Order = order,
            ExecutionMode = ExecutionMode.MakerOnly
        });

        var touchCandle = new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M3,
            Open = 101m,
            High = 103m,
            Low = 99m,
            Close = 102m,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(-3),
            CloseTimeUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        _provider.ProcessPendingMakerOrders(context, touchCandle, 1);

        Assert.Single(context.OpenPositions);
        Assert.Equal(100m, context.OpenPositions[0].EntryPrice);
        Assert.All(context.Orders, o => Assert.Equal(TradingMode.Backtest, o.Mode));
    }

    [Fact]
    public void Fees_ReduceBalanceOnEntry()
    {
        var context = CreateContext();
        var candles = CreateCandles();
        var initialBalance = context.Balance;

        _provider.SubmitMarketFill(context, new PendingMarketFill
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            TakerFeeRate = 0.01m,
            SlippagePercent = 0m,
            Signal = CreateSignal(),
            FillAtCandleIndex = 1,
            RequestedAtUtc = candles[0].CloseTimeUtc
        });

        _provider.ProcessPendingMarketFills(context, candles, 1);

        var expectedFee = 101m * 1m * 0.01m;
        Assert.Equal(initialBalance - expectedFee, context.Balance);
        Assert.Equal(expectedFee, context.TotalFees);
    }

    [Fact]
    public void TakeProfit_ClosesTrade()
    {
        var context = CreateContext();
        var candle = new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M3,
            Open = 108m,
            High = 111m,
            Low = 107m,
            Close = 110m,
            OpenTimeUtc = DateTime.UtcNow,
            CloseTimeUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var trade = new Trade
        {
            TradingSessionId = 1,
            SymbolId = 1,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            Status = TradeStatus.Open,
            OpenedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.OpenPositions.Add(new SimulatedPosition
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            EntryFees = 0.1m,
            OpenedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            Signal = CreateSignal(),
            EntryOrder = new Order
            {
                TradingSessionId = 1,
                SymbolId = 1,
                Mode = TradingMode.Backtest,
                Side = OrderSide.Buy,
                OrderType = OrderType.Market,
                PositionSide = PositionSide.Long,
                Price = 100m,
                Quantity = 1m,
                Status = OrderStatus.Filled,
                TimeInForce = TimeInForce.Gtc,
                RequestedAtUtc = DateTime.UtcNow.AddMinutes(-3),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            EntryFill = new OrderFill
            {
                FillPrice = 100m,
                FillQuantity = 1m,
                Fee = 0.1m,
                FeeAsset = "USDT",
                LiquidityType = LiquidityType.SimulatedTaker,
                FilledAtUtc = DateTime.UtcNow.AddMinutes(-3),
                CreatedAtUtc = DateTime.UtcNow
            },
            Trade = trade
        });

        _provider.UpdateOpenPositions(context, candle);

        Assert.Empty(context.OpenPositions);
        Assert.Equal(TradeStatus.Closed, trade.Status);
        Assert.Equal(CloseReason.TakeProfit, trade.CloseReason);
    }

    [Fact]
    public void Orders_AreMarkedAsBacktestMode()
    {
        var context = CreateContext();
        var candles = CreateCandles();

        _provider.SubmitMarketFill(context, new PendingMarketFill
        {
            SymbolId = 1,
            StrategyId = 1,
            StrategyCode = StrategyCode.EmaPullback,
            Timeframe = Timeframe.M3,
            Direction = TradeDirection.Long,
            Quantity = 1m,
            StopLoss = 95m,
            TakeProfit = 110m,
            TakerFeeRate = 0.001m,
            SlippagePercent = 0m,
            Signal = CreateSignal(),
            FillAtCandleIndex = 1,
            RequestedAtUtc = candles[0].CloseTimeUtc
        });

        _provider.ProcessPendingMarketFills(context, candles, 1);

        Assert.NotEmpty(context.Orders);
        Assert.All(context.Orders, order => Assert.Equal(TradingMode.Backtest, order.Mode));
    }

    private static BacktestContext CreateContext(ExecutionMode executionMode = ExecutionMode.MarketFill) => new()
    {
        BacktestRunId = 1,
        TradingSessionId = 1,
        ExchangeId = 1,
        RiskProfileId = 1,
        Settings = new RunBacktestSettings
        {
            Name = "Test",
            SymbolIds = [1],
            Timeframes = [Timeframe.M3],
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow,
            InitialBalance = 10_000m,
            StrategyIds = [1],
            ExecutionMode = executionMode,
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0005m,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 80m,
            SlippagePercent = 0m
        },
        RiskRules = [],
        Strategies = [],
        Symbols = new Dictionary<long, global::MomoQuant.Domain.Exchanges.Symbol>(),
        Balance = 10_000m,
        PeakEquity = 10_000m
    };

    private static IReadOnlyList<Candle> CreateCandles() =>
    [
        new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M3,
            Open = 100m,
            High = 102m,
            Low = 99m,
            Close = 101m,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(-6),
            CloseTimeUtc = DateTime.UtcNow.AddMinutes(-3),
            CreatedAtUtc = DateTime.UtcNow
        },
        new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M3,
            Open = 101m,
            High = 103m,
            Low = 100m,
            Close = 102m,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(-3),
            CloseTimeUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        }
    ];

    private static StrategySignal CreateSignal() => new()
    {
        TradingSessionId = 1,
        StrategyId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        SignalType = SignalType.Entry,
        Direction = TradeDirection.Long,
        Strength = 75m,
        ConfidenceContribution = 75m,
        Reason = "Test signal",
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class BacktestDataLoaderTests
{
    [Fact]
    public async Task LoadSymbolTimeframeAsync_ReturnsNullWhenNoCandles()
    {
        var symbol = new MomoQuant.Domain.Exchanges.Symbol
        {
            Id = 1,
            ExchangeId = 1,
            SymbolName = "BTCUSDT"
        };

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetCandlesChronologicalAsync(
                1,
                Timeframe.M3,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Candle>());

        var snapshotRepository = new Mock<IIndicatorSnapshotRepository>();

        var loader = new BacktestDataLoader(
            candleRepository.Object,
            snapshotRepository.Object,
            symbolRepository.Object);

        var result = await loader.LoadSymbolTimeframeAsync(
            1,
            1,
            Timeframe.M3,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            warmUpCount: 200);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadSymbolTimeframeAsync_EvaluationIndicesAreChronological()
    {
        var fromUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 2, 0, 9, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            CreateCandle(1, fromUtc.AddMinutes(-3)),
            CreateCandle(2, fromUtc),
            CreateCandle(3, fromUtc.AddMinutes(3)),
            CreateCandle(4, fromUtc.AddMinutes(6))
        };

        var symbol = new MomoQuant.Domain.Exchanges.Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" };
        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetCandlesChronologicalAsync(
                1,
                Timeframe.M3,
                fromUtc,
                toUtc,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var snapshotRepository = new Mock<IIndicatorSnapshotRepository>();
        snapshotRepository.Setup(repo => repo.GetByCandleIdsAsync(
                1,
                Timeframe.M3,
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<long, IndicatorSnapshot>());

        var loader = new BacktestDataLoader(
            candleRepository.Object,
            snapshotRepository.Object,
            symbolRepository.Object);

        var result = await loader.LoadSymbolTimeframeAsync(1, 1, Timeframe.M3, fromUtc, toUtc, warmUpCount: 1);

        Assert.NotNull(result);
        Assert.Equal([1, 2, 3], result!.EvaluationIndices);
        Assert.True(result.Candles[result.EvaluationIndices[0]].OpenTimeUtc <= result.Candles[result.EvaluationIndices[^1]].OpenTimeUtc);
    }

    private static Candle CreateCandle(long id, DateTime openTimeUtc) => new()
    {
        Id = id,
        SymbolId = 1,
        ExchangeId = 1,
        Timeframe = Timeframe.M3,
        Open = 100m,
        High = 101m,
        Low = 99m,
        Close = 100.5m,
        OpenTimeUtc = openTimeUtc,
        CloseTimeUtc = openTimeUtc.AddMinutes(3),
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class BacktestEngineTests
{
    [Fact]
    public async Task RunDatasetAsync_ProcessesCandlesInChronologicalOrder()
    {
        var processedIndices = new List<int>();
        var executionProvider = new Mock<ISimulatedExecutionProvider>();
        executionProvider.Setup(provider => provider.ProcessPendingMarketFills(It.IsAny<BacktestContext>(), It.IsAny<IReadOnlyList<Candle>>(), It.IsAny<int>()))
            .Callback<BacktestContext, IReadOnlyList<Candle>, int>((_, _, index) => processedIndices.Add(index));
        executionProvider.Setup(provider => provider.ProcessPendingMakerOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));
        executionProvider.Setup(provider => provider.UpdateOpenPositions(It.IsAny<BacktestContext>(), It.IsAny<Candle>()));
        executionProvider.Setup(provider => provider.FinalizePendingOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));

        var strategyEngine = new Mock<IStrategyEngine>();
        strategyEngine.Setup(engine => engine.EvaluateAsync(It.IsAny<IReadOnlyCollection<ITradingStrategy>>(), It.IsAny<StrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StrategyEvaluationResult>());

        var engine = CreateEngine(strategyEngine.Object, executionProvider.Object);
        var dataset = CreateDataset(evaluationIndices: [1, 2, 3]);
        var context = CreateEngineContext();

        await engine.RunDatasetAsync(context, dataset, CreatePreparedStrategies(), CancellationToken.None);

        Assert.Equal([1, 2, 3], processedIndices);
    }

    [Fact]
    public async Task RunDatasetAsync_RiskRejection_PreventsSimulatedOrder()
    {
        var executionProvider = new Mock<ISimulatedExecutionProvider>();
        executionProvider.Setup(provider => provider.ProcessPendingMarketFills(It.IsAny<BacktestContext>(), It.IsAny<IReadOnlyList<Candle>>(), It.IsAny<int>()));
        executionProvider.Setup(provider => provider.ProcessPendingMakerOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));
        executionProvider.Setup(provider => provider.UpdateOpenPositions(It.IsAny<BacktestContext>(), It.IsAny<Candle>()));
        executionProvider.Setup(provider => provider.FinalizePendingOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));

        var strategyEngine = new Mock<IStrategyEngine>();
        strategyEngine.Setup(engine => engine.EvaluateAsync(It.IsAny<IReadOnlyCollection<ITradingStrategy>>(), It.IsAny<StrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StrategyEvaluationResult
                {
                    StrategyCode = StrategyCode.EmaPullback.ToCode(),
                    StrategyName = "EMA Pullback",
                    Evaluated = true,
                    Skipped = false,
                    SignalType = SignalType.Entry,
                    Direction = TradeDirection.Long,
                    Strength = 90m,
                    ConfidenceContribution = 90m,
                    Reason = "Test entry",
                    IsValid = true,
                    EntryPrice = 100m,
                    SuggestedStopLoss = 95m,
                    SuggestedTakeProfit = 110m
                }
            });

        var riskEngine = new Mock<IRiskEngine>();
        riskEngine.Setup(engine => engine.Evaluate(It.IsAny<RiskContext>()))
            .Returns(RiskEvaluationResult.Reject("MAX_OPEN_POSITIONS", "Maximum open positions reached."));

        var engine = CreateEngine(strategyEngine.Object, executionProvider.Object, riskEngine.Object);
        var context = CreateEngineContext();
        var dataset = CreateDataset(evaluationIndices: [1]);

        await engine.RunDatasetAsync(context, dataset, CreatePreparedStrategies(), CancellationToken.None);

        executionProvider.Verify(provider => provider.SubmitMarketFill(It.IsAny<BacktestContext>(), It.IsAny<PendingMarketFill>()), Times.Never);
        Assert.Equal(1, context.RejectedSignals);
        Assert.Single(context.RiskDecisions);
    }

    [Fact]
    public async Task RunDatasetAsync_LowConfidence_PreventsTradeWhenConfigured()
    {
        var executionProvider = new Mock<ISimulatedExecutionProvider>();
        executionProvider.Setup(provider => provider.ProcessPendingMarketFills(It.IsAny<BacktestContext>(), It.IsAny<IReadOnlyList<Candle>>(), It.IsAny<int>()));
        executionProvider.Setup(provider => provider.ProcessPendingMakerOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));
        executionProvider.Setup(provider => provider.UpdateOpenPositions(It.IsAny<BacktestContext>(), It.IsAny<Candle>()));
        executionProvider.Setup(provider => provider.FinalizePendingOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));

        var strategyEngine = new Mock<IStrategyEngine>();
        strategyEngine.Setup(engine => engine.EvaluateAsync(It.IsAny<IReadOnlyCollection<ITradingStrategy>>(), It.IsAny<StrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StrategyEvaluationResult
                {
                    StrategyCode = StrategyCode.EmaPullback.ToCode(),
                    StrategyName = "EMA Pullback",
                    Evaluated = true,
                    Skipped = false,
                    SignalType = SignalType.Entry,
                    Direction = TradeDirection.Long,
                    Strength = 50m,
                    ConfidenceContribution = 50m,
                    Reason = "Weak entry",
                    IsValid = true,
                    EntryPrice = 100m,
                    SuggestedStopLoss = 95m,
                    SuggestedTakeProfit = 110m
                }
            });

        var engine = CreateEngine(strategyEngine.Object, executionProvider.Object, new RiskEngine(new PositionSizingService()));
        var context = CreateEngineContext(CreateMinConfidenceRiskRules(80m));
        var dataset = CreateDataset(evaluationIndices: [1]);

        await engine.RunDatasetAsync(context, dataset, CreatePreparedStrategies(), CancellationToken.None);

        executionProvider.Verify(provider => provider.SubmitMarketFill(It.IsAny<BacktestContext>(), It.IsAny<PendingMarketFill>()), Times.Never);
        Assert.Equal(1, context.RejectedSignals);
        Assert.Equal(1, context.ConfidenceRejected);
    }

    [Fact]
    public async Task RunDatasetAsync_DoesNotCallRealExecutionProvider()
    {
        var realProvider = new SimulatedExecutionProvider();
        var executionProvider = new Mock<ISimulatedExecutionProvider>();
        executionProvider.Setup(provider => provider.ProcessPendingMarketFills(It.IsAny<BacktestContext>(), It.IsAny<IReadOnlyList<Candle>>(), It.IsAny<int>()))
            .Callback<BacktestContext, IReadOnlyList<Candle>, int>(realProvider.ProcessPendingMarketFills);
        executionProvider.Setup(provider => provider.ProcessPendingMakerOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));
        executionProvider.Setup(provider => provider.UpdateOpenPositions(It.IsAny<BacktestContext>(), It.IsAny<Candle>()));
        executionProvider.Setup(provider => provider.FinalizePendingOrders(It.IsAny<BacktestContext>(), It.IsAny<Candle>(), It.IsAny<int>()));

        var strategyEngine = new Mock<IStrategyEngine>();
        strategyEngine.Setup(engine => engine.EvaluateAsync(It.IsAny<IReadOnlyCollection<ITradingStrategy>>(), It.IsAny<StrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StrategyEvaluationResult>());

        var engine = CreateEngine(strategyEngine.Object, executionProvider.Object);
        await engine.RunDatasetAsync(CreateEngineContext(), CreateDataset([1]), CreatePreparedStrategies(), CancellationToken.None);

        executionProvider.Verify(provider => provider.ProcessPendingMarketFills(It.IsAny<BacktestContext>(), It.IsAny<IReadOnlyList<Candle>>(), It.IsAny<int>()), Times.AtLeastOnce);
    }

    private static BacktestEngine CreateEngine(
        IStrategyEngine strategyEngine,
        ISimulatedExecutionProvider executionProvider,
        IRiskEngine? riskEngine = null)
    {
        var parameterProvider = new Mock<IStrategyParameterProvider>();
        parameterProvider.Setup(provider => provider.GetParametersAsync(It.IsAny<long>(), It.IsAny<Timeframe>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var aiIntegration = new Mock<IAiIntegrationService>();
        var risk = riskEngine ?? CreateApprovingRiskEngine().Object;

        return new BacktestEngine(
            strategyEngine,
            parameterProvider.Object,
            risk,
            aiIntegration.Object,
            executionProvider);
    }

    private static Mock<IRiskEngine> CreateApprovingRiskEngine()
    {
        var riskEngine = new Mock<IRiskEngine>();
        riskEngine.Setup(engine => engine.Evaluate(It.IsAny<RiskContext>()))
            .Returns(new RiskEvaluationResult
            {
                Decision = RiskDecisionType.Approved,
                Reason = "Approved",
                PositionSize = 1m,
                StopLoss = 95m,
                TakeProfit = 110m,
                ApprovedRiskPercent = 1m
            });
        return riskEngine;
    }

    private static BacktestContext CreateEngineContext(IReadOnlyList<RiskRule>? riskRules = null) => new()
    {
        BacktestRunId = 1,
        TradingSessionId = 1,
        ExchangeId = 1,
        RiskProfileId = 1,
        Settings = new RunBacktestSettings
        {
            Name = "Engine Test",
            SymbolIds = [1],
            Timeframes = [Timeframe.M3],
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow,
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
        RiskRules = riskRules ?? [],
        Strategies = [],
        Symbols = new Dictionary<long, MomoQuant.Domain.Exchanges.Symbol>(),
        Balance = 10_000m,
        PeakEquity = 10_000m
    };

    private static IReadOnlyList<RiskRule> CreateMinConfidenceRiskRules(decimal minConfidence) =>
    [
        new RiskRule
        {
            RuleKey = RiskRuleKeys.MinConfidenceScore,
            RuleValue = minConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ValueType = SettingValueType.Decimal,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        },
        new RiskRule
        {
            RuleKey = RiskRuleKeys.RequireStopLoss,
            RuleValue = "false",
            ValueType = SettingValueType.Bool,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }
    ];

    private static BacktestDataset CreateDataset(IReadOnlyList<int> evaluationIndices)
    {
        var openTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            new()
            {
                Id = 1,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M3,
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                OpenTimeUtc = openTime.AddMinutes(-3),
                CloseTimeUtc = openTime,
                CreatedAtUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M3,
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                OpenTimeUtc = openTime,
                CloseTimeUtc = openTime.AddMinutes(3),
                CreatedAtUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 3,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M3,
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                OpenTimeUtc = openTime.AddMinutes(3),
                CloseTimeUtc = openTime.AddMinutes(6),
                CreatedAtUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 4,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M3,
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                OpenTimeUtc = openTime.AddMinutes(6),
                CloseTimeUtc = openTime.AddMinutes(9),
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        var snapshots = candles.ToDictionary(
            candle => candle.Id,
            candle => new IndicatorSnapshot
            {
                SymbolId = 1,
                Timeframe = Timeframe.M3,
                CandleId = candle.Id,
                CalculatedAtUtc = candle.CloseTimeUtc,
                Ema20 = 99m,
                Ema50 = 98m,
                Ema200 = 97m,
                CreatedAtUtc = DateTime.UtcNow
            });

        return new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M3,
            Candles = candles,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = evaluationIndices
        };
    }

    private static IReadOnlyList<PreparedStrategy> CreatePreparedStrategies()
    {
        var strategy = new Strategy
        {
            Id = 1,
            Code = StrategyCode.EmaPullback,
            Name = "EMA Pullback",
            IsEnabled = true
        };

        var plugin = new Mock<ITradingStrategy>();
        plugin.Setup(p => p.Code).Returns(StrategyCode.EmaPullback);
        plugin.Setup(p => p.Name).Returns("EMA Pullback");

        return [new PreparedStrategy { Strategy = strategy, Plugin = plugin.Object }];
    }
}

public class BacktestRunnerValidationTests
{
    [Fact]
    public async Task RunAsync_RejectsInvalidDateRange()
    {
        var runner = CreateRunner();
        var request = new RunBacktestRequest
        {
            Name = "Validation Test",
            ExchangeId = 1,
            SymbolIds = [1],
            Timeframes = ["3m"],
            FromUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = 1,
            StrategyIds = [1]
        };

        var result = await runner.RunAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("fromUtc", result.ErrorField);
    }

    [Fact]
    public async Task RunAsync_FailsSafelyWhenNoCandlesExist()
    {
        var dataLoader = new Mock<IBacktestDataLoader>();
        dataLoader.Setup(loader => loader.LoadSymbolTimeframeAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset?)null);

        var runner = CreateRunner(dataLoader: dataLoader.Object);
        var result = await runner.RunAsync(CreateValidRequest());

        Assert.False(result.Succeeded);
        Assert.Contains("No candle data", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static RunBacktestRequest CreateValidRequest() => new()
    {
        Name = "Validation Test",
        ExchangeId = 1,
        SymbolIds = [1],
        Timeframes = ["3m"],
        FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
        InitialBalance = 10_000m,
        RiskProfileId = 1,
        StrategyIds = [1]
    };

    private static BacktestRunner CreateRunner(IBacktestDataLoader? dataLoader = null)
    {
        var exchange = new MomoQuant.Domain.Exchanges.Exchange
        {
            Id = 1,
            Code = "BINANCE_FUTURES",
            Name = "Binance Futures",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };

        var symbol = new MomoQuant.Domain.Exchanges.Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" };
        var strategy = new Strategy { Id = 1, Code = StrategyCode.EmaPullback, Name = "EMA Pullback", IsEnabled = true };
        var plugin = new Mock<ITradingStrategy>();
        plugin.Setup(p => p.Code).Returns(StrategyCode.EmaPullback);

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var riskProfileRepository = new Mock<IRiskProfileRepository>();
        riskProfileRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MomoQuant.Domain.Risk.RiskProfile { Id = 1, Name = "Default" });

        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(strategy);
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { strategy });

        var strategyRegistry = new Mock<IStrategyRegistry>();
        strategyRegistry.Setup(registry => registry.GetByCode(StrategyCode.EmaPullback)).Returns(plugin.Object);

        var sessionRepository = new Mock<ITradingSessionRepository>();
        sessionRepository.Setup(repo => repo.AddAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TradingSession, CancellationToken>((session, _) => session.Id = 1);
        sessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var backtestRunRepository = new Mock<IBacktestRunRepository>();
        backtestRunRepository.Setup(repo => repo.AddAsync(It.IsAny<BacktestRun>(), It.IsAny<CancellationToken>()))
            .Callback<BacktestRun, CancellationToken>((run, _) => run.Id = 1);
        backtestRunRepository.Setup(repo => repo.UpdateAsync(It.IsAny<BacktestRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        backtestRunRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var auditService = new Mock<IAuditService>();
        auditService.Setup(service => service.LogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<long?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.UserId).Returns(1);

        var riskRuleRepository = new Mock<IRiskRuleRepository>();
        riskRuleRepository.Setup(repo => repo.GetByProfileIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MomoQuant.Domain.Risk.RiskRule>());

        var preflightValidator = new Mock<ITradingSessionPreflightValidator>();
        preflightValidator.Setup(validator => validator.ValidateAsync(It.IsAny<TradingSessionPreflightRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<TradingSessionPreflightResult>.Ok(new TradingSessionPreflightResult
            {
                CandleCount = 100,
                IndicatorSnapshotCount = 100,
                EffectiveMinConfidenceScore = 80m,
                Warnings = []
            }));

        return new BacktestRunner(
            backtestRunRepository.Object,
            new Mock<IBacktestResultRepository>().Object,
            new Mock<IBacktestEquityPointRepository>().Object,
            new Mock<IBacktestStrategyResultRepository>().Object,
            new Mock<IBacktestSymbolResultRepository>().Object,
            sessionRepository.Object,
            new Mock<IStrategySignalRepository>().Object,
            new Mock<IRiskDecisionRepository>().Object,
            new Mock<IAiDecisionRepository>().Object,
            new Mock<IOrderRepository>().Object,
            new Mock<IOrderFillRepository>().Object,
            new Mock<ITradeRepository>().Object,
            new Mock<IMissedOrderRepository>().Object,
            exchangeRepository.Object,
            symbolRepository.Object,
            riskProfileRepository.Object,
            strategyRepository.Object,
            strategyRegistry.Object,
            riskRuleRepository.Object,
            dataLoader ?? new Mock<IBacktestDataLoader>().Object,
            new Mock<IBacktestEngine>().Object,
            new BacktestMetricsCalculator(),
            currentUser.Object,
            auditService.Object,
            preflightValidator.Object);
    }
}
