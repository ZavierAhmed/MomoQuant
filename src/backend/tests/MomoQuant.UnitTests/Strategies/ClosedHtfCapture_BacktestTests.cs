using MomoQuant.Application.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — closed-HTF capture via BacktestEngine production path.
/// </summary>
public sealed class ClosedHtfCapture_BacktestTests
{
    [Fact]
    public async Task Backtest_ProcessCandleAtIndex_ClosedHtfCapture_IgnoresOpenAndFuturePollution()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var strategies = new[] { fixture.Prepared };

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);

        var cleanDataset = ClosedHtfCaptureHarness.CreateDataset(fixture, cleanHtf);
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            cleanDataset,
            strategies,
            evaluationIndex: 0);

        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        var cleanCapture = recording.Capture.Records[0];
        var cleanResult = recording.Results[0];
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        recording.Clear();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        var pollutedDataset = ClosedHtfCaptureHarness.CreateDataset(fixture, pollutedHtf);
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            pollutedDataset,
            strategies,
            evaluationIndex: 0);

        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            recording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
    }
}
