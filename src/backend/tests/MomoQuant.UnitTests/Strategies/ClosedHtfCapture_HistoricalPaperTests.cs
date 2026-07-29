using MomoQuant.Application.Backtesting;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — closed-HTF capture via Historical PaperTradingEngine path
/// (PaperTradingEngine → BacktestEngine.ProcessCandleAtIndexAsync).
/// </summary>
public sealed class ClosedHtfCapture_HistoricalPaperTests
{
    [Fact]
    public async Task HistoricalPaper_ProcessNextCandle_ClosedHtfCapture_IgnoresOpenAndFuturePollution()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var strategies = new[] { fixture.Prepared };

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var backtestEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);
        var paperEngine = new PaperTradingEngine(
            backtestEngine,
            new PaperExecutionProvider(ClosedHtfCaptureHarness.CreateNoopExecutionProvider()),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var cleanState = CreatePaperState(fixture, cleanHtf, strategies, PaperTradingMode.HistoricalPaper);
        var cleanDecision = await paperEngine.ProcessNextCandleAsync(cleanState);
        Assert.NotNull(cleanDecision);
        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        var cleanCapture = recording.Capture.Records[0];
        var cleanResult = recording.Results[0];
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        recording.Clear();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        var pollutedState = CreatePaperState(fixture, pollutedHtf, strategies, PaperTradingMode.HistoricalPaper);
        var pollutedDecision = await paperEngine.ProcessNextCandleAsync(pollutedState);
        Assert.NotNull(pollutedDecision);

        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            recording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
    }

    internal static PaperSessionState CreatePaperState(
        ClosedHtfCaptureHarness.Fixture fixture,
        IReadOnlyList<Candle> htf,
        IReadOnlyList<PreparedStrategy> strategies,
        PaperTradingMode mode)
    {
        var dataset = ClosedHtfCaptureHarness.CreateDataset(fixture, htf);
        var session = new PaperTradingSession
        {
            Id = 11,
            Name = "ClosedHtf Paper",
            PaperAccountId = 1,
            TradingSessionId = 1,
            Status = PaperSessionStatus.Running,
            Mode = mode,
            ExchangeId = 1,
            RiskProfileId = 1,
            ExecutionMode = ExecutionMode.MarketFill,
            TotalCandles = 1,
            CreatedAtUtc = DateTime.UtcNow
        };

        return new PaperSessionState
        {
            Session = session,
            Account = new PaperAccount
            {
                Id = 1,
                Name = "ClosedHtf Account",
                InitialBalance = 10_000m,
                CurrentBalance = 10_000m,
                CurrentEquity = 10_000m,
                Currency = "USDT",
                CreatedAtUtc = DateTime.UtcNow
            },
            Settings = new PaperSessionSettings
            {
                MakerFeeRate = 0.0002m,
                TakerFeeRate = 0.0005m,
                OrderExpiryCandles = 3,
                UseAiScoring = false,
                MinConfidenceScore = 0m,
                SlippagePercent = 0m,
                ExecutionMode = ExecutionMode.MarketFill,
                StrategyIds = [42],
                SymbolIds = [ClosedHtfCaptureHarness.SymbolId],
                Timeframes = [ClosedHtfCaptureHarness.ExecutionTimeframe]
            },
            Context = ClosedHtfCaptureHarness.CreateBacktestContext(),
            Dataset = dataset,
            Strategies = strategies,
            RiskRules = [],
            NextEvaluationIndex = 0
        };
    }
}
