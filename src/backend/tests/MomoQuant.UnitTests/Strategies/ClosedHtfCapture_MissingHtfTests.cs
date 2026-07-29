using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1D — missing mapped HTF for all six closed-HTF execution modes.
/// Each case uses the same production path as the corresponding clean/polluted suite.
/// </summary>
public sealed class ClosedHtfCapture_MissingHtfTests
{
    [Fact]
    public async Task Backtest_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);

        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            ClosedHtfCaptureHarness.CreateDatasetMissingHtf(fixture),
            [fixture.Prepared],
            evaluationIndex: 0);

        AssertMissingOutcome(recording, fixture);
    }

    [Fact]
    public async Task Replay_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var replayEngine = new ReplayEngine(
            recording,
            ClosedHtfCaptureHarness.CreateAdaptiveParameterProvider(),
            ClosedHtfCaptureHarness.CreateApprovingRiskEngine(),
            new Mock<IAiIntegrationService>().Object,
            ClosedHtfCaptureHarness.CreateNoopExecutionProvider(),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var state = CreateReplayState(fixture);
        state.CurrentFrameIndex = 0;
        var step = await replayEngine.ProcessFrameAsync(state);

        Assert.Single(recording.Capture.Records);
        Assert.Single(step.StrategyResults);
        Assert.NotEmpty(fixture.LtfCandles);
        ClosedHtfCaptureHarness.AssertMissingHtfCapture(recording.Capture.Records[0], fixture.EvaluationTimeUtc);
        ClosedHtfCaptureHarness.AssertMtfDataUnavailable(step.StrategyResults[0]);
    }

    [Fact]
    public async Task HistoricalPaper_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var backtestEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);
        var paperEngine = new PaperTradingEngine(
            backtestEngine,
            new PaperExecutionProvider(ClosedHtfCaptureHarness.CreateNoopExecutionProvider()),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var state = ClosedHtfCapture_HistoricalPaperTests.CreatePaperState(
            fixture,
            Array.Empty<Candle>(),
            [fixture.Prepared],
            PaperTradingMode.HistoricalPaper);
        state.Dataset = ClosedHtfCaptureHarness.CreateDatasetMissingHtf(fixture);

        var decision = await paperEngine.ProcessNextCandleAsync(state);
        Assert.NotNull(decision);
        AssertMissingOutcome(recording, fixture);
    }

    [Fact]
    public async Task LivePaper_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var backtestEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);
        var paperEngine = new PaperTradingEngine(
            backtestEngine,
            new PaperExecutionProvider(ClosedHtfCaptureHarness.CreateNoopExecutionProvider()),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var liveState = ClosedHtfCapture_HistoricalPaperTests.CreatePaperState(
            fixture,
            Array.Empty<Candle>(),
            [fixture.Prepared],
            PaperTradingMode.LivePaper);
        liveState.Dataset = ClosedHtfCaptureHarness.CreateDatasetMissingHtf(fixture);

        var stateStore = new Mock<IPaperStateStore>();
        stateStore.Setup(s => s.TryGet(liveState.Session.Id, out It.Ref<PaperSessionState?>.IsAny))
            .Returns((long _, out PaperSessionState? state) =>
            {
                state = liveState;
                return true;
            });

        var sessionRepository = new Mock<Application.Abstractions.IPaperTradingSessionRepository>();
        sessionRepository.Setup(r => r.GetRunningSessionIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([liveState.Session.Id]);
        sessionRepository.Setup(r => r.UpdateAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessionRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dataLoader = new Mock<IBacktestDataLoader>();
        dataLoader.Setup(l => l.LoadSymbolTimeframeAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ClosedHtfCaptureHarness.CreateDatasetMissingHtf(fixture));

        var persistence = new Mock<IPaperPersistenceService>();
        persistence.Setup(p => p.PersistCandleAsync(
                It.IsAny<PaperSessionState>(),
                It.IsAny<CandleProcessResult>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        persistence.Setup(p => p.SyncAccountAsync(It.IsAny<PaperSessionState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new LivePaperCandleHandler(
            stateStore.Object,
            sessionRepository.Object,
            dataLoader.Object,
            paperEngine,
            persistence.Object,
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var candle = fixture.EvaluationCandle;
        var update = new LiveCandleUpdate
        {
            ExchangeId = 1,
            SymbolId = ClosedHtfCaptureHarness.SymbolId,
            Symbol = ClosedHtfCaptureHarness.SymbolName,
            Timeframe = ClosedHtfCaptureHarness.ExecutionTimeframe,
            OpenTimeUtc = candle.OpenTimeUtc,
            CloseTimeUtc = candle.CloseTimeUtc,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
            QuoteVolume = candle.Volume,
            TradeCount = 1,
            IsClosed = true,
            EventTimeUtc = candle.CloseTimeUtc,
            Source = "test"
        };

        await handler.HandleClosedCandleAsync(update, fixture.EvaluationCandle);
        AssertMissingOutcome(recording, fixture);
    }

    [Fact]
    public async Task ManualEval_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var context = new StrategyContext
        {
            SymbolId = ClosedHtfCaptureHarness.SymbolId,
            Symbol = ClosedHtfCaptureHarness.SymbolName,
            Timeframe = ClosedHtfCaptureHarness.ExecutionTimeframe,
            HigherTimeframe = ClosedHtfCaptureHarness.HigherTimeframe,
            MarketRegime = MarketRegime.Breakout,
            Candles = fixture.LtfCandles,
            HigherTimeframeCandles = Array.Empty<Candle>(),
            IndicatorSnapshot = fixture.IndicatorSnapshots[fixture.EvaluationCandle.Id],
            EvaluatedAtUtc = fixture.EvaluationTimeUtc
        };

        await recording.EvaluateAsync([fixture.Prepared.Plugin], context);
        AssertMissingOutcome(recording, fixture);
    }

    [Fact]
    public async Task Benchmark_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable()
    {
        await new ClosedHtfCapture_BenchmarkTests()
            .BenchmarkWorker_RealChain_MissingMappedHtf_NoLtfFallback_MtfDataUnavailable();
    }

    private static void AssertMissingOutcome(
        ClosedHtfCaptureHarness.RecordingStrategyEngine recording,
        ClosedHtfCaptureHarness.Fixture fixture)
    {
        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        Assert.NotEmpty(fixture.LtfCandles);
        ClosedHtfCaptureHarness.AssertMissingHtfCapture(recording.Capture.Records[0], fixture.EvaluationTimeUtc);
        ClosedHtfCaptureHarness.AssertMtfDataUnavailable(recording.Results[0]);
    }

    private static ReplayRuntimeState CreateReplayState(ClosedHtfCaptureHarness.Fixture fixture)
    {
        var dataset = ClosedHtfCaptureHarness.CreateDatasetMissingHtf(fixture);
        var session = new ReplaySession
        {
            Id = 8,
            Name = "ClosedHtf Replay Missing HTF",
            TradingSessionId = 1,
            ExchangeId = 1,
            SymbolId = ClosedHtfCaptureHarness.SymbolId,
            Timeframe = ClosedHtfCaptureHarness.ExecutionTimeframe,
            FromUtc = fixture.LtfCandles[0].OpenTimeUtc,
            ToUtc = fixture.EvaluationTimeUtc,
            Status = ReplaySessionStatus.Created,
            InitialBalance = 10_000m,
            RiskProfileId = 1,
            TotalFrames = 1,
            CreatedAtUtc = DateTime.UtcNow
        };

        var settings = new ReplaySessionSettings
        {
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0005m,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 0m,
            SlippagePercent = 0m,
            ExecutionMode = ExecutionMode.MarketFill,
            StrategyIds = [42]
        };

        return ReplayEngine.CreateRuntimeState(
            settings,
            session,
            dataset,
            [fixture.Prepared],
            [],
            new Symbol
            {
                Id = ClosedHtfCaptureHarness.SymbolId,
                ExchangeId = 1,
                SymbolName = ClosedHtfCaptureHarness.SymbolName
            });
    }
}
