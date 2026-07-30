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
        // Breakout + retest examined with retestToleranceMode=Atr while ATR14 is still null at retest.
        var candles = BuildAtrUnavailableAtRetestLongScenario();
        var retestIndex = candles.Count - 2;
        Assert.Null(PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, retestIndex));

        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));
        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.InsufficientData, reason);
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
    public void EvaluateAtCurrentCandle_SameCandleConfirmationReactionCloseLong_RejectsThenConfirmsLater()
    {
        var throughRetest = BuildLongWithQualifyingRetest("ReactionClose", includeConfirmation: false);
        AssertValidOhlc(throughRetest[^1]);
        AssertQualifyingRetestPredicate(throughRetest, "ReactionClose", longSide: true);

        var (rejected, rejectReason) = Evaluate(throughRetest, Parameters(("confirmationMode", "ReactionClose")));
        Assert.Null(rejected);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, rejectReason);

        var confirmed = BuildLongWithQualifyingRetest("ReactionClose", includeConfirmation: true);
        AssertValidOhlc(confirmed[^1]);
        var (candidate, reason) = Evaluate(confirmed, Parameters(("confirmationMode", "ReactionClose")));
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(19, candidate.Structure.RetestIndex);
        Assert.Equal(20, candidate.Structure.ConfirmationIndex);
        Assert.True(candidate.Structure.ConfirmationIndex > candidate.Structure.RetestIndex);
        Assert.Equal(confirmed[^1].Close, candidate.EntryPrice);
        Assert.Equal("Bullish breakout retest confirmed.", candidate.Reason);
        Assert.Equal("A0C702ACD7D2AE10", candidate.SetupFingerprint);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationReactionCloseShort_RejectsThenConfirmsLater()
    {
        var throughRetest = BuildShortWithQualifyingRetest("ReactionClose", includeConfirmation: false);
        AssertValidOhlc(throughRetest[^1]);
        AssertQualifyingRetestPredicate(throughRetest, "ReactionClose", longSide: false);

        var (rejected, rejectReason) = Evaluate(throughRetest, Parameters(("confirmationMode", "ReactionClose")));
        Assert.Null(rejected);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, rejectReason);

        var confirmed = BuildShortWithQualifyingRetest("ReactionClose", includeConfirmation: true);
        AssertValidOhlc(confirmed[^1]);
        var (candidate, reason) = Evaluate(confirmed, Parameters(("confirmationMode", "ReactionClose")));
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.Equal(19, candidate.Structure.RetestIndex);
        Assert.Equal(20, candidate.Structure.ConfirmationIndex);
        Assert.True(candidate.Structure.ConfirmationIndex > candidate.Structure.RetestIndex);
        Assert.Equal(confirmed[^1].Close, candidate.EntryPrice);
        Assert.Equal("Bearish breakout retest confirmed.", candidate.Reason);
        Assert.Equal("98EE14660A2E729A", candidate.SetupFingerprint);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationEngulfingLong_RejectsThenConfirmsLater()
    {
        var throughRetest = BuildLongWithQualifyingRetest("Engulfing", includeConfirmation: false);
        AssertValidOhlc(throughRetest[^1]);
        AssertQualifyingRetestPredicate(throughRetest, "Engulfing", longSide: true);

        var (rejected, rejectReason) = Evaluate(throughRetest, Parameters(("confirmationMode", "Engulfing")));
        Assert.Null(rejected);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, rejectReason);

        var confirmed = BuildLongWithQualifyingRetest("Engulfing", includeConfirmation: true);
        AssertValidOhlc(confirmed[^1]);
        var (candidate, reason) = Evaluate(confirmed, Parameters(("confirmationMode", "Engulfing")));
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(19, candidate.Structure.RetestIndex);
        Assert.Equal(20, candidate.Structure.ConfirmationIndex);
        Assert.True(candidate.Structure.ConfirmationIndex > candidate.Structure.RetestIndex);
        Assert.Equal(confirmed[^1].Close, candidate.EntryPrice);
        Assert.Equal("Bullish breakout retest confirmed.", candidate.Reason);
        Assert.Equal("A0C702ACD7D2AE10", candidate.SetupFingerprint);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationEngulfingShort_RejectsThenConfirmsLater()
    {
        var throughRetest = BuildShortWithQualifyingRetest("Engulfing", includeConfirmation: false);
        AssertValidOhlc(throughRetest[^1]);
        AssertQualifyingRetestPredicate(throughRetest, "Engulfing", longSide: false);

        var (rejected, rejectReason) = Evaluate(throughRetest, Parameters(("confirmationMode", "Engulfing")));
        Assert.Null(rejected);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, rejectReason);

        var confirmed = BuildShortWithQualifyingRetest("Engulfing", includeConfirmation: true);
        AssertValidOhlc(confirmed[^1]);
        var (candidate, reason) = Evaluate(confirmed, Parameters(("confirmationMode", "Engulfing")));
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.Equal(19, candidate.Structure.RetestIndex);
        Assert.Equal(20, candidate.Structure.ConfirmationIndex);
        Assert.True(candidate.Structure.ConfirmationIndex > candidate.Structure.RetestIndex);
        Assert.Equal(confirmed[^1].Close, candidate.EntryPrice);
        Assert.Equal("Bearish breakout retest confirmed.", candidate.Reason);
        Assert.Equal("98EE14660A2E729A", candidate.SetupFingerprint);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationCloseBeyondPreviousExtremeLong_RejectsThenConfirmsLater()
    {
        var throughRetest = BuildLongWithQualifyingRetest("CloseBeyondPreviousExtreme", includeConfirmation: false);
        AssertValidOhlc(throughRetest[^1]);
        AssertQualifyingRetestPredicate(throughRetest, "CloseBeyondPreviousExtreme", longSide: true);

        var (rejected, rejectReason) = Evaluate(throughRetest, Parameters(("confirmationMode", "CloseBeyondPreviousExtreme")));
        Assert.Null(rejected);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, rejectReason);

        var confirmed = BuildLongWithQualifyingRetest("CloseBeyondPreviousExtreme", includeConfirmation: true);
        AssertValidOhlc(confirmed[^1]);
        var (candidate, reason) = Evaluate(confirmed, Parameters(("confirmationMode", "CloseBeyondPreviousExtreme")));
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(19, candidate.Structure.RetestIndex);
        Assert.Equal(20, candidate.Structure.ConfirmationIndex);
        Assert.True(candidate.Structure.ConfirmationIndex > candidate.Structure.RetestIndex);
        Assert.Equal(confirmed[^1].Close, candidate.EntryPrice);
        Assert.Equal("Bullish breakout retest confirmed.", candidate.Reason);
        Assert.Equal("A0C702ACD7D2AE10", candidate.SetupFingerprint);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_SameCandleConfirmationCloseBeyondPreviousExtremeShort_RejectsThenConfirmsLater()
    {
        var throughRetest = BuildShortWithQualifyingRetest("CloseBeyondPreviousExtreme", includeConfirmation: false);
        AssertValidOhlc(throughRetest[^1]);
        AssertQualifyingRetestPredicate(throughRetest, "CloseBeyondPreviousExtreme", longSide: false);

        var (rejected, rejectReason) = Evaluate(throughRetest, Parameters(("confirmationMode", "CloseBeyondPreviousExtreme")));
        Assert.Null(rejected);
        Assert.Equal(PriceStructureRejectionCodes.NoConfirmation, rejectReason);

        var confirmed = BuildShortWithQualifyingRetest("CloseBeyondPreviousExtreme", includeConfirmation: true);
        AssertValidOhlc(confirmed[^1]);
        var (candidate, reason) = Evaluate(confirmed, Parameters(("confirmationMode", "CloseBeyondPreviousExtreme")));
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.Equal(19, candidate.Structure.RetestIndex);
        Assert.Equal(20, candidate.Structure.ConfirmationIndex);
        Assert.True(candidate.Structure.ConfirmationIndex > candidate.Structure.RetestIndex);
        Assert.Equal(confirmed[^1].Close, candidate.EntryPrice);
        Assert.Equal("Bearish breakout retest confirmed.", candidate.Reason);
        Assert.Equal("98EE14660A2E729A", candidate.SetupFingerprint);
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
        var (candles, retestPrice, atr, tolerance, boundary, delta) =
            BuildLongScenarioAtAtrRetestBoundaryConverged(epsilon: 0m);
        Assert.True(Math.Abs(delta - 0m) < 0.0000001m);
        Assert.True(Math.Abs(retestPrice - boundary) < 0.0000001m);
        Assert.Equal(atr * 1.00m, tolerance);

        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));
        var (candidate, _) = Evaluate(candles, parameters);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(retestPrice, candles[^2].Low);
        Assert.Equal(atr, PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, candles.Count - 2));
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceExactBoundaryPlusEpsilonLong_WaitsForRetest()
    {
        var (candles, retestPrice, atr, tolerance, boundary, delta) =
            BuildLongScenarioAtAtrRetestBoundaryConverged(epsilon: 0.01m);
        Assert.True(Math.Abs(delta - 0.01m) < 0.0000001m);
        Assert.True(Math.Abs(retestPrice - (boundary + 0.01m)) < 0.0000001m);
        Assert.True(retestPrice > boundary);

        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));
        var (candidate, reason) = Evaluate(candles, parameters);

        Assert.Null(candidate);
        Assert.Equal(PriceStructureRejectionCodes.WaitingForRetest, reason);
        Assert.Equal(atr * 1.00m, tolerance);
        Assert.Equal(atr, PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, candles.Count - 2));
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AtrToleranceExactBoundaryMinusEpsilonLong_ReturnsCandidate()
    {
        var (candles, retestPrice, atr, tolerance, boundary, delta) =
            BuildLongScenarioAtAtrRetestBoundaryConverged(epsilon: -0.01m);
        Assert.True(Math.Abs(delta - (-0.01m)) < 0.0000001m);
        Assert.True(Math.Abs(retestPrice - (boundary - 0.01m)) < 0.0000001m);
        Assert.True(retestPrice < boundary);

        var parameters = Parameters(("retestToleranceMode", "Atr"), ("retestToleranceAtrMultiplier", "1.00"));
        var (candidate, _) = Evaluate(candles, parameters);

        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(atr * 1.00m, tolerance);
        Assert.Equal(atr, PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, candles.Count - 2));
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
    /// iterating until an explicit decimal tolerance is satisfied; fails if it does not converge.
    /// </summary>
    private static (
        List<Candle> Candles,
        decimal ActualRetestPrice,
        decimal ActualAtr,
        decimal ActualTolerance,
        decimal ActualBoundary,
        decimal DifferenceFromExpectedBoundary) BuildLongScenarioAtAtrRetestBoundaryConverged(decimal epsilon)
    {
        const decimal level = 100.00m;
        const decimal atrMultiplier = 1.00m;
        const decimal convergenceTol = 0.00000001m;
        var retestLow = level;
        List<Candle> candles = BuildLongScenario(retestLow: retestLow);
        for (var i = 0; i < 32; i++)
        {
            var retestIndex = candles.Count - 2;
            var atr = PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, retestIndex);
            Assert.NotNull(atr);
            Assert.True(atr!.Value > 0m);
            var tolerance = atr.Value * atrMultiplier;
            var boundary = level + tolerance;
            var nextLow = boundary + epsilon;
            candles = BuildLongScenario(retestLow: nextLow, confirmationLow: 100.30m);
            var atrAfter = PriceStructureBreakoutRetestEvaluator.ComputeAtr14AtIndex(candles, candles.Count - 2);
            Assert.NotNull(atrAfter);
            var toleranceAfter = atrAfter!.Value * atrMultiplier;
            var boundaryAfter = level + toleranceAfter;
            if (Math.Abs(nextLow - (boundaryAfter + epsilon)) <= convergenceTol
                && Math.Abs(nextLow - retestLow) <= convergenceTol)
            {
                return (
                    candles,
                    ActualRetestPrice: nextLow,
                    ActualAtr: atrAfter.Value,
                    ActualTolerance: toleranceAfter,
                    ActualBoundary: boundaryAfter,
                    DifferenceFromExpectedBoundary: nextLow - boundaryAfter);
            }

            retestLow = nextLow;
        }

        Assert.Fail("ATR retest boundary builder did not converge within 32 iterations.");
        return (candles, 0m, 0m, 0m, 0m, 0m);
    }

    private static List<Candle> BuildAtrUnavailableAtRetestLongScenario()
    {
        // prefixCount=11 → breakout@11, retest@12 (ATR14 null), confirmation@13. Percent mode still confirms.
        return BuildLongScenario(prefixCount: 11, confirmationLow: 100.30m);
    }

    private static List<Candle> BuildAtrUnavailableLongScenario()
    {
        return BuildAtrUnavailableAtRetestLongScenario();
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

    /// <summary>
    /// Retest candle itself satisfies the selected confirmation predicate vs the breakout candle,
    /// but confirmation still cannot fire at retestIndex. All OHLC candles are geometrically valid.
    /// </summary>
    private static List<Candle> BuildLongWithQualifyingRetest(string confirmationMode, bool includeConfirmation)
    {
        var candles = BuildBaseStructure(18, 100.00m, bullishSwing: true);
        var breakoutTime = candles[^1].OpenTimeUtc.AddMinutes(5);
        // Breakout: open 99.60 close 100.40 high 100.60
        candles.Add(CreateCandle(breakoutTime, 99.60m, 100.60m, 99.60m, 100.40m));
        var retestTime = breakoutTime.AddMinutes(5);

        // Qualifying retest for the mode (as if evaluated as confirmation with prev=breakout), plus retest touch.
        Candle retest = confirmationMode switch
        {
            // Engulfs breakout (O<=prev.C, C>=prev.O), bullish, close > level, Low touches level
            "Engulfing" => CreateCandle(retestTime, 100.00m, 100.70m, 99.95m, 100.55m),
            // Close > prev high 100.60, Low touches level
            "CloseBeyondPreviousExtreme" => CreateCandle(retestTime, 100.10m, 100.80m, 99.95m, 100.70m),
            // ReactionClose: bullish close > level, Low touches level
            _ => CreateCandle(retestTime, 100.00m, 100.40m, 99.95m, 100.25m)
        };
        AssertValidOhlc(retest);
        candles.Add(retest);

        if (!includeConfirmation)
        {
            return candles;
        }

        var confirmTime = retestTime.AddMinutes(5);
        Candle confirm = confirmationMode switch
        {
            "Engulfing" => CreateCandle(confirmTime, 100.05m, 101.20m, 100.00m, 101.10m),
            "CloseBeyondPreviousExtreme" => CreateCandle(confirmTime, 100.20m, 101.00m, 100.00m, 100.90m),
            _ => CreateCandle(confirmTime, 100.10m, 100.90m, 100.00m, 100.80m)
        };
        AssertValidOhlc(confirm);
        candles.Add(confirm);
        return candles;
    }

    private static List<Candle> BuildShortWithQualifyingRetest(string confirmationMode, bool includeConfirmation)
    {
        var candles = BuildBaseStructure(18, 100.00m, bullishSwing: false);
        var breakoutTime = candles[^1].OpenTimeUtc.AddMinutes(5);
        candles.Add(CreateCandle(breakoutTime, 100.40m, 100.40m, 99.40m, 99.60m));
        var retestTime = breakoutTime.AddMinutes(5);

        Candle retest = confirmationMode switch
        {
            // Engulfs breakout (O>=prev.C, C<=prev.O), bearish, close < level, High touches level
            "Engulfing" => CreateCandle(retestTime, 100.00m, 100.05m, 99.30m, 99.45m),
            // Close < prev low 99.40, High touches level
            "CloseBeyondPreviousExtreme" => CreateCandle(retestTime, 99.90m, 100.05m, 99.20m, 99.30m),
            // ReactionClose: bearish close < level, High touches level
            _ => CreateCandle(retestTime, 100.00m, 100.05m, 99.70m, 99.80m)
        };
        AssertValidOhlc(retest);
        candles.Add(retest);

        if (!includeConfirmation)
        {
            return candles;
        }

        var confirmTime = retestTime.AddMinutes(5);
        Candle confirm = confirmationMode switch
        {
            "Engulfing" => CreateCandle(confirmTime, 99.90m, 100.00m, 98.80m, 98.90m),
            "CloseBeyondPreviousExtreme" => CreateCandle(confirmTime, 99.70m, 99.80m, 98.90m, 99.00m),
            _ => CreateCandle(confirmTime, 99.90m, 100.00m, 99.10m, 99.20m)
        };
        AssertValidOhlc(confirm);
        candles.Add(confirm);
        return candles;
    }

    private static void AssertValidOhlc(Candle candle)
    {
        Assert.True(candle.Low <= candle.High, $"Low {candle.Low} > High {candle.High}");
        Assert.True(candle.Low <= candle.Open && candle.Open <= candle.High, $"Open {candle.Open} outside [{candle.Low},{candle.High}]");
        Assert.True(candle.Low <= candle.Close && candle.Close <= candle.High, $"Close {candle.Close} outside [{candle.Low},{candle.High}]");
    }

    private static void AssertQualifyingRetestPredicate(IReadOnlyList<Candle> candles, string mode, bool longSide)
    {
        const decimal level = 100.00m;
        var retest = candles[^1];
        var prev = candles[^2];
        AssertValidOhlc(retest);
        if (longSide)
        {
            Assert.True(retest.Close > level);
            if (string.Equals(mode, "ReactionClose", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(retest.Close > retest.Open);
            }
            else if (string.Equals(mode, "Engulfing", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(retest.Close > retest.Open);
                Assert.True(retest.Close >= prev.Open);
                Assert.True(retest.Open <= prev.Close);
            }
            else
            {
                Assert.True(retest.Close > prev.High);
            }
        }
        else
        {
            Assert.True(retest.Close < level);
            if (string.Equals(mode, "ReactionClose", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(retest.Close < retest.Open);
            }
            else if (string.Equals(mode, "Engulfing", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(retest.Close < retest.Open);
                Assert.True(retest.Close <= prev.Open);
                Assert.True(retest.Open >= prev.Close);
            }
            else
            {
                Assert.True(retest.Close < prev.Low);
            }
        }
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
            new Mock<IStrategyLabQueue>().Object,
            new Mock<IStrategyDataRequirementService>().Object);

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
            new Mock<IStrategyLabQueue>().Object,
            new Mock<IStrategyDataRequirementService>().Object);

        var result = await service.GetRerunConfigAsync(2);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Current v1.1 Run", result.Data.Name);
    }
}
