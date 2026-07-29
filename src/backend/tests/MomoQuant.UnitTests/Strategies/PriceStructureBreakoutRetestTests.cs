using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Synthetic;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using Moq;

namespace MomoQuant.UnitTests.Strategies;

public sealed class PriceStructureBreakoutRetestTests
{
    private static readonly DateTime StartUtc = new(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EvaluateAtCurrentCandle_ValidLong_UsesExpectedGeometry()
    {
        var candles = BuildLongScenario();

        var (candidate, reason) = Evaluate(candles);

        Assert.Equal("Bullish breakout retest confirmed.", reason);
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(100.80m, candidate.EntryPrice);
        Assert.Equal(99.95m, candidate.StopLoss);
        Assert.Equal(102.50m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ValidShort_UsesExpectedGeometry()
    {
        var candles = BuildShortScenario();

        var (candidate, reason) = Evaluate(candles);

        Assert.Equal("Bearish breakout retest confirmed.", reason);
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.Equal(99.20m, candidate.EntryPrice);
        Assert.Equal(100.05m, candidate.StopLoss);
        Assert.Equal(97.50m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_PercentToleranceJustInside_ReturnsCandidate()
    {
        var candles = BuildLongScenario(retestLow: 99.86m, confirmationLow: 100.30m);

        var (candidate, _) = Evaluate(candles);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(100.80m, candidate.EntryPrice);
        Assert.Equal(99.81007m, candidate.StopLoss);
        Assert.Equal(102.77986m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_PercentToleranceJustOutside_WaitsForRetest()
    {
        var candles = BuildLongScenario(retestLow: 99.84m, confirmationLow: 100.30m);

        var (candidate, reason) = Evaluate(candles);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.WaitingForRetest, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceJustInside_ReturnsCandidate()
    {
        var candles = BuildLongScenario();
        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));

        var (candidate, _) = Evaluate(candles, parameters);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(100.80m, candidate.EntryPrice);
        Assert.Equal(99.95m, candidate.StopLoss);
        Assert.Equal(102.50m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceJustOutside_WaitsForRetest()
    {
        var candles = BuildLongScenario(retestLow: 98.90m, confirmationLow: 100.60m);
        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "0.20"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.WaitingForRetest, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrUnavailable_DoesNotFallBackToPercent()
    {
        var candles = BuildAtrUnavailableLongScenario();
        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "0.10"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoBreakout, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ToleranceAppliedExactlyOnce_DoesNotDoubleCount()
    {
        var candles = BuildLongScenario(retestLow: 99.71m, confirmationLow: 100.30m);

        var (candidate, reason) = Evaluate(candles);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.WaitingForRetest, reason);
    }

    [Theory]
    [InlineData("ReactionClose")]
    [InlineData("BullishReactionClose")]
    public void EvaluateAtCurrentCandle_ReactionCloseLongAliases_ReturnCandidate(string mode)
    {
        var (candidate, _) = Evaluate(BuildLongScenario(), Parameters(("confirmationMode", mode)));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(100.80m, candidate.EntryPrice);
        Assert.Equal(99.95m, candidate.StopLoss);
        Assert.Equal(102.50m, candidate.Target1);
    }

    [Theory]
    [InlineData("ReactionClose")]
    [InlineData("BullishReactionClose")]
    public void EvaluateAtCurrentCandle_ReactionCloseShortAliases_ReturnCandidate(string mode)
    {
        var (candidate, _) = Evaluate(BuildShortScenario(), Parameters(("confirmationMode", mode)));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.Equal(99.20m, candidate.EntryPrice);
        Assert.Equal(100.05m, candidate.StopLoss);
        Assert.Equal(97.50m, candidate.Target1);
    }

    [Theory]
    [InlineData("Engulfing")]
    [InlineData("BullishEngulfing")]
    public void EvaluateAtCurrentCandle_EngulfingLongAliases_ReturnCandidate(string mode)
    {
        var (candidate, _) = Evaluate(BuildLongScenario(confirmationMode: mode), Parameters(("confirmationMode", mode)));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(101.10m, candidate.EntryPrice);
        Assert.Equal(99.95m, candidate.StopLoss);
        Assert.Equal(103.40m, candidate.Target1);
    }

    [Theory]
    [InlineData("Engulfing")]
    [InlineData("BullishEngulfing")]
    public void EvaluateAtCurrentCandle_EngulfingShortAliases_ReturnCandidate(string mode)
    {
        var (candidate, _) = Evaluate(BuildShortScenario(confirmationMode: mode), Parameters(("confirmationMode", mode)));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.Equal(98.90m, candidate.EntryPrice);
        Assert.Equal(100.05m, candidate.StopLoss);
        Assert.Equal(96.60m, candidate.Target1);
    }

    [Theory]
    [InlineData("CloseBeyondPreviousExtreme")]
    [InlineData("CloseAbovePreviousHigh")]
    public void EvaluateAtCurrentCandle_CloseBeyondPreviousExtremeLongAliases_ReturnCandidate(string mode)
    {
        var (candidate, _) = Evaluate(BuildLongScenario(confirmationMode: mode), Parameters(("confirmationMode", mode)));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(101.10m, candidate.EntryPrice);
        Assert.Equal(99.95m, candidate.StopLoss);
        Assert.Equal(103.40m, candidate.Target1);
    }

    [Theory]
    [InlineData("CloseBeyondPreviousExtreme")]
    [InlineData("CloseAbovePreviousHigh")]
    public void EvaluateAtCurrentCandle_CloseBeyondPreviousExtremeShortAliases_ReturnCandidate(string mode)
    {
        var (candidate, _) = Evaluate(BuildShortScenario(confirmationMode: mode), Parameters(("confirmationMode", mode)));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.Equal(98.90m, candidate.EntryPrice);
        Assert.Equal(100.05m, candidate.StopLoss);
        Assert.Equal(96.60m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_NoConfirmationLong_ReturnsSameCandleCandidate()
    {
        var candles = BuildLongScenario(confirmationMode: "NoConfirmation");

        var (candidate, _) = Evaluate(candles, Parameters(("confirmationMode", "NoConfirmation")));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(100.05m, candidate.EntryPrice);
        Assert.Equal(99.95m, candidate.StopLoss);
        Assert.Equal(100.25m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_NoConfirmationShort_ReturnsSameCandleCandidate()
    {
        var candles = BuildShortScenario(confirmationMode: "NoConfirmation");

        var (candidate, _) = Evaluate(candles, Parameters(("confirmationMode", "NoConfirmation")));

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.Equal(99.95m, candidate.EntryPrice);
        Assert.Equal(100.05m, candidate.StopLoss);
        Assert.Equal(99.75m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_UnknownConfirmationMode_ReturnsInvalidParameters()
    {
        var (candidate, reason) = Evaluate(BuildLongScenario(), Parameters(("confirmationMode", "CloseBeyondExtreme")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_UnknownToleranceMode_ReturnsInvalidParameters()
    {
        var (candidate, reason) = Evaluate(BuildLongScenario(), Parameters(("retestToleranceMode", "WeirdMode")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ExpiredSetupCannotConfirmLater()
    {
        var candles = BuildLongScenario(confirmGapBars: 21);
        var parameters = Parameters(("maxRetestBars", "2"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.RetestExpired, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_WorstMultiCandleRetestStop_UsesDeepestAllowedLow()
    {
        var candles = BuildLongScenario(
            multiCandleRetest: true,
            secondRetestLow: 99.92m,
            confirmationLow: 99.90m);

        var (candidate, _) = Evaluate(candles);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(100.80m, candidate.EntryPrice);
        Assert.Equal(99.85005m, candidate.StopLoss);
        Assert.Equal(102.69990m, candidate.Target1);
    }

    [Fact]
    public void ComputeStrengthBreakdown_DeterministicExactComponents()
    {
        var parameters = Parameters();
        var candles = BuildLongScenario();
        var (candidate, _) = Evaluate(candles, parameters);

        Assert.NotNull(candidate);
        var breakdown = PriceStructureBreakoutRetestEvaluator.ComputeStrengthBreakdown(
            candles,
            candidate,
            PriceStructureBreakoutRetestEvaluator.ReadParameters(parameters));

        Assert.Equal(85.47m, breakdown.Total);
        Assert.Equal(22.75m, breakdown.BreakoutDistanceScore);
        Assert.Equal(23.50m, breakdown.RetestQualityScore);
        Assert.Equal(14.22m, breakdown.ConfirmationQualityScore);
        Assert.Equal(25.00m, breakdown.RewardRiskValidityScore);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidShortTarget_ReturnsInvalidTarget()
    {
        var candles = BuildShortScenario();
        var parameters = Parameters(("fixedRewardRisk", "0"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_DuplicateSetup_ReturnsDuplicateSetup()
    {
        var candles = BuildLongScenario();
        var first = Evaluate(candles);

        Assert.NotNull(first.Candidate);

        var duplicate = Evaluate(candles, seenFingerprints: new HashSet<string> { first.Candidate.SetupFingerprint });

        Assert.Null(duplicate.Candidate);
        Assert.Equal(PriceStructureRejectionCodes.DuplicateSetup, duplicate.Reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationReactionCloseLong_RejectsNoConfirmation()
    {
        var candles = BuildLongScenario();
        candles.RemoveAt(candles.Count - 1);

        var (candidate, reason) = Evaluate(candles, Parameters(("confirmationMode", "ReactionClose")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationReactionCloseShort_RejectsNoConfirmation()
    {
        var candles = BuildShortScenario();
        candles.RemoveAt(candles.Count - 1);

        var (candidate, reason) = Evaluate(candles, Parameters(("confirmationMode", "ReactionClose")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationEngulfingLong_RejectsNoConfirmation()
    {
        var candles = BuildLongScenario(confirmationMode: "Engulfing");
        candles.RemoveAt(candles.Count - 1);

        var (candidate, reason) = Evaluate(candles, Parameters(("confirmationMode", "Engulfing")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationEngulfingShort_RejectsNoConfirmation()
    {
        var candles = BuildShortScenario(confirmationMode: "Engulfing");
        candles.RemoveAt(candles.Count - 1);

        var (candidate, reason) = Evaluate(candles, Parameters(("confirmationMode", "Engulfing")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationCloseBeyondPreviousExtremeLong_RejectsNoConfirmation()
    {
        var candles = BuildLongScenario(confirmationMode: "CloseBeyondPreviousExtreme");
        candles.RemoveAt(candles.Count - 1);

        var (candidate, reason) = Evaluate(candles, Parameters(("confirmationMode", "CloseBeyondPreviousExtreme")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationCloseBeyondPreviousExtremeShort_RejectsNoConfirmation()
    {
        var candles = BuildShortScenario(confirmationMode: "CloseBeyondPreviousExtreme");
        candles.RemoveAt(candles.Count - 1);

        var (candidate, reason) = Evaluate(candles, Parameters(("confirmationMode", "CloseBeyondPreviousExtreme")));

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ExpiredShortSetup_CannotConfirmLater()
    {
        var candles = BuildShortScenario(confirmGapBars: 21);
        var parameters = Parameters(("maxRetestBars", "2"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.RetestExpired, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ShortWorstMultiCandleRetestStop_UsesHighestAllowedHigh()
    {
        var candles = BuildShortScenario(
            multiCandleRetest: true,
            secondRetestHigh: 100.08m,
            confirmationHigh: 100.10m);

        var (candidate, _) = Evaluate(candles);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.Equal(99.20m, candidate.EntryPrice);
        Assert.Equal(100.150050m, candidate.StopLoss);
        Assert.Equal(97.299900m, candidate.Target1);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_PercentToleranceExactBoundaryLong_ReturnsCandidate()
    {
        var level = 100.00m;
        var retestTolerancePct = 0.15m;
        var tolerance = level * retestTolerancePct / 100m;
        var exactBoundary = level + tolerance;
        var candles = BuildLongScenario(retestLow: exactBoundary, confirmationLow: 100.30m);

        var (candidate, _) = Evaluate(candles);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_PercentToleranceExactBoundaryPlusEpsilonLong_WaitsForRetest()
    {
        var level = 100.00m;
        var retestTolerancePct = 0.15m;
        var tolerance = level * retestTolerancePct / 100m;
        var exactBoundary = level + tolerance;
        var candles = BuildLongScenario(retestLow: exactBoundary + 0.001m, confirmationLow: 100.30m);

        var (candidate, reason) = Evaluate(candles);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.WaitingForRetest, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_PercentToleranceExactBoundaryMinusEpsilonLong_ReturnsCandidate()
    {
        var level = 100.00m;
        var retestTolerancePct = 0.15m;
        var tolerance = level * retestTolerancePct / 100m;
        var exactBoundary = level + tolerance;
        var candles = BuildLongScenario(retestLow: exactBoundary - 0.001m, confirmationLow: 100.30m);

        var (candidate, _) = Evaluate(candles);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceExactBoundaryLong_ReturnsCandidate()
    {
        var candles = BuildLongScenarioAtAtrRetestBoundary(epsilon: 0m);
        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));

        var (candidate, _) = Evaluate(candles, parameters);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceExactBoundaryPlusEpsilonLong_WaitsForRetest()
    {
        var candles = BuildLongScenarioAtAtrRetestBoundary(epsilon: 0.01m);
        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.WaitingForRetest, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceExactBoundaryMinusEpsilonLong_ReturnsCandidate()
    {
        var candles = BuildLongScenarioAtAtrRetestBoundary(epsilon: -0.01m);
        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));

        var (candidate, _) = Evaluate(candles, parameters);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidShortTarget_GeometryProducesInvalidTarget()
    {
        // Valid short path with positive fixedRewardRisk large enough that target drops to <= 0.
        var candles = BuildShortScenario();
        var parameters = Parameters(("fixedRewardRisk", "200.00"));

        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.InvalidTarget, reason);
    }

    /// <summary>
    /// Builds a long scenario whose retest low sits at level + ATR14(retest)×1.00 + epsilon,
    /// refining until the ATR used matches the final series (tolerance applied once).
    /// </summary>
    private static List<Candle> BuildLongScenarioAtAtrRetestBoundary(decimal epsilon)
    {
        const decimal level = 100.00m;
        const decimal atrMultiplier = 1.00m;
        var retestLow = level;
        List<Candle> candles = BuildLongScenario(retestLow: retestLow);
        for (var i = 0; i < 8; i++)
        {
            var retestIndex = candles.Count - 2;
            var atr = PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, retestIndex);
            Assert.NotNull(atr);
            Assert.True(atr.Value > 0m);
            var nextLow = level + (atr.Value * atrMultiplier) + epsilon;
            if (nextLow == retestLow)
            {
                return candles;
            }

            retestLow = nextLow;
            candles = BuildLongScenario(retestLow: retestLow, confirmationLow: 100.30m);
        }

        return candles;
    }

    private static (PriceStructureCandidateDto? Candidate, string Reason) Evaluate(
        IReadOnlyList<Candle> candles,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlySet<string>? seenFingerprints = null)
    {
        return PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters ?? Parameters(),
            seenFingerprints ?? new HashSet<string>(),
            StrategyCodes.PriceStructureBreakoutRetest,
            1,
            "5m");
    }

    private static Dictionary<string, string> Parameters(params (string Key, string Value)[] overrides)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            parameters[key] = value;
        }

        return parameters;
    }

    private static List<Candle> BuildLongScenario(
        decimal breakoutClose = 100.40m,
        decimal retestLow = 100.00m,
        decimal retestClose = 100.05m,
        decimal confirmationOpen = 100.10m,
        decimal confirmationLow = 100.00m,
        decimal confirmationClose = 100.80m,
        int prefixCount = 18,
        int confirmGapBars = 1,
        bool multiCandleRetest = false,
        decimal secondRetestLow = 99.95m,
        string confirmationMode = "ReactionClose")
    {
        var candles = BuildBaseStructure(prefixCount, 100.00m, bullishSwing: true);

        var breakoutTime = candles[^1].OpenTimeUtc.AddMinutes(5);
        candles.Add(CreateCandle(breakoutTime, 99.60m, 100.60m, 99.60m, breakoutClose));
        var retestTime = breakoutTime.AddMinutes(5);
        candles.Add(CreateCandle(retestTime, 100.20m, 100.30m, retestLow, retestClose));

        if (multiCandleRetest)
        {
            candles.Add(CreateCandle(retestTime.AddMinutes(5), 100.12m, 100.28m, secondRetestLow, 100.07m));
        }

        if (string.Equals(confirmationMode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return candles;
        }

        for (var gap = 1; gap < confirmGapBars; gap++)
        {
            var gapTime = retestTime.AddMinutes(gap * 5L);
            candles.Add(CreateCandle(gapTime, 100.20m, 100.40m, 100.18m, 100.24m));
        }

        var confirmTime = retestTime.AddMinutes(confirmGapBars * 5L);
        if (string.Equals(confirmationMode, "Engulfing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmationMode, "BullishEngulfing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmationMode, "CloseBeyondPreviousExtreme", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmationMode, "CloseAbovePreviousHigh", StringComparison.OrdinalIgnoreCase))
        {
            candles.Add(CreateCandle(confirmTime, 99.95m, 101.20m, confirmationLow, 101.10m));
            return candles;
        }

        candles.Add(CreateCandle(confirmTime, confirmationOpen, 100.90m, confirmationLow, confirmationClose));
        return candles;
    }

    private static List<Candle> BuildShortScenario(
        decimal breakoutClose = 99.60m,
        decimal retestHigh = 100.00m,
        decimal retestClose = 99.95m,
        decimal confirmationOpen = 99.90m,
        decimal confirmationHigh = 100.00m,
        decimal confirmationClose = 99.20m,
        int prefixCount = 18,
        int confirmGapBars = 1,
        bool multiCandleRetest = false,
        decimal secondRetestHigh = 100.05m,
        string confirmationMode = "ReactionClose")
    {
        var candles = BuildBaseStructure(prefixCount, 100.00m, bullishSwing: false);

        var breakoutTime = candles[^1].OpenTimeUtc.AddMinutes(5);
        candles.Add(CreateCandle(breakoutTime, 100.40m, 100.40m, 99.40m, breakoutClose));
        var retestTime = breakoutTime.AddMinutes(5);
        candles.Add(CreateCandle(retestTime, 99.80m, retestHigh, 99.70m, retestClose));

        if (multiCandleRetest)
        {
            candles.Add(CreateCandle(retestTime.AddMinutes(5), 99.88m, secondRetestHigh, 99.72m, 99.93m));
        }

        if (string.Equals(confirmationMode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return candles;
        }

        for (var gap = 1; gap < confirmGapBars; gap++)
        {
            var gapTime = retestTime.AddMinutes(gap * 5L);
            candles.Add(CreateCandle(gapTime, 99.80m, 99.82m, 99.60m, 99.76m));
        }

        var confirmTime = retestTime.AddMinutes(confirmGapBars * 5L);
        if (string.Equals(confirmationMode, "Engulfing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmationMode, "BullishEngulfing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmationMode, "CloseBeyondPreviousExtreme", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmationMode, "CloseAbovePreviousHigh", StringComparison.OrdinalIgnoreCase))
        {
            candles.Add(CreateCandle(confirmTime, 100.05m, confirmationHigh, 98.80m, 98.90m));
            return candles;
        }

        candles.Add(CreateCandle(confirmTime, confirmationOpen, confirmationHigh, 99.10m, confirmationClose));
        return candles;
    }

    private static List<Candle> BuildBaseStructure(int prefixCount, decimal level, bool bullishSwing)
    {
        var candles = new List<Candle>();
        for (var i = 0; i < prefixCount; i++)
        {
            var time = StartUtc.AddMinutes(i * 5L);
            if (i == 6)
            {
                candles.Add(bullishSwing
                    ? CreateCandle(time, 99.60m, level, 99.55m, 99.80m)
                    : CreateCandle(time, 100.40m, 100.45m, level, 100.20m));
                continue;
            }

            var open = bullishSwing ? 99.70m + ((i % 4) * 0.03m) : 100.30m - ((i % 4) * 0.03m);
            var high = bullishSwing ? 99.92m : 100.45m;
            var low = bullishSwing ? 99.40m : 100.08m;
            var close = bullishSwing ? 99.75m + ((i % 3) * 0.02m) : 100.25m - ((i % 3) * 0.02m);
            candles.Add(CreateCandle(time, open, high, low, close));
        }

        return candles;
    }

    private static List<Candle> BuildAtrUnavailableLongScenario()
    {
        return
        [
            CreateCandle(StartUtc.AddMinutes(0), 99.70m, 99.90m, 99.50m, 99.75m),
            CreateCandle(StartUtc.AddMinutes(5), 99.72m, 99.88m, 99.55m, 99.76m),
            CreateCandle(StartUtc.AddMinutes(10), 99.78m, 100.00m, 99.60m, 99.82m),
            CreateCandle(StartUtc.AddMinutes(15), 99.70m, 99.86m, 99.52m, 99.74m),
            CreateCandle(StartUtc.AddMinutes(20), 99.71m, 99.87m, 99.54m, 99.75m),
            CreateCandle(StartUtc.AddMinutes(25), 99.73m, 99.89m, 99.58m, 99.77m),
            CreateCandle(StartUtc.AddMinutes(30), 99.74m, 99.90m, 99.60m, 99.78m),
            CreateCandle(StartUtc.AddMinutes(35), 99.78m, 100.45m, 99.78m, 100.40m),
            CreateCandle(StartUtc.AddMinutes(40), 100.05m, 100.08m, 99.98m, 100.03m),
            CreateCandle(StartUtc.AddMinutes(45), 100.35m, 100.55m, 100.30m, 100.50m),
            CreateCandle(StartUtc.AddMinutes(50), 100.10m, 100.26m, 100.08m, 100.16m),
            CreateCandle(StartUtc.AddMinutes(55), 100.12m, 100.28m, 100.10m, 100.18m),
            CreateCandle(StartUtc.AddMinutes(60), 100.14m, 100.32m, 100.12m, 100.20m)
        ];
    }

    private static Candle CreateCandle(DateTime openTimeUtc, decimal open, decimal high, decimal low, decimal close)
    {
        return new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = openTimeUtc,
            CloseTimeUtc = openTimeUtc.AddMinutes(5),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = 100m,
            IsClosed = true
        };
    }
}

public sealed class PriceStructureBreakoutRetestV10HistoricalTests
{
    [Fact]
    public async Task StrategyLabService_GetRerunConfigAsync_V10PriceStructure_ReturnsReadOnlyError()
    {
        var mockRunRepository = new Mock<IStrategyLabRunRepository>();
        var v10Run = new StrategyLabRun
        {
            Id = 1,
            Name = "Historical v1.0 Run",
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            StrategyVersion = PriceStructureBreakoutRetestEvaluator.StrategyVersionV10,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "5m",
            FromUtc = DateTime.UtcNow.AddDays(-30),
            ToUtc = DateTime.UtcNow,
            ExecutionMode = StrategyLabExecutionMode.RawStrategy,
            ParametersJson = "{}",
            FeeSettingsJson = "{}",
            SlippageSettingsJson = "{}",
            InitialBalance = 10000m
        };

        mockRunRepository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(v10Run);

        var service = new StrategyLabService(
            mockRunRepository.Object,
            new Mock<IStrategyResearchCandidateRepository>().Object,
            new Mock<IStrategyRepository>().Object,
            new Mock<IStrategyRegistry>().Object,
            new Mock<ISymbolRepository>().Object,
            new Mock<IStrategyLabQueue>().Object);

        var result = await service.GetRerunConfigAsync(1);

        Assert.False(result.Succeeded);
        Assert.Contains("v1.0.0", result.ErrorMessage);
        Assert.Contains("read-only", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot reproduce", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StrategyLabService_GetRerunConfigAsync_V11PriceStructure_SucceedsWithConfig()
    {
        var mockRunRepository = new Mock<IStrategyLabRunRepository>();
        var v11Run = new StrategyLabRun
        {
            Id = 2,
            Name = "Current v1.1 Run",
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            StrategyVersion = PriceStructureBreakoutRetestEvaluator.StrategyVersion,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "5m",
            FromUtc = DateTime.UtcNow.AddDays(-30),
            ToUtc = DateTime.UtcNow,
            ExecutionMode = StrategyLabExecutionMode.RawStrategy,
            ParametersJson = "{}",
            FeeSettingsJson = "{}",
            SlippageSettingsJson = "{}",
            InitialBalance = 10000m
        };

        mockRunRepository
            .Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(v11Run);

        var service = new StrategyLabService(
            mockRunRepository.Object,
            new Mock<IStrategyResearchCandidateRepository>().Object,
            new Mock<IStrategyRepository>().Object,
            new Mock<IStrategyRegistry>().Object,
            new Mock<ISymbolRepository>().Object,
            new Mock<IStrategyLabQueue>().Object);

        var result = await service.GetRerunConfigAsync(2);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Current v1.1 Run", result.Data.Name);
    }
}
