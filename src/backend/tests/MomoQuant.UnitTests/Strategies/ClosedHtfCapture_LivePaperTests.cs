using Moq;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — closed-HTF capture via Live Paper path.
/// Uses LivePaperCandleHandler (reload dataset → enrich → PaperTradingEngine → BacktestEngine)
/// with a mocked loader returning clean then polluted HTF series.
/// </summary>
public sealed class ClosedHtfCapture_LivePaperTests
{
    [Fact]
    public async Task LivePaper_HandleClosedCandle_ClosedHtfCapture_IgnoresOpenAndFuturePollution()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        var strategies = new[] { fixture.Prepared };

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var backtestEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);
        var paperEngine = new PaperTradingEngine(
            backtestEngine,
            new PaperExecutionProvider(ClosedHtfCaptureHarness.CreateNoopExecutionProvider()),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var liveState = ClosedHtfCapture_HistoricalPaperTests.CreatePaperState(
            fixture,
            cleanHtf,
            strategies,
            PaperTradingMode.LivePaper);

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

        var loadCall = 0;
        var dataLoader = new Mock<IBacktestDataLoader>();
        dataLoader.Setup(l => l.LoadSymbolTimeframeAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                loadCall++;
                var htf = loadCall == 1 ? cleanHtf : pollutedHtf;
                // Live handler evaluates the last EvaluationIndices entry.
                return ClosedHtfCaptureHarness.CreateDataset(fixture, htf);
            });

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
        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        var cleanCapture = recording.Capture.Records[0];
        var cleanResult = recording.Results[0];
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        // Allow second live tick (handler dedupes on LastProcessedCandleId / time).
        // Context is init-only; reset mutable counters on the same context instance.
        liveState.LastProcessedCandleId = null;
        liveState.LastProcessedCandleTimeUtc = null;
        liveState.Context.OpenPositions.Clear();
        liveState.Context.Signals.Clear();
        liveState.Context.AiDecisions.Clear();
        liveState.Context.RiskDecisions.Clear();
        liveState.Context.Orders.Clear();
        liveState.Context.OrderFills.Clear();
        liveState.Context.Trades.Clear();
        liveState.Context.MissedOrderLinks.Clear();
        liveState.Context.NoTradeReasonEvents.Clear();
        liveState.Context.Balance = 10_000m;
        liveState.Context.PeakEquity = 10_000m;
        recording.Clear();

        await handler.HandleClosedCandleAsync(update, fixture.EvaluationCandle);

        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            recording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
        Assert.Equal(2, loadCall);
    }
}
