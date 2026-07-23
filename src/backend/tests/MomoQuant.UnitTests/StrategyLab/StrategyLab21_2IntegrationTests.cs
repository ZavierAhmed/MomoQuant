using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Synthetic;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.StrategyLab;

public class StrategyLab21_2IntegrationTests
{
    [Fact]
    public void ExtractFingerprint_AcceptsPascalAndCamelCase()
    {
        var camel = StrategyLabRunner.ExtractFingerprint("""{"setupFingerprint":"abc123"}""");
        var pascal = StrategyLabRunner.ExtractFingerprint("""{"SetupFingerprint":"def456"}""");

        Assert.Equal("abc123", camel);
        Assert.Equal("def456", pascal);
    }

    [Fact]
    public void ImportRangeChunker_SplitsWarmupExpandedWindow()
    {
        var chunker = new BenchmarkImportRangeChunker();
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
        var chunks = chunker.CreateChunks(from, to, 30);

        Assert.True(chunks.Count >= 2);
        Assert.Equal(from, chunks[0].FromUtc);
        Assert.True(chunks.All(c => (c.ToUtc - c.FromUtc).TotalDays <= 30.0001));
    }

    [Fact]
    public void SyntheticAndDetectorFactory_UseSameCoreEngine()
    {
        var runner = new SyntheticCandleScenarioRunner();
        var breakout = runner.Run(new BullishBreakoutRetestScenario());
        var sweep = runner.Run(new BullishLiquiditySweepScenario());

        Assert.True(breakout.Passed);
        Assert.True(sweep.Passed);
        Assert.NotNull(PriceStructureDetectorFactory.Create("PRICE_STRUCTURE_BREAKOUT_RETEST"));
        Assert.NotNull(PriceStructureDetectorFactory.Create("PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM"));
    }

    [Fact]
    public void BreakoutFingerprints_DistinctSetupsDiffer_SameSetupStable()
    {
        var swingA = new ConfirmedSwing(10, 100m, true, DateTime.UtcNow);
        var swingB = new ConfirmedSwing(20, 110m, true, DateTime.UtcNow);
        var candles = Enumerable.Range(0, 40).Select(i => new Candle
        {
            Id = i + 1,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.M15,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(15 * i),
            CloseTimeUtc = DateTime.UtcNow.AddMinutes(15 * (i + 1)),
            Open = 100,
            High = 101,
            Low = 99,
            Close = 100,
            Volume = 1,
            QuoteVolume = 1,
            TradeCount = 1,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();

        var first = PriceStructureBreakoutRetestEvaluator.BuildFingerprint(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", 1, "15m", TradeDirection.Long, swingA, 12, 15, candles);
        var same = PriceStructureBreakoutRetestEvaluator.BuildFingerprint(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", 1, "15m", TradeDirection.Long, swingA, 12, 15, candles);
        var second = PriceStructureBreakoutRetestEvaluator.BuildFingerprint(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", 1, "15m", TradeDirection.Long, swingB, 22, 25, candles);

        Assert.Equal(first, same);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void LiquidityFingerprints_DistinctLevelsDiffer()
    {
        var swingA = new ConfirmedSwing(10, 100m, false, DateTime.UtcNow);
        var swingB = new ConfirmedSwing(18, 95m, false, DateTime.UtcNow);
        var candles = Enumerable.Range(0, 30).Select(i => new Candle
        {
            Id = i + 1,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = DateTime.UtcNow.AddHours(i),
            CloseTimeUtc = DateTime.UtcNow.AddHours(i + 1),
            Open = 100,
            High = 101,
            Low = 99,
            Close = 100,
            Volume = 1,
            QuoteVolume = 1,
            TradeCount = 1,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();

        var a = PriceStructureLiquiditySweepEvaluator.BuildFingerprint(
            "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM", 1, "1h", TradeDirection.Long, swingA, 12, candles);
        var b = PriceStructureLiquiditySweepEvaluator.BuildFingerprint(
            "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM", 1, "1h", TradeDirection.Long, swingB, 20, candles);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void BreakoutDetector_IncrementsFunnelThroughConfirmation()
    {
        var scenario = new BullishBreakoutRetestScenario();
        var candles = scenario.BuildCandles();
        var detector = new BreakoutRetestStrategyDetector();
        detector.Initialize(new Dictionary<string, string>());

        for (var i = 10; i < candles.Count; i++)
        {
            detector.ProcessCandle(candles.Take(i + 1).ToList(), scenario.StrategyCode, 1, "15m");
        }

        var funnel = detector.GetDiagnostics();
        Assert.True(funnel.ConfirmedSwingHighs + funnel.ConfirmedSwingLows > 0);
        Assert.True(funnel.BullishBreakoutsDetected + funnel.BearishBreakoutsDetected > 0);
        Assert.True(funnel.ValidRetests > 0);
        Assert.True(funnel.ConfirmationsPassed > 0);
        Assert.True(funnel.RawCandidatesCreated > 0);
    }

    [Fact]
    public void ZeroCandidateExplainer_FlagsSyntheticRuntimeMismatch()
    {
        var funnel = new PriceStructureFunnelDiagnostics
        {
            StrategyFamily = "BreakoutRetest",
            CandlesEvaluated = 1000,
            EligibleEvaluationCandles = 1000,
            WarmupCandlesLoaded = 100,
            CandlesLoaded = 1100
        };

        PriceStructureZeroCandidateExplainer.Populate(funnel, syntheticAllPassed: true);

        Assert.Equal("SyntheticRuntimeMismatch", funnel.ZeroCandidateClassification);
        Assert.Contains(funnel.RuntimeWarnings, w => w.Contains("DetectorRuntimeWarning") || w.Contains("SyntheticRuntimeMismatch"));
    }

    [Fact]
    public void CoverageProgress_DtoCarriesDiagnosticsFields()
    {
        var result = new HistoricalCandleCoverageResult
        {
            Coverage = new Application.Validation.Dtos.CandleCoverageDto
            {
                Symbol = "BTCUSDT",
                Exchange = "Binance Futures",
                Timeframe = "1h",
                RequiredFromUtc = DateTime.UtcNow.AddDays(-30),
                RequiredToUtc = DateTime.UtcNow,
                CandleCount = 0,
                MissingCandleCountEstimate = 720,
                CoverageStatus = "Missing"
            },
            CoverageCheckStartedAtUtc = DateTime.UtcNow,
            RequestedFromUtc = DateTime.UtcNow.AddDays(-30),
            RequestedToUtc = DateTime.UtcNow,
            RequestedTimeframe = "1h",
            ExistingCandleCount = 0,
            AutoImportAttempted = true,
            ImportedCandleCount = 100,
            FinalCoverageStatus = "Complete",
            WarmupCandlesRequested = 100
        };

        Assert.True(result.AutoImportAttempted);
        Assert.Equal(100, result.ImportedCandleCount);
        Assert.Equal("Complete", result.FinalCoverageStatus);
    }
}
