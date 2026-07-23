namespace MomoQuant.Application.StrategyLab.Risk;

/// <summary>
/// Explicit futures risk quantity formulas for Strategy Lab observation.
/// Distinguishes risk-at-stop, notional exposure, margin usage, and leverage variants.
/// </summary>
public static class FuturesSizingCalculator
{
    public sealed class Result
    {
        public decimal EntryPrice { get; init; }
        public decimal StopLoss { get; init; }
        public decimal StopDistanceAbsolute { get; init; }
        public decimal? StopDistancePercent { get; init; }
        public decimal RiskPerTradePercent { get; init; }
        public decimal RiskAmount { get; init; }
        public decimal RiskAtStopPercent { get; init; }
        public decimal? Quantity { get; init; }
        public decimal? PositionNotional { get; init; }
        public decimal? NotionalExposurePercent { get; init; }
        public decimal? MinimumRequiredLeverage { get; init; }
        public decimal? AssessmentLeverage { get; init; }
        public decimal? MaxLeverage { get; init; }
        public decimal? PreferredLeverage { get; init; }
        public decimal? InitialMarginRequired { get; init; }
        public decimal? MarginUsagePercent { get; init; }
        public decimal EstimatedEntryFee { get; init; }
        public decimal EstimatedExitFee { get; init; }
        public decimal EstimatedRoundTripFees { get; init; }
        public decimal TargetGrossProfit { get; init; }
        public decimal TargetNetProfitEstimate { get; init; }
        public decimal? FeeToTargetPercent { get; init; }
        public string? UnavailableReason { get; init; }
    }

    public static Result Calculate(
        decimal entryPrice,
        decimal stopLoss,
        decimal targetPrice,
        decimal assessmentEquity,
        decimal riskPerTradePercent,
        decimal maxLeverage,
        decimal? preferredLeverage,
        decimal takerFeeRate)
    {
        var stopAbs = Math.Abs(entryPrice - stopLoss);
        var stopPct = entryPrice > 0 && stopAbs > 0
            ? Math.Round(stopAbs / entryPrice * 100m, 6)
            : (decimal?)null;

        if (entryPrice <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Entry price is invalid.");
        }

        if (stopAbs <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Stop distance is zero.");
        }

        if (assessmentEquity <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Assessment equity must be greater than zero.");
        }

        if (riskPerTradePercent <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Risk per trade percent must be greater than zero.");
        }

        if (maxLeverage <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Max leverage must be greater than zero.");
        }

        var riskAmount = Math.Round(assessmentEquity * riskPerTradePercent / 100m, 8);
        var riskAtStopPct = Math.Round(riskPerTradePercent, 6);
        var quantity = decimal.Round(riskAmount / stopAbs, 8, MidpointRounding.ToZero);
        if (quantity <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Calculated position size is zero.");
        }

        var notional = Math.Round(quantity * entryPrice, 8);
        var notionalExposurePct = Math.Round(notional / assessmentEquity * 100m, 6);
        var minRequiredLeverage = Math.Round(notional / assessmentEquity, 6);

        decimal assessmentLeverage;
        if (preferredLeverage is > 0)
        {
            assessmentLeverage = Math.Min(preferredLeverage.Value, maxLeverage);
            if (assessmentLeverage < minRequiredLeverage)
            {
                // Preferred leverage cannot support the notional with available equity as margin.
                assessmentLeverage = Math.Min(maxLeverage, CeilingLeverage(minRequiredLeverage));
            }
        }
        else
        {
            assessmentLeverage = Math.Min(maxLeverage, Math.Max(1m, CeilingLeverage(minRequiredLeverage)));
        }

        if (assessmentLeverage <= 0)
        {
            return Unavailable(entryPrice, stopLoss, stopAbs, stopPct, riskPerTradePercent, "Assessment leverage is invalid.");
        }

        var initialMargin = Math.Round(notional / assessmentLeverage, 8);
        var marginUsagePct = Math.Round(initialMargin / assessmentEquity * 100m, 6);

        var entryFee = Math.Round(notional * takerFeeRate, 8);
        var exitFee = entryFee;
        var roundTrip = entryFee + exitFee;
        var targetGross = Math.Round(Math.Abs(targetPrice - entryPrice) * quantity, 8);
        var targetNet = targetGross - roundTrip;
        var feeToTarget = targetGross > 0 ? Math.Round(roundTrip / targetGross * 100m, 6) : (decimal?)null;

        return new Result
        {
            EntryPrice = entryPrice,
            StopLoss = stopLoss,
            StopDistanceAbsolute = stopAbs,
            StopDistancePercent = stopPct,
            RiskPerTradePercent = riskPerTradePercent,
            RiskAmount = riskAmount,
            RiskAtStopPercent = riskAtStopPct,
            Quantity = quantity,
            PositionNotional = notional,
            NotionalExposurePercent = notionalExposurePct,
            MinimumRequiredLeverage = minRequiredLeverage,
            AssessmentLeverage = Math.Round(assessmentLeverage, 4),
            MaxLeverage = maxLeverage,
            PreferredLeverage = preferredLeverage,
            InitialMarginRequired = initialMargin,
            MarginUsagePercent = marginUsagePct,
            EstimatedEntryFee = entryFee,
            EstimatedExitFee = exitFee,
            EstimatedRoundTripFees = roundTrip,
            TargetGrossProfit = targetGross,
            TargetNetProfitEstimate = targetNet,
            FeeToTargetPercent = feeToTarget
        };
    }

    /// <summary>Ceiling to 0.1x increments for deterministic leverage steps.</summary>
    public static decimal CeilingLeverage(decimal required)
    {
        if (required <= 1m) return 1m;
        const decimal step = 0.1m;
        return Math.Ceiling(required / step) * step;
    }

    private static Result Unavailable(
        decimal entry,
        decimal stop,
        decimal stopAbs,
        decimal? stopPct,
        decimal riskPct,
        string reason) =>
        new()
        {
            EntryPrice = entry,
            StopLoss = stop,
            StopDistanceAbsolute = Math.Max(0m, stopAbs),
            StopDistancePercent = stopPct,
            RiskPerTradePercent = riskPct,
            UnavailableReason = reason
        };
}
