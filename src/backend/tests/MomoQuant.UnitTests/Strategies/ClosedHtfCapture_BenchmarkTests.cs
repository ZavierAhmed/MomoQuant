using MomoQuant.Application.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — closed-HTF capture for Benchmark worker path.
/// StrategyBenchmarkRunner executes via IBacktestRunner.RunAsync → BacktestEngine;
/// this suite drives BacktestEngine.ProcessCandleAtIndexAsync the same way and documents
/// equivalence to the benchmark worker evaluation path.
/// </summary>
public sealed class ClosedHtfCapture_BenchmarkTests
{
    [Fact]
    public async Task BenchmarkWorker_EquivalentBacktestEngine_ClosedHtfCapture_IgnoresOpenAndFuturePollution()
    {
        // Equivalence note: StrategyBenchmarkRunner scopes IBacktestRunner.RunAsync which
        // loads/enriches a BacktestDataset then calls BacktestEngine.RunDatasetAsync /
        // ProcessCandleAtIndex*. HTF slicing is BuildContextHigherTimeframe → SliceClosedThrough
        // inside BacktestEngine — identical to this capture proof.
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var strategies = new[] { fixture.Prepared };

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = ClosedHtfCaptureHarness.CreateBacktestEngine(recording);

        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            ClosedHtfCaptureHarness.CreateDataset(fixture, cleanHtf),
            strategies,
            evaluationIndex: 0);

        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        var cleanCapture = recording.Capture.Records[0];
        var cleanResult = recording.Results[0];
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        recording.Clear();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            ClosedHtfCaptureHarness.CreateDataset(fixture, pollutedHtf),
            strategies,
            evaluationIndex: 0);

        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            recording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
    }
}
