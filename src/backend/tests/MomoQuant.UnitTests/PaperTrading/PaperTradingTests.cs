using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.UnitTests.PaperTrading;

public class PaperMapperTests
{
    [Theory]
    [InlineData("HistoricalPaper", PaperTradingMode.HistoricalPaper)]
    [InlineData("LivePaper", PaperTradingMode.LivePaper)]
    public void TryParseMode_ParsesKnownValues(string input, PaperTradingMode expected)
    {
        var parsed = PaperMapper.TryParseMode(input, out var mode);

        Assert.True(parsed);
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void TryParseMode_RejectsInvalidValue()
    {
        var parsed = PaperMapper.TryParseMode("invalid", out _);

        Assert.False(parsed);
    }
}

public class PaperAccountServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesPaperAccount()
    {
        var repository = new Mock<IPaperAccountRepository>();
        repository.Setup(repo => repo.AddAsync(It.IsAny<PaperAccount>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogAsync(
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

        var service = new PaperAccountService(
            repository.Object,
            new Mock<IPaperAccountSnapshotRepository>().Object,
            new Mock<ICurrentUserService>().Object,
            audit.Object);

        var result = await service.CreateAsync(new CreatePaperAccountRequest
        {
            Name = "Default Paper Account",
            InitialBalance = 10_000m,
            Currency = "USDT"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Default Paper Account", result.Data.Name);
        Assert.Equal(10_000m, result.Data.CurrentBalance);
    }

    [Fact]
    public async Task ResetAsync_RestoresInitialBalance()
    {
        var account = new PaperAccount
        {
            Id = 1,
            Name = "Test",
            InitialBalance = 10_000m,
            CurrentBalance = 8_500m,
            CurrentEquity = 8_700m,
            TotalRealizedPnl = -500m,
            TotalFees = 100m,
            Currency = "USDT",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var repository = new Mock<IPaperAccountRepository>();
        repository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        repository.Setup(repo => repo.UpdateAsync(It.IsAny<PaperAccount>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogAsync(
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

        var service = new PaperAccountService(
            repository.Object,
            new Mock<IPaperAccountSnapshotRepository>().Object,
            new Mock<ICurrentUserService>().Object,
            audit.Object);

        var result = await service.ResetAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(10_000m, account.CurrentBalance);
        Assert.Equal(10_000m, account.CurrentEquity);
        Assert.Equal(0m, account.TotalRealizedPnl);
        Assert.Equal(0m, account.TotalFees);
    }
}

public class PaperSessionControlServiceTests
{
    [Fact]
    public async Task PauseAsync_ChangesStatusToPaused()
    {
        var session = CreateSession(PaperSessionStatus.Running);
        var repository = new Mock<IPaperTradingSessionRepository>();
        repository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        repository.Setup(repo => repo.UpdateAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogAsync(
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

        var service = CreateControlService(repository.Object, audit.Object);
        var result = await service.PauseAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(PaperSessionStatus.Paused.ToString(), result.Data!.Status);
    }

    [Fact]
    public async Task StopAsync_ChangesStatusToStopped()
    {
        var session = CreateSession(PaperSessionStatus.Running);
        var repository = new Mock<IPaperTradingSessionRepository>();
        repository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        repository.Setup(repo => repo.UpdateAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogAsync(
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

        var service = CreateControlService(repository.Object, audit.Object);
        var result = await service.StopAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(PaperSessionStatus.Stopped.ToString(), result.Data!.Status);
    }

    private static PaperSessionControlService CreateControlService(
        IPaperTradingSessionRepository sessionRepository,
        IAuditService auditService) => new(
        sessionRepository,
        new Mock<ITradingSessionRepository>().Object,
        new Mock<IPaperStateStore>().Object,
        new Mock<IPaperTradingEngine>().Object,
        new Mock<IPaperPersistenceService>().Object,
        new Mock<ILiveMarketConnectionManager>().Object,
        new Mock<ILiveMarketBootstrapService>().Object,
        new Mock<ICurrentUserService>().Object,
        auditService);

    private static PaperTradingSession CreateSession(PaperSessionStatus status) => new()
    {
        Id = 1,
        Name = "Test",
        PaperAccountId = 1,
        TradingSessionId = 1,
        Status = status,
        Mode = PaperTradingMode.HistoricalPaper,
        ExchangeId = 1,
        RiskProfileId = 1,
        ExecutionMode = ExecutionMode.MakerOnly,
        TotalCandles = 10,
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class PaperTradingEngineTests
{
    [Fact]
    public async Task ProcessNextCandleAsync_AdvancesEvaluationIndex()
    {
        var candle = CreateCandle(1, DateTime.UtcNow);
        var dataset = CreateDataset(candle);
        var state = CreateState(dataset);

        var backtestEngine = new Mock<IBacktestEngine>();
        backtestEngine.Setup(engine => engine.ProcessCandleAtIndexAsync(
                state.Context,
                dataset,
                state.Strategies,
                0,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CandleProcessResult
            {
                Candle = candle,
                SignalCountBefore = 0,
                AiCountBefore = 0,
                RiskCountBefore = 0,
                OrderCountBefore = 0,
                FillCountBefore = 0,
                TradeCountBefore = 0,
                MissedCountBefore = 0
            });

        var engine = new PaperTradingEngine(
            backtestEngine.Object,
            new PaperExecutionProvider(new Mock<Application.Backtesting.Simulation.ISimulatedExecutionProvider>().Object));

        var result = await engine.ProcessNextCandleAsync(state);

        Assert.NotNull(result);
        Assert.Equal(1, state.NextEvaluationIndex);
        Assert.Equal(0, state.Session.CurrentCandleIndex);
    }

    [Fact]
    public async Task ProcessNextCandleAsync_ReturnsNullWhenComplete()
    {
        var candle = CreateCandle(1, DateTime.UtcNow);
        var dataset = CreateDataset(candle);
        var state = CreateState(dataset);
        state.NextEvaluationIndex = 1;

        var engine = new PaperTradingEngine(
            new Mock<IBacktestEngine>().Object,
            new PaperExecutionProvider(new Mock<Application.Backtesting.Simulation.ISimulatedExecutionProvider>().Object));

        var result = await engine.ProcessNextCandleAsync(state);

        Assert.Null(result);
    }

    private static PaperSessionState CreateState(BacktestDataset dataset)
    {
        var session = new PaperTradingSession
        {
            Id = 1,
            Name = "Test",
            PaperAccountId = 1,
            TradingSessionId = 1,
            Status = PaperSessionStatus.Running,
            Mode = PaperTradingMode.HistoricalPaper,
            ExchangeId = 1,
            RiskProfileId = 1,
            ExecutionMode = ExecutionMode.MarketFill,
            TotalCandles = 1,
            CreatedAtUtc = DateTime.UtcNow
        };

        var account = new PaperAccount
        {
            Id = 1,
            Name = "Account",
            InitialBalance = 10_000m,
            CurrentBalance = 10_000m,
            CurrentEquity = 10_000m,
            Currency = "USDT",
            CreatedAtUtc = DateTime.UtcNow
        };

        return new PaperSessionState
        {
            Session = session,
            Account = account,
            Settings = new PaperSessionSettings
            {
                MakerFeeRate = 0.0002m,
                TakerFeeRate = 0.0005m,
                OrderExpiryCandles = 3,
                UseAiScoring = false,
                MinConfidenceScore = 80m,
                SlippagePercent = 0m,
                ExecutionMode = ExecutionMode.MarketFill,
                StrategyIds = [1],
                SymbolIds = [1],
                Timeframes = [Timeframe.M3]
            },
            Context = new BacktestContext
            {
                BacktestRunId = 1,
                SimulationMode = TradingMode.Paper,
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
                Symbols = new Dictionary<long, Symbol>
                {
                    [1] = new() { Id = 1, SymbolName = "BTCUSDT", ExchangeId = 1 }
                },
                Balance = 10_000m,
                PeakEquity = 10_000m
            },
            Dataset = dataset,
            Strategies = [],
            RiskRules = [],
            NextEvaluationIndex = 0
        };
    }

    private static BacktestDataset CreateDataset(Candle candle) => new()
    {
        SymbolId = 1,
        SymbolName = "BTCUSDT",
        Timeframe = Timeframe.M3,
        Candles = [candle],
        IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
        EvaluationIndices = [0]
    };

    private static Candle CreateCandle(long id, DateTime time) => new()
    {
        Id = id,
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        OpenTimeUtc = time.AddMinutes(-3),
        CloseTimeUtc = time,
        Open = 100m,
        High = 101m,
        Low = 99m,
        Close = 100m,
        Volume = 1000m,
        CreatedAtUtc = time
    };
}

public class PaperSessionQueryServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReturnsProgressFields()
    {
        var candleTime = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var session = new PaperTradingSession
        {
            Id = 5,
            Name = "Historical",
            PaperAccountId = 1,
            TradingSessionId = 10,
            Status = PaperSessionStatus.Running,
            Mode = PaperTradingMode.HistoricalPaper,
            CurrentCandleIndex = 49,
            TotalCandles = 100,
            CurrentCandleTimeUtc = candleTime,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var account = new PaperAccount
        {
            Id = 1,
            Name = "Paper",
            InitialBalance = 10_000m,
            CurrentBalance = 9_500m,
            CurrentEquity = 9_800m
        };

        var sessionRepository = new Mock<IPaperTradingSessionRepository>();
        sessionRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var accountRepository = new Mock<IPaperAccountRepository>();
        accountRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var orderRepository = new Mock<IOrderRepository>();
        orderRepository
            .Setup(repo => repo.GetByTradingSessionIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var positionRepository = new Mock<IPositionRepository>();
        positionRepository
            .Setup(repo => repo.GetByTradingSessionIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tradeRepository = new Mock<ITradeRepository>();
        tradeRepository
            .Setup(repo => repo.GetByTradingSessionIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var missedOrderRepository = new Mock<IMissedOrderRepository>();
        missedOrderRepository
            .Setup(repo => repo.GetByTradingSessionIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = new PaperSessionQueryService(
            sessionRepository.Object,
            accountRepository.Object,
            new Mock<IPaperAccountSnapshotRepository>().Object,
            orderRepository.Object,
            new Mock<IOrderFillRepository>().Object,
            positionRepository.Object,
            tradeRepository.Object,
            missedOrderRepository.Object,
            new Mock<IStrategySignalRepository>().Object,
            new Mock<IRiskDecisionRepository>().Object,
            new Mock<IAiDecisionRepository>().Object,
            new Mock<IPaperStateStore>().Object,
            new Mock<ILiveMarketConnectionManager>().Object,
            new Mock<ILiveMarketSnapshotStore>().Object,
            new Mock<ISymbolRepository>().Object);

        var result = await service.GetStatusAsync(5);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.SessionId);
        Assert.Equal("Running", result.Data.Status);
        Assert.Equal("HistoricalPaper", result.Data.Mode);
        Assert.Equal(50, result.Data.ProcessedCandles);
        Assert.Equal(100, result.Data.TotalCandles);
        Assert.Equal(50m, result.Data.ProgressPercent);
        Assert.Equal(candleTime, result.Data.CurrentCandleTimeUtc);
        Assert.Equal(9_500m, result.Data.CurrentBalance);
        Assert.Equal(9_800m, result.Data.CurrentEquity);
        Assert.Equal(0, result.Data.OpenPositionCount);
        Assert.Equal(0, result.Data.OrdersCount);
        Assert.Equal(0, result.Data.TradesCount);
        Assert.Equal(0, result.Data.MissedOrdersCount);
        Assert.NotNull(result.Data.LastUpdatedAtUtc);
    }

    [Fact]
    public async Task GetStatusAsync_LivePaper_ReturnsLiveProgressWithoutFinalCandleWarnings()
    {
        var session = new PaperTradingSession
        {
            Id = 6,
            Name = "Live",
            PaperAccountId = 1,
            TradingSessionId = 10,
            Status = PaperSessionStatus.Running,
            Mode = PaperTradingMode.LivePaper,
            ExchangeId = 1,
            CurrentCandleIndex = -1,
            TotalCandles = 0,
            ConfigJson = """{"symbolIds":[1],"timeframes":["3m"],"strategyIds":[1]}""",
            UpdatedAtUtc = DateTime.UtcNow
        };

        var account = new PaperAccount
        {
            Id = 1,
            Name = "Paper",
            InitialBalance = 100m,
            CurrentBalance = 100m,
            CurrentEquity = 100m
        };

        var sessionRepository = new Mock<IPaperTradingSessionRepository>();
        sessionRepository.Setup(repo => repo.GetByIdAsync(6, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var accountRepository = new Mock<IPaperAccountRepository>();
        accountRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Symbol
        {
            Id = 1,
            ExchangeId = 1,
            SymbolName = "BNBUSDT",
            CreatedAtUtc = DateTime.UtcNow
        });

        var liveConnection = new Mock<ILiveMarketConnectionManager>();
        liveConnection.Setup(manager => manager.IsConnected).Returns(true);
        liveConnection.Setup(manager => manager.IsSubscribed(1, Timeframe.M3)).Returns(true);
        liveConnection.Setup(manager => manager.GetDiagnostics()).Returns(new Application.LiveMarket.Dtos.LiveMarketDiagnosticsDto
        {
            Provider = "Binance",
            Connected = true,
            Subscriptions =
            [
                new Application.LiveMarket.Dtos.LiveMarketSubscriptionDiagnosticsDto
                {
                    ExchangeId = 1,
                    SymbolId = 1,
                    Symbol = "BNBUSDT",
                    Timeframe = "3m",
                    StreamName = "bnbusdt@kline_3m",
                    ConnectionState = "Connected",
                    Warning = "Waiting for first live candle update."
                }
            ]
        });

        var service = new PaperSessionQueryService(
            sessionRepository.Object,
            accountRepository.Object,
            new Mock<IPaperAccountSnapshotRepository>().Object,
            new Mock<IOrderRepository>().Object,
            new Mock<IOrderFillRepository>().Object,
            new Mock<IPositionRepository>().Object,
            new Mock<ITradeRepository>().Object,
            new Mock<IMissedOrderRepository>().Object,
            new Mock<IStrategySignalRepository>().Object,
            new Mock<IRiskDecisionRepository>().Object,
            new Mock<IAiDecisionRepository>().Object,
            new Mock<IPaperStateStore>().Object,
            liveConnection.Object,
            new Mock<ILiveMarketSnapshotStore>().Object,
            symbolRepository.Object);

        var result = await service.GetStatusAsync(6);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("LivePaper", result.Data.Mode);
        Assert.Equal("Live", result.Data.ProgressLabel);
        Assert.Null(result.Data.TotalCandles);
        Assert.Null(result.Data.ProgressPercent);
        Assert.Equal(0, result.Data.ProcessedCandles);
        Assert.DoesNotContain(result.Data.Warnings, warning => warning.Contains("final candle", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Data.Warnings, warning => warning.Contains("No candles are configured", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Data.Warnings, warning => warning.Contains("live", StringComparison.OrdinalIgnoreCase));
    }
}
