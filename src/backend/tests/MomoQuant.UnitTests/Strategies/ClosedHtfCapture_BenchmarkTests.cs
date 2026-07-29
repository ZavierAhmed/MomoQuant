using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1D — closed-HTF capture via StrategyBenchmarkRunner.ExecuteAsync → IBacktestRunner.RunAsync.
/// </summary>
public sealed class ClosedHtfCapture_BenchmarkTests
{
    [Fact]
    public async Task BenchmarkWorker_ExecuteAsync_ReachesBacktestRunner_AndClosedHtfCaptureIgnoresPollution()
    {
        var fixture = ClosedHtfCaptureHarness.CreateAdaptiveFixture();
        var cleanHtf = fixture.CleanHtfCandles.ToList();
        var strategies = new[] { fixture.Prepared };

        var cleanRecording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var cleanEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(cleanRecording);
        await cleanEngine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            ClosedHtfCaptureHarness.CreateDataset(fixture, cleanHtf),
            strategies,
            evaluationIndex: 0);
        var cleanCapture = Assert.Single(cleanRecording.Capture.Records);
        var cleanResult = Assert.Single(cleanRecording.Results);
        ClosedHtfCaptureHarness.AssertCaptureClosedOnly(cleanCapture, fixture.EvaluationTimeUtc);

        var pollutedRecording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var pollutedEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(pollutedRecording);
        var pollutedHtf = ClosedHtfCaptureHarness.PolluteHtf(cleanHtf, fixture.EvaluationTimeUtc);
        var runAsyncCalls = 0;

        var backtestRunner = new Mock<IBacktestRunner>(MockBehavior.Strict);
        backtestRunner
            .Setup(r => r.RunAsync(It.IsAny<RunBacktestRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (RunBacktestRequest request, CancellationToken ct) =>
            {
                Assert.Equal(42, Assert.Single(request.StrategyIds));
                runAsyncCalls++;
                await pollutedEngine.ProcessCandleAtIndexAsync(
                    ClosedHtfCaptureHarness.CreateBacktestContext(),
                    ClosedHtfCaptureHarness.CreateDataset(fixture, pollutedHtf),
                    strategies,
                    evaluationIndex: 0,
                    ct);
                return ServiceResult<RunBacktestResponse>.Ok(new RunBacktestResponse
                {
                    BacktestRunId = 1,
                    Status = "Completed",
                    StartedAtUtc = DateTime.UtcNow,
                    Summary = new BacktestSummaryDto()
                });
            });

        var adaptive = fixture.Prepared.Strategy;
        adaptive.Id = 42;
        var run = BuildPendingRun(91);
        var runItem = new StrategyBenchmarkRunItem
        {
            Id = 501,
            BenchmarkRunId = 91,
            StrategyId = 42,
            StrategyCode = adaptive.Code.ToCode(),
            StrategyName = adaptive.Name,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = "5m",
            Status = StrategyBenchmarkRunItemStatus.Pending,
            CandleCount = fixture.LtfCandles.Count
        };

        var runner = BuildRunner(run, runItem, adaptive, backtestRunner.Object);
        await runner.ExecuteAsync(91);

        Assert.True(runAsyncCalls >= 1, "ExecuteAsync must invoke IBacktestRunner.RunAsync");
        ClosedHtfCaptureHarness.AssertCleanVsPollutedRun(
            pollutedRecording,
            fixture.EvaluationTimeUtc,
            cleanCapture,
            cleanResult);
    }

    private static StrategyBenchmarkRun BuildPendingRun(long id) => new()
    {
        Id = id,
        Name = "Closed HTF benchmark",
        Status = StrategyBenchmarkStatus.Pending,
        ExchangeId = 1,
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
