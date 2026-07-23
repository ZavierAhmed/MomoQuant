using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

/// <summary>
/// Milestone 23.0B — actual StrategyLabRunner equivalence (copied vs prefix-view) + performance harness.
/// Uses a test-only deterministic plugin; does not change production strategy semantics.
/// </summary>
public sealed class StrategyLabRunnerEquivalenceAndPerformanceTests
{
    private const int CandleCount = 10_000;
    private const int SignalEvery = 250;
    private const int Iterations = 3;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public StrategyLabRunnerEquivalenceAndPerformanceTests(Xunit.Abstractions.ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public async Task StrategyLabRunner_CopiedVsPrefixView_ProducesExactCandidateEquivalence()
    {
        var candles = BuildDeterministicCandles(CandleCount);
        var dataset = BuildDataset(candles);

        var copied = await RunOnceAsync(new CopiedListStrategyLabCandleWindowFactory(), dataset, runId: 230_001);
        var prefix = await RunOnceAsync(new CandlePrefixViewStrategyLabCandleWindowFactory(), dataset, runId: 230_002);

        Assert.True(
            copied.Candidates.Count >= 4,
            $"Expected several candidates, got {copied.Candidates.Count}. runStatus={copied.RunStatus} err={copied.ErrorMessage}");
        Assert.Equal(StrategyLabRunStatus.Completed, copied.RunStatus);
        Assert.Equal(copied.Candidates.Count, prefix.Candidates.Count);

        var left = copied.Candidates.Select(CanonicalSnapshot).ToList();
        var right = prefix.Candidates.Select(CanonicalSnapshot).ToList();
        Assert.Equal(left, right);

        var hashLeft = HashSnapshots(left);
        var hashRight = HashSnapshots(right);
        Assert.Equal(hashLeft, hashRight);
        _output.WriteLine($"EQUIV candidates={copied.Candidates.Count} fingerprintHash={hashLeft}");

        Assert.IsType<CandlePrefixView>(
            new CandlePrefixViewStrategyLabCandleWindowFactory().CreateVisibleWindow(candles, 10));
    }

    [Fact]
    public async Task StrategyLabRunner_PerformanceHarness_CopiedAllocatesMateriallyMoreThanPrefixView()
    {
        var candles = BuildDeterministicCandles(CandleCount);
        var dataset = BuildDataset(candles);

        // Warmup both paths
        _ = await RunOnceAsync(new CopiedListStrategyLabCandleWindowFactory(), dataset, runId: 1);
        _ = await RunOnceAsync(new CandlePrefixViewStrategyLabCandleWindowFactory(), dataset, runId: 2);

        var copiedSamples = new List<PerfSample>(Iterations);
        var prefixSamples = new List<PerfSample>(Iterations);

        for (var i = 0; i < Iterations; i++)
        {
            // Alternate order to reduce JIT/order bias
            if (i % 2 == 0)
            {
                copiedSamples.Add(await MeasureAsync(new CopiedListStrategyLabCandleWindowFactory(), dataset, 100 + i));
                prefixSamples.Add(await MeasureAsync(new CandlePrefixViewStrategyLabCandleWindowFactory(), dataset, 200 + i));
            }
            else
            {
                prefixSamples.Add(await MeasureAsync(new CandlePrefixViewStrategyLabCandleWindowFactory(), dataset, 200 + i));
                copiedSamples.Add(await MeasureAsync(new CopiedListStrategyLabCandleWindowFactory(), dataset, 100 + i));
            }
        }

        var copiedMedian = Median(copiedSamples);
        var prefixMedian = Median(prefixSamples);

        Assert.Equal(copiedMedian.Candidates, prefixMedian.Candidates);
        Assert.True(copiedMedian.Candidates >= 4, $"Expected candidates from full runner. got={copiedMedian.Candidates}");

        // Candle-window-only measurement (isolates factory allocation; CI-asserted).
        var windowCopied = MeasureWindowOnly(new CopiedListStrategyLabCandleWindowFactory(), candles);
        var windowPrefix = MeasureWindowOnly(new CandlePrefixViewStrategyLabCandleWindowFactory(), candles);
        Assert.True(
            windowCopied.AllocatedBytes > windowPrefix.AllocatedBytes * 2
            || windowCopied.AllocatedBytes > windowPrefix.AllocatedBytes + 1_000_000,
            $"Expected copied window allocation materially higher. copied={windowCopied.AllocatedBytes} prefix={windowPrefix.AllocatedBytes}");
        Assert.IsType<CandlePrefixView>(
            new CandlePrefixViewStrategyLabCandleWindowFactory().CreateVisibleWindow(candles, candles.Count));

        // Surfaced for milestone evidence.
        Assert.True(copiedMedian.ElapsedMs >= 0);
        Assert.True(prefixMedian.ElapsedMs >= 0);
        Assert.True(prefixMedian.CandlesPerSec > 0);
        Assert.True(CandleCount == 10_000);
        var allocReductionPct = windowCopied.AllocatedBytes <= 0
            ? 0
            : 100.0 * (windowCopied.AllocatedBytes - windowPrefix.AllocatedBytes) / windowCopied.AllocatedBytes;
        var runtimeDiffPct = copiedMedian.ElapsedMs <= 0
            ? 0
            : 100.0 * (prefixMedian.ElapsedMs - copiedMedian.ElapsedMs) / copiedMedian.ElapsedMs;
        _output.WriteLine(
            $"FULL median copied: ms={copiedMedian.ElapsedMs:F1} alloc={copiedMedian.AllocatedBytes} gen0={copiedMedian.Gen0} gen1={copiedMedian.Gen1} gen2={copiedMedian.Gen2} cps={copiedMedian.CandlesPerSec:F1} cands={copiedMedian.Candidates}");
        _output.WriteLine(
            $"FULL median prefix: ms={prefixMedian.ElapsedMs:F1} alloc={prefixMedian.AllocatedBytes} gen0={prefixMedian.Gen0} gen1={prefixMedian.Gen1} gen2={prefixMedian.Gen2} cps={prefixMedian.CandlesPerSec:F1} cands={prefixMedian.Candidates}");
        _output.WriteLine(
            $"WINDOW copiedAlloc={windowCopied.AllocatedBytes} prefixAlloc={windowPrefix.AllocatedBytes} copiedMs={windowCopied.ElapsedMs:F1} prefixMs={windowPrefix.ElapsedMs:F1} allocReductionPct={allocReductionPct:F1} runtimeDiffPct={runtimeDiffPct:F1}");
    }

    private static PerfSample MeasureWindowOnly(IStrategyLabCandleWindowFactory factory, IReadOnlyList<Candle> candles)
    {
        // Warmup
        _ = RunWindowLoop(factory, candles);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var checksum = RunWindowLoop(factory, candles);
        sw.Stop();
        _ = checksum;
        return new PerfSample(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocBefore,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            candles.Count,
            0,
            candles.Count / Math.Max(sw.Elapsed.TotalSeconds, 0.0001));
    }

    private static long RunWindowLoop(IStrategyLabCandleWindowFactory factory, IReadOnlyList<Candle> candles)
    {
        long checksum = 0;
        var window = factory.CreateVisibleWindow(candles, 0);
        for (var i = 0; i < candles.Count; i++)
        {
            if (window is CandlePrefixView prefix)
            {
                prefix.SetVisibleCount(i + 1);
            }
            else
            {
                window = factory.CreateVisibleWindow(candles, i + 1);
            }

            checksum = unchecked(checksum * 31 + window[^1].Close.GetHashCode() + i);
        }

        return checksum;
    }

    private static async Task<RunResult> RunOnceAsync(
        IStrategyLabCandleWindowFactory windowFactory,
        BacktestDataset dataset,
        long runId)
    {
        var candidates = new List<StrategyResearchCandidate>();
        var run = CreateRun(runId, dataset);
        var runner = CreateRunner(windowFactory, run, dataset, candidates);
        await runner.ExecuteAsync(runId);
        return new RunResult(
            candidates.OrderBy(c => c.SetupDetectedAtUtc).ThenBy(c => c.SetupFingerprint).ToList(),
            run.Status,
            run.ErrorMessage);
    }

    private static async Task<PerfSample> MeasureAsync(
        IStrategyLabCandleWindowFactory windowFactory,
        BacktestDataset dataset,
        long runId)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var result = await RunOnceAsync(windowFactory, dataset, runId);
        sw.Stop();
        var alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

        return new PerfSample(
            sw.Elapsed.TotalMilliseconds,
            alloc,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            CandleCount,
            result.Candidates.Count,
            CandleCount / Math.Max(sw.Elapsed.TotalSeconds, 0.0001));
    }

    private static StrategyLabRunner CreateRunner(
        IStrategyLabCandleWindowFactory windowFactory,
        StrategyLabRun run,
        BacktestDataset dataset,
        List<StrategyResearchCandidate> sink)
    {
        var runRepo = new Mock<IStrategyLabRunRepository>();
        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepo.Setup(r => r.UpdateAsync(It.IsAny<StrategyLabRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var candidateRepo = new Mock<IStrategyResearchCandidateRepository>();
        candidateRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<StrategyResearchCandidate>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<StrategyResearchCandidate>, CancellationToken>((items, _) => sink.AddRange(items))
            .Returns(Task.CompletedTask);

        var dataLoader = new Mock<IBacktestDataLoader>();
        dataLoader.Setup(d => d.LoadSymbolTimeframeAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<Timeframe>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataset);

        var strategyEntity = new Strategy
        {
            Id = 77,
            Code = StrategyCode.DonchianBreakout,
            Name = "Deterministic Test Proxy",
            Version = "1.0.0-test",
            IsEnabled = true
        };
        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(s => s.GetByCodeAsync(StrategyCode.DonchianBreakout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyEntity);

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(StrategyCode.DonchianBreakout))
            .Returns(new DeterministicNthCandleStrategy(SignalEvery));

        var requirements = new Mock<IStrategyDataRequirementService>();
        requirements.Setup(r => r.GetByStrategyIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<StrategyDataRequirementDto>.Fail("use-default-warmup"));

        var coverage = new Mock<IHistoricalCandleCoverageService>();
        coverage
            .Setup(c => c.EnsureCoverageAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<Func<HistoricalCoverageProgress, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<HistoricalCandleCoverageResult>.Ok(new HistoricalCandleCoverageResult
            {
                Coverage = new CandleCoverageDto
                {
                    Symbol = "TESTUSDT",
                    Exchange = "BINANCE",
                    Timeframe = "5m",
                    RequiredFromUtc = run.FromUtc,
                    RequiredToUtc = run.ToUtc,
                    AvailableFromUtc = dataset.Candles[0].OpenTimeUtc,
                    AvailableToUtc = dataset.Candles[^1].OpenTimeUtc,
                    CandleCount = dataset.Candles.Count,
                    CoverageStatus = "Complete"
                },
                FinalCoverageStatus = "Complete",
                ExistingCandleCount = dataset.Candles.Count
            }));

        var riskRules = new Mock<IRiskRuleRepository>();
        riskRules.Setup(r => r.GetByProfileIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RiskRule>());
        var riskProfiles = new Mock<IRiskProfileRepository>();

        var confidence = new Mock<ICandidateConfidenceScorer>();
        confidence.Setup(c => c.Score(It.IsAny<CandidateConfidenceContext>()))
            .Returns(new CandidateConfidenceResult
            {
                Score = 90m,
                Explanation = "test",
                ModelVersion = "test"
            });

        return new StrategyLabRunner(
            runRepo.Object,
            candidateRepo.Object,
            dataLoader.Object,
            strategyRepo.Object,
            registry.Object,
            requirements.Object,
            coverage.Object,
            riskRules.Object,
            riskProfiles.Object,
            new PositionSizingService(),
            confidence.Object,
            windowFactory);
    }

    private static StrategyLabRun CreateRun(long id, BacktestDataset dataset)
    {
        var from = dataset.Candles[0].OpenTimeUtc;
        var to = dataset.Candles[^1].OpenTimeUtc;
        return new StrategyLabRun
        {
            Id = id,
            Name = $"m230b-eq-{id}",
            StrategyCode = StrategyCodes.DonchianBreakout,
            StrategyVersion = "1.0.0-test",
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "TESTUSDT",
            Timeframe = "5m",
            FromUtc = from,
            ToUtc = to,
            ExecutionMode = StrategyLabExecutionMode.RawStrategy,
            ParametersJson = "{}",
            InitialBalance = 10_000m,
            FeeSettingsJson = """{"takerFeeRate":0.0004}""",
            SlippageSettingsJson = """{"slippagePercent":0}""",
            Status = StrategyLabRunStatus.Created,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static BacktestDataset BuildDataset(IReadOnlyList<Candle> candles)
    {
        var indices = Enumerable.Range(0, candles.Count).ToList();
        return new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "TESTUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = indices
        };
    }

    private static List<Candle> BuildDeterministicCandles(int count)
    {
        var list = new List<Candle>(count);
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
        {
            var t = start.AddMinutes(5 * i);
            // Mild trend with periodic swings so long/short targets remain reachable.
            var px = 100m + (i % 40) * 0.15m + (i / 500) * 0.05m;
            list.Add(new Candle
            {
                Id = i + 1,
                ExchangeId = 1,
                SymbolId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = t,
                CloseTimeUtc = t.AddMinutes(5).AddTicks(-1),
                Open = px,
                High = px + 1.5m,
                Low = px - 1.5m,
                Close = px + ((i % 2 == 0) ? 0.25m : -0.25m),
                Volume = 10m + (i % 7),
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return list;
    }

    private static string CanonicalSnapshot(StrategyResearchCandidate c) =>
        string.Join('|',
            c.StrategyCode,
            c.StrategyVersion,
            c.SymbolId.ToString(),
            c.Timeframe,
            c.Direction.ToString(),
            c.SetupDetectedAtUtc.ToString("O"),
            c.ProposedEntryTimeUtc.ToString("O"),
            c.ProposedEntryPrice.ToString("G29"),
            c.StopLoss.ToString("G29"),
            c.Target1.ToString("G29"),
            c.SetupFingerprint,
            c.RawOutcomeStatus.ToString(),
            c.RawExitPrice?.ToString("G29") ?? "",
            c.RawExitTimeUtc?.ToString("O") ?? "",
            c.RawGrossPnl?.ToString("G29") ?? "",
            c.RawNetPnl?.ToString("G29") ?? "",
            c.ConfidenceDecision?.ToString() ?? "",
            c.CandidateStatus.ToString(),
            c.StrategyReason);

    private static string HashSnapshots(IReadOnlyList<string> snapshots)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', snapshots));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static PerfSample Median(IReadOnlyList<PerfSample> samples)
    {
        var byAlloc = samples.OrderBy(s => s.AllocatedBytes).ToList();
        var mid = byAlloc[byAlloc.Count / 2];
        var byMs = samples.OrderBy(s => s.ElapsedMs).ToList();
        var msMid = byMs[byMs.Count / 2];
        return mid with
        {
            ElapsedMs = msMid.ElapsedMs,
            CandlesPerSec = msMid.CandlesPerSec
        };
    }

    private sealed record RunResult(
        IReadOnlyList<StrategyResearchCandidate> Candidates,
        StrategyLabRunStatus RunStatus,
        string? ErrorMessage);

    private sealed record PerfSample(
        double ElapsedMs,
        long AllocatedBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        int Candles,
        int Candidates,
        double CandlesPerSec);

    /// <summary>
    /// Test-only plugin: emits alternating long/short entries every N candles with stable fingerprints.
    /// Registered only in this harness via mocked IStrategyRegistry.
    /// </summary>
    private sealed class DeterministicNthCandleStrategy : StrategyBase
    {
        private readonly int _every;

        public DeterministicNthCandleStrategy(int every) => _every = every;

        public override StrategyCode Code => StrategyCode.DonchianBreakout;
        public override string Name => "Deterministic Nth Candle (test)";
        public override string Description => "Test-only candidate emitter for StrategyLabRunner equivalence.";
        public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
            [MarketRegime.Trending, MarketRegime.Breakout];
        public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
            [Timeframe.M1, Timeframe.M3, Timeframe.M5, Timeframe.M15, Timeframe.H1];

        public override StrategySignalResult Evaluate(StrategyContext context)
        {
            var index = context.CurrentCandleIndex ?? (context.Candles.Count - 1);
            if (index < _every || index % _every != 0)
            {
                return NoTrade("skip");
            }

            var candle = context.CurrentCandle!;
            var longSide = (index / _every) % 2 == 0;
            var direction = longSide ? TradeDirection.Long : TradeDirection.Short;
            var entry = candle.Close;
            var stop = longSide ? entry - 1m : entry + 1m;
            var target = longSide ? entry + 2m : entry - 2m;
            var fp = $"det-{index}-{direction}";
            var raw = JsonSerializer.Serialize(new { setupFingerprint = fp });
            return Entry(direction, 80m, 80m, entry, stop, target, "deterministic-entry", raw);
        }
    }
}
