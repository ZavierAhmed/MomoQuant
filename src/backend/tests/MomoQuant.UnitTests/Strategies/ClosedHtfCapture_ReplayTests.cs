using Moq;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — closed-HTF capture via ReplayEngine.ProcessFrameAsync production path.
/// </summary>
public sealed class ClosedHtfCapture_ReplayTests
{
    [Fact]
    public async Task Replay_ProcessFrame_ClosedHtfCapture_IgnoresOpenAndFuturePollution()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var strategies = new[] { fixture.Prepared };

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var replayEngine = new ReplayEngine(
            recording,
            ClosedHtfCaptureHarness.CreateAdaptiveParameterProvider(),
            ClosedHtfCaptureHarness.CreateApprovingRiskEngine(),
            new Mock<IAiIntegrationService>().Object,
            ClosedHtfCaptureHarness.CreateNoopExecutionProvider(),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

        var cleanState = CreateReplayState(fixture, cleanHtf, strategies);
        cleanState.CurrentFrameIndex = 0;
        var cleanStep = await replayEngine.ProcessFrameAsync(cleanState);

        Assert.Single(recording.Capture.Records);
        Assert.Single(cleanStep.StrategyResults);
        var cleanCapture = recording.Capture.Records[0];
        var cleanResult = cleanStep.StrategyResults[0];
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        recording.Clear();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        var pollutedState = CreateReplayState(fixture, pollutedHtf, strategies);
        pollutedState.CurrentFrameIndex = 0;
        var pollutedStep = await replayEngine.ProcessFrameAsync(pollutedState);

        Assert.Single(recording.Capture.Records);
        Assert.Single(pollutedStep.StrategyResults);
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(recording.Capture.Records[0], fixture.EvaluationTimeUtc);
        ClosedHtfCaptureHarness.AssertIdenticalCaptures(cleanCapture, recording.Capture.Records[0]);
        ClosedHtfCaptureHarness.AssertIdenticalEvaluationOutcomes(cleanResult, pollutedStep.StrategyResults[0]);
    }

    private static ReplayRuntimeState CreateReplayState(
        ClosedHtfCaptureHarness.Fixture fixture,
        IReadOnlyList<Candle> htf,
        IReadOnlyList<PreparedStrategy> strategies)
    {
        var dataset = ClosedHtfCaptureHarness.CreateDataset(fixture, htf);
        var session = new ReplaySession
        {
            Id = 7,
            Name = "ClosedHtf Replay",
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
            strategies,
            [],
            new Symbol
            {
                Id = ClosedHtfCaptureHarness.SymbolId,
                ExchangeId = 1,
                SymbolName = ClosedHtfCaptureHarness.SymbolName
            });
    }
}
