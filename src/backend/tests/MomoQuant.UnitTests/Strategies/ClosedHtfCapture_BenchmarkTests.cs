using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1E — real StrategyBenchmarkRunner → BacktestRunner → BacktestEngine → Adaptive chain.
/// </summary>
public sealed class ClosedHtfCapture_BenchmarkTests
{
    [Fact]
    public async Task BenchmarkWorker_RealChain_CleanVsPollutedHtf_IdenticalCapture()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        Assert.Contains(pollutedHtf, c => !c.IsClosed || c.CloseTimeUtc > fixture.EvaluationTimeUtc);

        var cleanRecording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        await ExecuteRealBenchmarkAsync(fixture, cleanHtf, cleanRecording, runId: 91);
        var cleanCapture = Assert.Single(cleanRecording.Capture.Records);
        var cleanResult = Assert.Single(cleanRecording.Results);
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        var pollutedRecording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        await ExecuteRealBenchmarkAsync(fixture, pollutedHtf, pollutedRecording, runId: 92);
        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            pollutedRecording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
    }

    [Fact]
    public async Task BenchmarkWorker_RealChain_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        await ExecuteRealBenchmarkAsync(
            fixture,
            htfSeries: null,
            recording,
            runId: 93,
            missingHtf: true);

        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        Assert.NotEmpty(fixture.LtfCandles);
        ClosedHtfCaptureHarness.AssertMissingHtfCapture(recording.Capture.Records[0], fixture.EvaluationTimeUtc);
        ClosedHtfCaptureHarness.AssertMtfDataUnavailable(recording.Results[0]);
    }

    private static async Task ExecuteRealBenchmarkAsync(
        ClosedHtfCaptureHarness.Fixture fixture,
        IReadOnlyList<Candle>? htfSeries,
        ClosedHtfCaptureHarness.RecordingStrategyEngine recording,
        long runId,
        bool missingHtf = false)
    {
        var adaptive = fixture.Prepared.Strategy;
        adaptive.Id = 42;
        var engine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);
        var dataset = missingHtf
            ? ClosedHtfCaptureHarness.CreateDatasetMissingHtf(fixture)
            : ClosedHtfCaptureHarness.CreateDataset(fixture, htfSeries!);

        var dataLoader = new Mock<IBacktestDataLoader>();
        dataLoader.Setup(l => l.LoadSymbolTimeframeAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataset);

        var backtestRunner = CreateRealBacktestRunner(engine, dataLoader.Object, adaptive);
        var run = BuildPendingRun(runId);
        run.CreatedByUserId = 1;
        var runItem = new StrategyBenchmarkRunItem
        {
            Id = 500 + runId,
            BenchmarkRunId = runId,
            StrategyId = 42,
            StrategyCode = adaptive.Code.ToCode(),
            StrategyName = adaptive.Name,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = "5m",
            Status = StrategyBenchmarkRunItemStatus.Pending,
            CandleCount = fixture.LtfCandles.Count
        };

        var runner = BuildRunner(run, runItem, adaptive, backtestRunner);
        await runner.ExecuteAsync(runId);
    }

    private static BacktestRunner CreateRealBacktestRunner(
        IBacktestEngine engine,
        IBacktestDataLoader dataLoader,
        Strategy adaptive)
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
        var plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var riskProfileRepository = new Mock<IRiskProfileRepository>();
        riskProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskProfile { Id = 1, Name = "Default" });

        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(adaptive);
        strategyRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([adaptive]);

        var strategyRegistry = new Mock<IStrategyRegistry>();
        strategyRegistry.Setup(r => r.GetByCode(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout)).Returns(plugin);

        var sessionRepository = new Mock<ITradingSessionRepository>();
        sessionRepository.Setup(r => r.AddAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TradingSession, CancellationToken>((session, _) => session.Id = 1);
        sessionRepository.Setup(r => r.UpdateAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessionRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var backtestRunRepository = new Mock<IBacktestRunRepository>();
        backtestRunRepository.Setup(r => r.AddAsync(It.IsAny<BacktestRun>(), It.IsAny<CancellationToken>()))
            .Callback<BacktestRun, CancellationToken>((run, _) => run.Id = 1);
        backtestRunRepository.Setup(r => r.UpdateAsync(It.IsAny<BacktestRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        backtestRunRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var auditService = new Mock<IAuditService>();
        auditService.Setup(s => s.LogAsync(
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
        currentUser.Setup(s => s.UserId).Returns(1);

        var riskRuleRepository = new Mock<IRiskRuleRepository>();
        riskRuleRepository.Setup(r => r.GetByProfileIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RiskRule>());

        var preflightValidator = new Mock<ITradingSessionPreflightValidator>();
        preflightValidator.Setup(v => v.ValidateAsync(It.IsAny<TradingSessionPreflightRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<TradingSessionPreflightResult>.Ok(new TradingSessionPreflightResult
            {
                CandleCount = 100,
                IndicatorSnapshotCount = 100,
                EffectiveMinConfidenceScore = 0m,
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
            dataLoader,
            engine,
            new BacktestMetricsCalculator(),
            currentUser.Object,
            auditService.Object,
            preflightValidator.Object);
    }

    private static StrategyBenchmarkRun BuildPendingRun(long id) => new()
    {
        Id = id,
        Name = "Closed HTF benchmark",
        Status = StrategyBenchmarkStatus.Pending,
        ExchangeId = 1,
        CreatedByUserId = 1,
        SymbolsJson = StrategyBenchmarkMapper.SerializeList(["BTCUSDT"]),
        TimeframesJson = StrategyBenchmarkMapper.SerializeList(["5m"]),
        StrategyIdsJson = StrategyBenchmarkMapper.SerializeList([42L]),
        BenchmarkFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        BenchmarkToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        WarmupFromUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        WarmupToUtc = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        InitialBalance = 10_000m,
        RiskProfileId = 1,
        ExecutionMode = ExecutionMode.MarketFill,
        MakerFeeRate = 0.0002m,
        TakerFeeRate = 0.0005m,
        OrderExpiryCandles = 3,
        ConfigJson = StrategyBenchmarkMapper.SerializeConfig(new StrategyBenchmarkConfigState
        {
            Request = new CreateStrategyBenchmarkRequest
            {
                ImportMissingData = false,
                RecalculateIndicators = false
            },
            Preparation = new StrategyBenchmarkPreparationDto
            {
                Imports = [],
                DataQuality = [],
                Indicators = []
            },
            ExecutionPlan = []
        })
    };

    internal static StrategyBenchmarkRunner BuildRunner(
        StrategyBenchmarkRun run,
        StrategyBenchmarkRunItem runItem,
        Strategy adaptive,
        IBacktestRunner backtestRunner)
    {
        var runs = new Mock<IStrategyBenchmarkRunRepository>();
        runs.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runs.Setup(r => r.UpdateAsync(It.IsAny<StrategyBenchmarkRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runItems = new Mock<IStrategyBenchmarkRunItemRepository>();
        runItems.Setup(r => r.GetByBenchmarkRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([runItem]);
        runItems.Setup(r => r.UpdateAsync(It.IsAny<StrategyBenchmarkRunItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        runItems.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var results = new Mock<IStrategyBenchmarkResultRepository>();
        results.Setup(r => r.GetByBenchmarkRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StrategyBenchmarkResult>());
        results.Setup(r => r.AddAsync(It.IsAny<StrategyBenchmarkResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(r => r.GetByExchangeAndNameAsync(1, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([adaptive]);

        return new StrategyBenchmarkRunner(
            runs.Object,
            results.Object,
            runItems.Object,
            symbols.Object,
            strategyRepo.Object,
            Mock.Of<IMarketDataService>(),
            Mock.Of<IIndicatorCalculationService>(),
            Mock.Of<IBacktestProgressStore>(),
            new CapturingScopeFactory(backtestRunner),
            Mock.Of<IStrategyGradeService>(),
            Mock.Of<IBenchmarkImportRangeChunker>(),
            Options.Create(new MarketDataSettings()),
            Options.Create(new StrategyBenchmarkSettings
            {
                ContinueOnRunFailure = true,
                MaxBacktestRunMinutes = 5,
                HeartbeatSeconds = 60
            }),
            Mock.Of<ILogger<StrategyBenchmarkRunner>>());
    }

    private sealed class CapturingScopeFactory(IBacktestRunner backtestRunner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(backtestRunner);

        private sealed class Scope(IBacktestRunner runner) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Provider(runner);
            public void Dispose()
            {
            }

            private sealed class Provider(IBacktestRunner runner) : IServiceProvider
            {
                public object? GetService(Type serviceType) =>
                    serviceType == typeof(IBacktestRunner) ? runner : null;
            }
        }
    }
}
