using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — closed-HTF capture via manual StrategyService.EvaluateAsync path.
/// Mocked candle repo returns clean then polluted HTF series (including open/future beyond T)
/// to prove LoadHigherTimeframeContextAsync → SliceClosedThrough excludes pollution.
/// </summary>
public sealed class ClosedHtfCapture_ManualEvalTests
{
    [Fact]
    public async Task ManualEvaluateAsync_ClosedHtfCapture_IgnoresOpenAndFuturePollution()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var htfCall = 0;
        var service = CreateService(fixture, recording, () =>
        {
            htfCall++;
            return htfCall == 1 ? cleanHtf : pollutedHtf;
        });

        var request = new StrategyEvaluationRequest
        {
            SymbolId = ClosedHtfCaptureHarness.SymbolId,
            Timeframe = "5m",
            CandleId = fixture.EvaluationCandle.Id,
            MarketRegime = "Trending",
            StrategyIds = [42]
        };

        var cleanResponse = await service.EvaluateAsync(request);
        Assert.True(cleanResponse.Succeeded);
        Assert.Single(recording.Capture.Records);
        Assert.Single(recording.Results);
        Assert.Single(cleanResponse.Data!.Results);
        var cleanCapture = recording.Capture.Records[0];
        var cleanResult = recording.Results[0];
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        recording.Clear();
        var pollutedResponse = await service.EvaluateAsync(request);
        Assert.True(pollutedResponse.Succeeded);
        Assert.Single(pollutedResponse.Data!.Results);

        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            recording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
        ClosedHtfCaptureHarness.AssertIdenticalEvaluationOutcomes(
            cleanResponse.Data.Results[0],
            pollutedResponse.Data!.Results[0]);
        Assert.Equal(2, htfCall);
    }

    private static StrategyService CreateService(
        ClosedHtfCaptureHarness.Fixture fixture,
        ClosedHtfCaptureHarness.RecordingStrategyEngine recording,
        Func<IReadOnlyList<Candle>> htfFactory)
    {
        var strategy = fixture.Prepared.Strategy;
        var symbol = new Symbol
        {
            Id = ClosedHtfCaptureHarness.SymbolId,
            SymbolName = ClosedHtfCaptureHarness.SymbolName,
            ExchangeId = 1
        };

        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(r => r.GetByIdAsync(ClosedHtfCaptureHarness.SymbolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(r => r.GetByIdAsync(fixture.EvaluationCandle.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.EvaluationCandle);
        candleRepository.Setup(r => r.GetRecentCandlesAsync(
                ClosedHtfCaptureHarness.SymbolId,
                ClosedHtfCaptureHarness.ExecutionTimeframe,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.LtfCandles);
        // Intentionally returns polluted open/future candles even when toUtc is T —
        // proves SliceClosedThrough still excludes them on the manual path.
        candleRepository.Setup(r => r.GetCandlesChronologicalAsync(
                ClosedHtfCaptureHarness.SymbolId,
                ClosedHtfCaptureHarness.HigherTimeframe,
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => htfFactory());

        var indicatorRepository = new Mock<IIndicatorSnapshotRepository>();
        indicatorRepository.Setup(r => r.GetByKeyAsync(
                ClosedHtfCaptureHarness.SymbolId,
                ClosedHtfCaptureHarness.ExecutionTimeframe,
                fixture.EvaluationCandle.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.IndicatorSnapshots[fixture.EvaluationCandle.Id]);
        indicatorRepository.Setup(r => r.GetRecentForSymbolAsync(
                ClosedHtfCaptureHarness.SymbolId,
                ClosedHtfCaptureHarness.ExecutionTimeframe,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([fixture.IndicatorSnapshots[fixture.EvaluationCandle.Id]]);

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout))
            .Returns(new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy());

        var parameterProvider = new Mock<IStrategyParameterProvider>();
        parameterProvider.Setup(p => p.GetParametersAsync(
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract());

        return new StrategyService(
            strategyRepository.Object,
            new Mock<IStrategyParameterRepository>().Object,
            registry.Object,
            recording,
            parameterProvider.Object,
            new Mock<IStrategyDataRequirementService>().Object,
            new Mock<IStrategyParameterDefinitionProvider>().Object,
            candleRepository.Object,
            indicatorRepository.Object,
            symbolRepository.Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<IAuditService>().Object);
    }
}
