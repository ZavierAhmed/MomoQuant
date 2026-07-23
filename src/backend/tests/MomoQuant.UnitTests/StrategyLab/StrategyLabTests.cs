using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Synthetic;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public class PriceStructureSwingDetectorTests
{
    [Fact]
    public void DetectConfirmedSwings_FindsSwingHigh()
    {
        var candles = BuildSwingHighSeries();
        var swings = PriceStructureSwingDetector.DetectConfirmedSwings(candles, 2, 2, true, candles.Count - 3);
        Assert.Contains(swings, s => s.IsHigh && s.Price == 112m);
    }

    [Fact]
    public void DetectConfirmedSwings_FindsSwingLow()
    {
        var candles = BuildSwingLowSeries();
        var swings = PriceStructureSwingDetector.DetectConfirmedSwings(candles, 2, 2, true, candles.Count - 3);
        Assert.Contains(swings, s => !s.IsHigh && s.Price == 88m);
    }

    private static List<Candle> BuildSwingHighSeries()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 15; i++)
        {
            candles.Add(MakeCandle(i, 100, 101, 99, 100));
        }

        candles[10] = MakeCandle(10, 101, 112, 100, 111);
        return candles;
    }

    private static List<Candle> BuildSwingLowSeries()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 15; i++)
        {
            candles.Add(MakeCandle(i, 100, 101, 99, 100));
        }

        candles[10] = MakeCandle(10, 99, 100, 88, 89);
        return candles;
    }

    private static Candle MakeCandle(int index, decimal open, decimal high, decimal low, decimal close) => new()
    {
        Id = index + 1,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M15,
        OpenTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(15 * index),
        CloseTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(15 * (index + 1)),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 1000,
        QuoteVolume = 1000,
        TradeCount = 100,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class SyntheticScenarioTests
{
    private readonly SyntheticCandleScenarioRunner _runner = new();

    [Fact]
    public void BullishBreakoutScenario_Passes()
    {
        var result = _runner.Run(new BullishBreakoutRetestScenario());
        Assert.True(result.Passed);
        Assert.Equal(1, result.ActualCandidateCount);
    }

    [Fact]
    public void BearishBreakoutScenario_Passes()
    {
        var result = _runner.Run(new BearishBreakoutRetestScenario());
        Assert.True(result.Passed);
    }

    [Fact]
    public void BullishLiquiditySweepScenario_Passes()
    {
        var result = _runner.Run(new BullishLiquiditySweepScenario());
        Assert.True(result.Passed);
    }

    [Fact]
    public void BearishLiquiditySweepScenario_Passes()
    {
        var result = _runner.Run(new BearishLiquiditySweepScenario());
        Assert.True(result.Passed);
    }
}

public class StrategyLabFoundationTests
{
    [Fact]
    public void ExperimentFingerprint_IsDeterministic()
    {
        var parameters = new Dictionary<string, string> { ["swingLeftBars"] = "2" };
        var fp1 = ExperimentFingerprintBuilder.Build(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", "1.0.0", 1, 1, "BTCUSDT", "15m",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            StrategyLabExecutionMode.RawStrategy, parameters, "{}", 10000m, "{}", "{}");
        var fp2 = ExperimentFingerprintBuilder.Build(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", "1.0.0", 1, 1, "BTCUSDT", "15m",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            StrategyLabExecutionMode.RawStrategy, parameters, "{}", 10000m, "{}", "{}");
        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ExperimentFingerprint_ChangesWhenParameterChanges()
    {
        var fp1 = ExperimentFingerprintBuilder.Build(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", "1.0.0", 1, 1, "BTCUSDT", "15m",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            StrategyLabExecutionMode.RawStrategy,
            new Dictionary<string, string> { ["swingLeftBars"] = "2" }, "{}", 10000m, "{}", "{}");
        var fp2 = ExperimentFingerprintBuilder.Build(
            "PRICE_STRUCTURE_BREAKOUT_RETEST", "1.0.0", 1, 1, "BTCUSDT", "15m",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            StrategyLabExecutionMode.RawStrategy,
            new Dictionary<string, string> { ["swingLeftBars"] = "3" }, "{}", 10000m, "{}", "{}");
        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void EvidenceQuality_VeryLowBelow10Trades()
    {
        Assert.Equal(StrategyEvidenceQuality.VeryLow, EvidenceQualityCalculator.Calculate(5));
        Assert.Equal(StrategyEvidenceQuality.Low, EvidenceQualityCalculator.Calculate(15));
        Assert.Equal(StrategyEvidenceQuality.Medium, EvidenceQualityCalculator.Calculate(50));
        Assert.Equal(StrategyEvidenceQuality.High, EvidenceQualityCalculator.Calculate(120));
    }

    [Fact]
    public void OpportunityMetrics_CalculatesPer1000Candles()
    {
        var metrics = StrategyOpportunityMetricsCalculator.Calculate(
            1000,
            new List<StrategyResearchCandidate> { new(), new(), new(), new() },
            1000,
            DateTime.UtcNow.AddDays(-30),
            DateTime.UtcNow);
        Assert.Equal(4m, metrics.CandidatesPer1000Candles);
    }

    [Fact]
    public void RawOutcomeSimulator_StopFirstOnAmbiguousCandle()
    {
        var candidate = new StrategyResearchCandidate
        {
            Direction = TradeDirection.Long,
            ProposedEntryPrice = 100m,
            StopLoss = 95m,
            Target1 = 110m,
            CandidateStatus = StrategyResearchCandidateStatus.StrategyQualified
        };

        var candles = new List<Candle>
        {
            Make(0, 100, 101, 99, 100),
            Make(1, 100, 112, 94, 105)
        };

        RawOutcomeSimulator.Simulate(new RawOutcomeSimulationRequest
        {
            Candidate = candidate,
            Candles = candles,
            EntryCandleIndex = 0,
            Quantity = 1m
        });

        Assert.Equal(RawOutcomeStatus.Loser, candidate.RawOutcomeStatus);
        Assert.Equal(95m, candidate.RawExitPrice);
    }

    private static Candle Make(int index, decimal open, decimal high, decimal low, decimal close) => new()
    {
        Id = index + 1,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M15,
        OpenTimeUtc = DateTime.UtcNow.AddMinutes(15 * index),
        CloseTimeUtc = DateTime.UtcNow.AddMinutes(15 * (index + 1)),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 1000,
        QuoteVolume = 1000,
        TradeCount = 100,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class PriceStructureBreakoutRetestEvaluatorTests
{
    [Fact]
    public void WickAboveSwingWithoutClose_DoesNotBreakout()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 25; i++)
        {
            candles.Add(Make(i, 100, 101, 99, 100));
        }

        candles[10] = Make(10, 101, 112, 100, 111);
        candles[16] = Make(16, 108, 113, 107, 109);

        var (_, reason) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles, new Dictionary<string, string>(), new HashSet<string>(),
            "PRICE_STRUCTURE_BREAKOUT_RETEST", 1, "15m");

        Assert.NotEqual("Bullish breakout retest confirmed.", reason);
    }

    [Fact]
    public void DuplicateSetup_DoesNotCreateSecondCandidate()
    {
        var scenario = new BullishBreakoutRetestScenario();
        var candles = scenario.BuildCandles().ToList();
        var seen = new HashSet<string>();
        var parameters = new Dictionary<string, string>();

        PriceStructureCandidateDto? first = null;
        for (var i = 10; i < candles.Count; i++)
        {
            var slice = candles.Take(i + 1).ToList();
            var (candidate, _) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
                slice, parameters, seen, "PRICE_STRUCTURE_BREAKOUT_RETEST", 1, "15m");
            if (candidate is not null)
            {
                seen.Add(candidate.SetupFingerprint);
                first ??= candidate;
            }
        }

        Assert.NotNull(first);
        var confirmationSlice = candles.Take(20).ToList();
        var (duplicate, reason) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            confirmationSlice, parameters, seen, "PRICE_STRUCTURE_BREAKOUT_RETEST", 1, "15m");
        Assert.Null(duplicate);
        Assert.Equal(PriceStructureRejectionCodes.DuplicateSetup, reason);
    }

    private static Candle Make(int index, decimal open, decimal high, decimal low, decimal close) => new()
    {
        Id = index + 1,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M15,
        OpenTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(15 * index),
        CloseTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(15 * (index + 1)),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 1000,
        QuoteVolume = 1000,
        TradeCount = 100,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };
}
