using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.Trading;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Optimization;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1 — production services reject archived/unsupported codes
/// before persistence / research executor invocation.
/// </summary>
public sealed class Milestone231A1ProductionPathRejectionUnitTests
{
    [Fact]
    public async Task Backtest_ArchivedRequest_FailsBeforeRunCreation()
    {
        var archived = new Strategy
        {
            Id = 99,
            Code = StrategyCode.EmaPullback,
            Name = "EMA Pullback",
            IsEnabled = true,
            Version = "1.0.0"
        };

        var backtestRuns = new Mock<IBacktestRunRepository>(MockBehavior.Strict);
        var runner = CreateBacktestRunner(archived, backtestRuns);

        var result = await runner.RunAsync(new RunBacktestRequest
        {
            Name = "Archived should fail",
            ExchangeId = 1,
            SymbolIds = [1],
            Timeframes = ["5m"],
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = 1,
            StrategyIds = [99]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        backtestRuns.Verify(
            repo => repo.AddAsync(It.IsAny<BacktestRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ParameterOptimization_ArchivedRequest_FailsBeforeRunPersistence()
    {
        var runs = new Mock<IParameterOptimizationRunRepository>(MockBehavior.Loose);
        var definitions = new Mock<IStrategyParameterDefinitionProvider>(MockBehavior.Loose);
        definitions.Setup(provider => provider.EstimateGridCombinations(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns(10);
        definitions.Setup(provider => provider.GenerateGridCombinations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns([new Dictionary<string, string> { ["x"] = "1" }]);

        var research = new Mock<IStrategyResearchBacktestExecutor>(MockBehavior.Loose);
        var service = CreateOptimizationService(runs, definitions, research);

        var result = await service.RunAsync(new RunParameterOptimizationRequest
        {
            StrategyCode = StrategyCodes.EmaPullback,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "5m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            OptimizationMode = ParameterOptimizationMode.GridSearch,
            MaxCombinations = 10,
            MaxRuntimeMinutes = 5
        }, userId: 1);

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        runs.Verify(repo => repo.AddAsync(It.IsAny<ParameterOptimizationRun>(), It.IsAny<CancellationToken>()), Times.Never);
        definitions.Verify(
            provider => provider.GenerateGridCombinations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>()),
            Times.Never);
        research.Verify(
            executor => executor.RunWindowAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<long>(),
                It.IsAny<decimal>(),
                It.IsAny<StrategyResearchExecutionOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ParameterOptimization_UnsupportedCanonical_FailsBeforeRunPersistence()
    {
        var runs = new Mock<IParameterOptimizationRunRepository>(MockBehavior.Loose);
        var definitions = new Mock<IStrategyParameterDefinitionProvider>(MockBehavior.Loose);
        definitions.Setup(provider => provider.EstimateGridCombinations(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns(10);
        definitions.Setup(provider => provider.GenerateGridCombinations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns([new Dictionary<string, string> { ["x"] = "1" }]);

        var research = new Mock<IStrategyResearchBacktestExecutor>(MockBehavior.Loose);
        var service = CreateOptimizationService(runs, definitions, research);

        var result = await service.RunAsync(new RunParameterOptimizationRequest
        {
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "5m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            OptimizationMode = ParameterOptimizationMode.GridSearch,
            MaxCombinations = 10,
            MaxRuntimeMinutes = 5
        }, userId: 1);

        Assert.False(result.Succeeded);
        Assert.Contains("does not support parameter optimization", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        runs.Verify(repo => repo.AddAsync(It.IsAny<ParameterOptimizationRun>(), It.IsAny<CancellationToken>()), Times.Never);
        definitions.Verify(
            provider => provider.GenerateGridCombinations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>()),
            Times.Never);
        research.Verify(
            executor => executor.RunWindowAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<long>(),
                It.IsAny<decimal>(),
                It.IsAny<StrategyResearchExecutionOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StrategyValidation_MtfAndRange_FailBeforeResearchExecutor()
    {
        var research = new Mock<IStrategyResearchBacktestExecutor>(MockBehavior.Loose);
        var service = CreateValidationService(research);

        foreach (var code in new[]
                 {
                     StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
                     StrategyCodes.MomoVolatilityRangeReversion
                 })
        {
            var result = await service.RunAsync(new RunStrategyValidationRequest
            {
                StrategyCode = code,
                ExchangeId = 1,
                SymbolId = 1,
                Timeframe = "15m",
                FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.False(result.Succeeded);
            Assert.Contains("does not support Validation Laboratory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        research.Verify(
            executor => executor.RunWindowAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<long>(),
                It.IsAny<decimal>(),
                It.IsAny<StrategyResearchExecutionOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Replay_ArchivedRequest_FailsBeforeSessionCreation()
    {
        var archived = ArchivedEmaPullback(99);
        var sessions = new Mock<IReplaySessionRepository>(MockBehavior.Strict);
        var tradingSessions = new Mock<ITradingSessionRepository>(MockBehavior.Strict);
        var service = CreateReplayService(archived, sessions, tradingSessions);

        var result = await service.CreateAsync(new CreateReplaySessionRequest
        {
            Name = "Archived replay",
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "5m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = 1,
            StrategyIds = [99]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        sessions.Verify(repo => repo.AddAsync(It.IsAny<ReplaySession>(), It.IsAny<CancellationToken>()), Times.Never);
        tradingSessions.Verify(repo => repo.AddAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Paper_ArchivedRequest_FailsBeforeSessionCreation()
    {
        var archived = ArchivedEmaPullback(99);
        var sessions = new Mock<IPaperTradingSessionRepository>(MockBehavior.Strict);
        var tradingSessions = new Mock<ITradingSessionRepository>(MockBehavior.Strict);
        var service = CreatePaperService(archived, sessions, tradingSessions);

        var result = await service.CreateAsync(new CreatePaperSessionRequest
        {
            Name = "Archived paper",
            PaperAccountId = 1,
            ExchangeId = 1,
            SymbolIds = [1],
            Timeframes = ["5m"],
            Mode = "HistoricalPaper",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RiskProfileId = 1,
            StrategyIds = [99]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        sessions.Verify(repo => repo.AddAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>()), Times.Never);
        tradingSessions.Verify(repo => repo.AddAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Paper_DisabledCanonicalRequest_FailsBeforeSessionCreation()
    {
        var disabled = new Strategy
        {
            Id = 99,
            Code = StrategyCode.PriceStructureBreakoutRetest,
            Name = "Disabled PSBR",
            IsEnabled = false,
            Version = "1.1"
        };
        var sessions = new Mock<IPaperTradingSessionRepository>(MockBehavior.Strict);
        var tradingSessions = new Mock<ITradingSessionRepository>(MockBehavior.Strict);
        var service = CreatePaperService(disabled, sessions, tradingSessions);

        var result = await service.CreateAsync(new CreatePaperSessionRequest
        {
            Name = "Disabled paper",
            PaperAccountId = 1,
            ExchangeId = 1,
            SymbolIds = [1],
            Timeframes = ["5m"],
            Mode = "HistoricalPaper",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RiskProfileId = 1,
            StrategyIds = [99]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        sessions.Verify(repo => repo.AddAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>()), Times.Never);
        tradingSessions.Verify(repo => repo.AddAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Benchmark_ArchivedRequest_FailsBeforeQueueSubmission()
    {
        var archived = ArchivedEmaPullback(99);
        var runs = new Mock<IStrategyBenchmarkRunRepository>(MockBehavior.Strict);
        var queue = new Mock<IStrategyBenchmarkQueue>(MockBehavior.Strict);
        var service = CreateBenchmarkService(archived, runs, queue);

        var result = await service.CreateAsync(new CreateStrategyBenchmarkRequest
        {
            Name = "Archived benchmark",
            ExchangeCode = "BINANCE_FUTURES",
            Symbols = ["BTCUSDT"],
            StrategyIds = [99],
            BenchmarkFromDate = new DateOnly(2026, 6, 1),
            BenchmarkToDate = new DateOnly(2026, 6, 10),
            WarmupFromDate = new DateOnly(2026, 5, 25),
            RiskProfileId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        runs.Verify(repo => repo.AddAsync(It.IsAny<StrategyBenchmarkRun>(), It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(q => q.Enqueue(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task BenchmarkWorker_StaleArchivedPayload_FailsBeforeBacktest()
    {
        var archived = ArchivedEmaPullback(99);
        var run = new StrategyBenchmarkRun
        {
            Id = 7,
            Name = "Stale archived",
            Status = StrategyBenchmarkStatus.Pending,
            ExchangeId = 1,
            SymbolsJson = StrategyBenchmarkMapper.SerializeList(["BTCUSDT"]),
            TimeframesJson = StrategyBenchmarkMapper.SerializeList(["5m"]),
            StrategyIdsJson = StrategyBenchmarkMapper.SerializeList([99L]),
            BenchmarkFromUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BenchmarkToUtc = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            WarmupFromUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
            WarmupToUtc = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = 1,
            ExecutionMode = ExecutionMode.MarketFill,
            ConfigJson = "{}"
        };

        var runs = new Mock<IStrategyBenchmarkRunRepository>();
        runs.Setup(repo => repo.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runs.Setup(repo => repo.UpdateAsync(It.IsAny<StrategyBenchmarkRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var results = new Mock<IStrategyBenchmarkResultRepository>(MockBehavior.Strict);
        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByExchangeAndNameAsync(1, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([archived]);

        var runItems = new Mock<IStrategyBenchmarkRunItemRepository>();
        runItems.Setup(repo => repo.GetByBenchmarkRunIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StrategyBenchmarkRunItem>());

        var runner = new StrategyBenchmarkRunner(
            runs.Object,
            results.Object,
            runItems.Object,
            symbols.Object,
            strategies.Object,
            Mock.Of<IMarketDataService>(),
            Mock.Of<IIndicatorCalculationService>(),
            Mock.Of<IBacktestProgressStore>(),
            Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Mock.Of<IStrategyGradeService>(),
            Mock.Of<IBenchmarkImportRangeChunker>(),
            Options.Create(new MarketDataSettings()),
            Options.Create(new StrategyBenchmarkSettings()),
            Mock.Of<ILogger<StrategyBenchmarkRunner>>());

        await runner.ExecuteAsync(7);

        Assert.Equal(StrategyBenchmarkStatus.Failed, run.Status);
        Assert.Contains("archived", run.Message, StringComparison.OrdinalIgnoreCase);
        results.Verify(repo => repo.AddAsync(It.IsAny<StrategyBenchmarkResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ManualEvaluation_ArchivedRequest_FailsBeforeEngine()
    {
        var archived = ArchivedEmaPullback(99);
        var engine = new Mock<IStrategyEngine>(MockBehavior.Strict);
        var candle = new Candle
        {
            Id = 5,
            SymbolId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CloseTimeUtc = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc),
            Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
        };

        var candles = new Mock<ICandleRepository>();
        candles.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(candle);
        candles.Setup(repo => repo.GetRecentCandlesAsync(
                1, Timeframe.M5, candle.OpenTimeUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candle]);

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([archived]);

        var indicators = new Mock<IIndicatorSnapshotRepository>();
        indicators.Setup(repo => repo.GetByKeyAsync(1, Timeframe.M5, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndicatorSnapshot?)null);
        indicators.Setup(repo => repo.GetRecentForSymbolAsync(
                1, Timeframe.M5, candle.OpenTimeUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndicatorSnapshot>());

        var service = new StrategyService(
            strategies.Object,
            Mock.Of<IStrategyParameterRepository>(),
            Mock.Of<IStrategyRegistry>(),
            engine.Object,
            Mock.Of<IStrategyParameterProvider>(),
            Mock.Of<IStrategyDataRequirementService>(),
            Mock.Of<IStrategyParameterDefinitionProvider>(),
            candles.Object,
            indicators.Object,
            symbols.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.EvaluateAsync(new StrategyEvaluationRequest
        {
            SymbolId = 1,
            Timeframe = "5m",
            CandleId = 5,
            MarketRegime = "Unknown",
            StrategyIds = [99]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        engine.Verify(
            e => e.EvaluateAsync(
                It.IsAny<IReadOnlyCollection<ITradingStrategy>>(),
                It.IsAny<StrategyContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidationCreate_MtfAndRange_FailBeforeExperimentPersistence()
    {
        var experiments = new Mock<IValidationExperimentRepository>(MockBehavior.Strict);
        var service = CreateValidationLabService(experiments);

        foreach (var code in new[]
                 {
                     StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
                     StrategyCodes.MomoVolatilityRangeReversion
                 })
        {
            var result = await service.CreateExperimentAsync(new CreateValidationExperimentRequest
            {
                StrategyCode = code,
                ExchangeId = 1,
                SymbolId = 1,
                Timeframe = "15m",
                RequestedStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RequestedEndUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.False(result.Succeeded);
            Assert.Contains("does not support Validation Laboratory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        experiments.Verify(
            repo => repo.AddAsync(It.IsAny<ValidationExperiment>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Strategy ArchivedEmaPullback(long id) => new()
    {
        Id = id,
        Code = StrategyCode.EmaPullback,
        Name = "EMA Pullback",
        IsEnabled = true,
        Version = "1.0.0"
    };

    private static ReplaySessionService CreateReplayService(
        Strategy archived,
        Mock<IReplaySessionRepository> sessions,
        Mock<ITradingSessionRepository> tradingSessions)
    {
        var exchanges = new Mock<IExchangeRepository>();
        exchanges.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" });

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var risk = new Mock<IRiskProfileRepository>();
        risk.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskProfile { Id = 1, Name = "Default" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetByIdAsync(archived.Id, It.IsAny<CancellationToken>())).ReturnsAsync(archived);

        var enricher = new Mock<IHigherTimeframeDatasetEnricher>();
        enricher.Setup(e => e.EnrichForStrategiesAsync(
                It.IsAny<BacktestDataset>(),
                It.IsAny<IReadOnlyList<PreparedStrategy>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset dataset, IReadOnlyList<PreparedStrategy> _, CancellationToken _) => dataset);

        return new ReplaySessionService(
            sessions.Object,
            tradingSessions.Object,
            exchanges.Object,
            symbols.Object,
            risk.Object,
            strategies.Object,
            Mock.Of<IStrategyRegistry>(),
            Mock.Of<IRiskRuleRepository>(),
            Mock.Of<IReplayDataLoader>(),
            Mock.Of<IReplayStateStore>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            enricher.Object);
    }

    private static PaperSessionService CreatePaperService(
        Strategy archived,
        Mock<IPaperTradingSessionRepository> sessions,
        Mock<ITradingSessionRepository> tradingSessions)
    {
        var accounts = new Mock<IPaperAccountRepository>();
        accounts.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperAccount { Id = 1, Name = "Paper", IsActive = true, Currency = "USDT" });

        var exchanges = new Mock<IExchangeRepository>();
        exchanges.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" });

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var risk = new Mock<IRiskProfileRepository>();
        risk.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskProfile { Id = 1, Name = "Default" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetByIdAsync(archived.Id, It.IsAny<CancellationToken>())).ReturnsAsync(archived);

        var live = new Mock<ILiveMarketConnectionManager>();
        live.SetupGet(m => m.IsAvailable).Returns(true);

        var enricher = new Mock<IHigherTimeframeDatasetEnricher>();
        enricher.Setup(e => e.EnrichForStrategiesAsync(
                It.IsAny<BacktestDataset>(),
                It.IsAny<IReadOnlyList<PreparedStrategy>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset dataset, IReadOnlyList<PreparedStrategy> _, CancellationToken _) => dataset);

        return new PaperSessionService(
            sessions.Object,
            accounts.Object,
            tradingSessions.Object,
            exchanges.Object,
            symbols.Object,
            risk.Object,
            strategies.Object,
            Mock.Of<IStrategyRegistry>(),
            Mock.Of<IStrategyParameterSetRepository>(),
            Mock.Of<IStrategyParameterProvider>(),
            Mock.Of<IRiskRuleRepository>(),
            Mock.Of<IBacktestDataLoader>(),
            Mock.Of<IPaperStateStore>(),
            live.Object,
            Mock.Of<IMarketSituationService>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            enricher.Object);
    }

    private static StrategyBenchmarkService CreateBenchmarkService(
        Strategy archived,
        Mock<IStrategyBenchmarkRunRepository> runs,
        Mock<IStrategyBenchmarkQueue> queue)
    {
        var exchanges = new Mock<IExchangeRepository>();
        exchanges.Setup(repo => repo.GetByCodeAsync("BINANCE_FUTURES", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" });

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByExchangeAndNameAsync(1, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([archived]);

        var risk = new Mock<IRiskProfileRepository>();
        risk.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskProfile { Id = 1, Name = "Default" });

        return new StrategyBenchmarkService(
            runs.Object,
            Mock.Of<IStrategyBenchmarkRunItemRepository>(),
            Mock.Of<IStrategyBenchmarkResultRepository>(),
            exchanges.Object,
            symbols.Object,
            strategies.Object,
            risk.Object,
            queue.Object,
            Mock.Of<IStrategyBenchmarkReportService>(),
            Mock.Of<IStrategyDataRequirementService>(),
            Mock.Of<IMarketDataService>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            Options.Create(new StrategyBenchmarkSettings()));
    }

    private static ValidationLabService CreateValidationLabService(Mock<IValidationExperimentRepository> experiments)
    {
        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var exchanges = new Mock<IExchangeRepository>();
        exchanges.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetByCodeAsync(It.IsAny<StrategyCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StrategyCode code, CancellationToken _) => new Strategy
            {
                Id = 1,
                Code = code,
                Name = code.ToString(),
                IsEnabled = true,
                Version = "1.0.0"
            });

        return new ValidationLabService(
            experiments.Object,
            Mock.Of<IValidationParameterTrialRepository>(),
            Mock.Of<IValidationSegmentResultRepository>(),
            Mock.Of<IStrategyLabRunRepository>(),
            Mock.Of<IStrategyResearchCandidateRepository>(),
            Mock.Of<IStrategyLabRunner>(),
            Mock.Of<ICandleRepository>(),
            symbols.Object,
            exchanges.Object,
            strategies.Object,
            Mock.Of<IStrategyParameterDefinitionProvider>(),
            Mock.Of<IHistoricalCandleCoverageService>(),
            Mock.Of<IValidationCandidateReconciliationService>(),
            Mock.Of<IValidationMetricConsistencyService>(),
            Mock.Of<IValidationLeakageAuditor>(),
            Mock.Of<IValidationVerdictService>(),
            Mock.Of<IValidationLaboratoryReadinessService>(),
            Mock.Of<IValidationTrainingPreflightService>(),
            Mock.Of<IValidationTrainingExecutionLeaseService>(),
            Mock.Of<IValidationTrialRecoveryService>(),
            Mock.Of<IValidationTrainingSelectionService>(),
            Mock.Of<IValidationSelectionIntegrityService>(),
            Mock.Of<IValidationParameterFingerprintService>(),
            Mock.Of<IValidationRiskBasisService>(),
            Mock.Of<IValidationCandleAccessAuditRepository>(),
            Mock.Of<IValidationCandleAccessRecorder>(),
            Mock.Of<IValidationTrainingScopeExecution>(),
            Mock.Of<IValidationTrainingFailureHandler>(),
            Mock.Of<IValidationSegmentResultWriter>(),
            Mock.Of<IStrategyExecutionRequirementsResolver>(),
            Mock.Of<IValidationTrialMetricsRouter>(),
            Mock.Of<IValidationTrialSegmentReconciliationService>(),
            Mock.Of<IValidationAuditExecutionFactory>(),
            Mock.Of<IValidationAuditExecutionSupersessionService>(),
            Mock.Of<IValidationAuditExecutionRecoveryService>(),
            Mock.Of<IValidationAuditExecutionFinalizer>(),
            Mock.Of<IValidationTrialAuditCompletionGate>(),
            Mock.Of<IValidationAuditExecutionRepository>(),
            Mock.Of<IValidationAuditBatchRepository>(),
            Mock.Of<IValidationAuditCompletenessVerifier>(),
            Mock.Of<IValidationAuthoritativeAuditQualificationEvaluator>());
    }

    private static ParameterOptimizationService CreateOptimizationService(
        Mock<IParameterOptimizationRunRepository> runs,
        Mock<IStrategyParameterDefinitionProvider> definitions,
        Mock<IStrategyResearchBacktestExecutor> research)
    {
        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        return new ParameterOptimizationService(
            runs.Object,
            definitions.Object,
            new Mock<IValidationDateSplitService>().Object,
            new Mock<IStrategyValidationEvaluator>().Object,
            new Mock<IParameterOptimizationScorer>().Object,
            research.Object,
            new Mock<IStrategyParameterSetService>().Object,
            symbols.Object);
    }

    private static StrategyValidationService CreateValidationService(Mock<IStrategyResearchBacktestExecutor> research)
    {
        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repo => repo.GetByCodeAsync(It.IsAny<StrategyCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StrategyCode code, CancellationToken _) => new Strategy
            {
                Id = 1,
                Code = code,
                Name = code.ToString(),
                IsEnabled = true,
                Version = "1.0.0"
            });

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var exchanges = new Mock<IExchangeRepository>();
        exchanges.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange
            {
                Id = 1,
                Code = "BINANCE_FUTURES",
                Name = "Binance Futures",
                BaseUrl = "https://example",
                WebSocketUrl = "wss://example"
            });

        return new StrategyValidationService(
            new Mock<IValidationDateSplitService>().Object,
            new Mock<IStrategyValidationEvaluator>().Object,
            research.Object,
            new Mock<IStrategyResearchCandleCoverageService>().Object,
            new Mock<IStrategyDataRequirementService>().Object,
            strategies.Object,
            new Mock<IStrategyParameterSetService>().Object,
            symbols.Object,
            exchanges.Object);
    }

    private static BacktestRunner CreateBacktestRunner(Strategy strategyUnderTest, Mock<IBacktestRunRepository> backtestRuns)
    {
        var exchange = new Exchange
        {
            Id = 1,
            Code = "BINANCE_FUTURES",
            Name = "Binance Futures",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };
        var symbol = new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" };

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var riskProfileRepository = new Mock<IRiskProfileRepository>();
        riskProfileRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskProfile { Id = 1, Name = "Default" });

        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetByIdAsync(strategyUnderTest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyUnderTest);
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategyUnderTest]);

        var riskRuleRepository = new Mock<IRiskRuleRepository>();
        riskRuleRepository.Setup(repo => repo.GetByProfileIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RiskRule>());

        var preflightValidator = new Mock<ITradingSessionPreflightValidator>();
        preflightValidator.Setup(validator => validator.ValidateAsync(It.IsAny<TradingSessionPreflightRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<TradingSessionPreflightResult>.Ok(new TradingSessionPreflightResult
            {
                CandleCount = 100,
                IndicatorSnapshotCount = 100,
                EffectiveMinConfidenceScore = 80m,
                Warnings = []
            }));

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

        return new BacktestRunner(
            backtestRuns.Object,
            new Mock<IBacktestResultRepository>().Object,
            new Mock<IBacktestEquityPointRepository>().Object,
            new Mock<IBacktestStrategyResultRepository>().Object,
            new Mock<IBacktestSymbolResultRepository>().Object,
            new Mock<ITradingSessionRepository>().Object,
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
            new Mock<IStrategyRegistry>().Object,
            riskRuleRepository.Object,
            new Mock<IBacktestDataLoader>().Object,
            new Mock<IBacktestEngine>().Object,
            new BacktestMetricsCalculator(),
            currentUser.Object,
            auditService.Object,
            preflightValidator.Object);
    }
}
