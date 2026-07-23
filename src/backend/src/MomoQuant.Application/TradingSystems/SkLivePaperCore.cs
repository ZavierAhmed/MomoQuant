using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Application.TradingSystems;

public static class SkLivePaperConstants
{
    public const string SimulationMode = "SK_LIVE_PAPER";
    public const decimal DefaultTakerFeePercent = 0.04m;
    public const decimal DefaultSlippagePercent = 0.01m;
    public const int DefaultLookbackCandles = 500;
}

public static class SkLivePaperRejectionReasons
{
    public const string DirectionMismatch = "DirectionMismatch";
    public const string LowClarity = "LowClarity";
    public const string LowUsefulness = "LowUsefulness";
    public const string HtfDisagreement = "HtfDisagreement";
    public const string DirectionNotAllowed = "DirectionNotAllowed";
    public const string MaxOpenPositionsReached = "MaxOpenPositionsReached";
    public const string MaxTradesPerDayReached = "MaxTradesPerDayReached";
    public const string MissingInvalidation = "MissingInvalidation";
    public const string MissingTarget = "MissingTarget";
    public const string InvalidRiskReward = "InvalidRiskReward";
    public const string WaitingForReactionConfirmation = "WaitingForReactionConfirmation";
    public const string AlreadyReached = "AlreadyReached";
    public const string StructureInvalidated = "StructureInvalidated";
}

public sealed class SkLivePaperEvaluationResult
{
    public bool CanOpenTrade { get; init; }
    public string? RejectionReason { get; init; }
    public bool WaitingForConfirmation { get; init; }
    public SkSequenceDto? Sequence { get; init; }
}

public static class SkLivePaperCandidateEvaluator
{
    public static SkLivePaperEvaluationResult Evaluate(
        SkLivePaperSession session,
        SkSequenceDto sequence,
        SkSystemAnalysisResultDto analysis,
        Candle closedCandle,
        int openTradeCount,
        int tradesOpenedToday)
    {
        if (sequence.ValidationStatus == SkScenarioValidator.DirectionMismatch)
        {
            return Reject(SkLivePaperRejectionReasons.DirectionMismatch);
        }

        if (sequence.ValidityStatus is "Invalid" or "DirectionMismatch" or "StructureInvalidated" or "InsufficientData")
        {
            return Reject(sequence.ValidityStatus);
        }

        if (sequence.UsefulnessStatus is "AlreadyReached" or "Invalidated" or "DirectionMismatch" or "TooFarAway")
        {
            return Reject(sequence.UsefulnessStatus);
        }

        if (sequence.ClarityScore < session.MinClarityScore)
        {
            return Reject(SkLivePaperRejectionReasons.LowClarity);
        }

        if (sequence.UsefulnessScore < session.MinUsefulnessScore)
        {
            return Reject(SkLivePaperRejectionReasons.LowUsefulness);
        }

        var upward = sequence.Direction == "Upward";
        if (upward && !session.AllowLong)
        {
            return Reject(SkLivePaperRejectionReasons.DirectionNotAllowed);
        }

        if (!upward && !session.AllowShort)
        {
            return Reject(SkLivePaperRejectionReasons.DirectionNotAllowed);
        }

        if (session.RequireHtfAgreement && analysis.ConceptAudit is { HtfLtfAgreement: false })
        {
            return Reject(SkLivePaperRejectionReasons.HtfDisagreement);
        }

        if (openTradeCount >= session.MaxOpenPaperPositions)
        {
            return Reject(SkLivePaperRejectionReasons.MaxOpenPositionsReached);
        }

        if (tradesOpenedToday >= session.MaxPaperTradesPerDay)
        {
            return Reject(SkLivePaperRejectionReasons.MaxTradesPerDayReached);
        }

        if (sequence.InvalidationLevel <= 0)
        {
            return Reject(SkLivePaperRejectionReasons.MissingInvalidation);
        }

        if (sequence.Target1 <= 0)
        {
            return Reject(SkLivePaperRejectionReasons.MissingTarget);
        }

        if (!PassesDirectionalGeometry(sequence))
        {
            return Reject(SkLivePaperRejectionReasons.DirectionMismatch);
        }

        if (session.RequireReactionConfirmation && !IsConfirmed(session, sequence, closedCandle))
        {
            return new SkLivePaperEvaluationResult
            {
                CanOpenTrade = false,
                WaitingForConfirmation = true,
                RejectionReason = SkLivePaperRejectionReasons.WaitingForReactionConfirmation,
                Sequence = sequence
            };
        }

        var sizing = SkLivePaperPositionSizer.TrySize(
            session.CurrentBalance,
            session.RiskPerPaperTradePercent,
            closedCandle.Close,
            sequence.InvalidationLevel,
            session.SimulatedLeverage);

        if (!sizing.IsValid)
        {
            return Reject(SkLivePaperRejectionReasons.InvalidRiskReward);
        }

        return new SkLivePaperEvaluationResult
        {
            CanOpenTrade = true,
            Sequence = sequence
        };
    }

    private static bool PassesDirectionalGeometry(SkSequenceDto sequence)
    {
        if (sequence.Direction == "Upward")
        {
            return sequence.Target1 > sequence.CorrectionZoneHigh
                && sequence.Target2 > sequence.Target1
                && sequence.InvalidationLevel < sequence.CorrectionZoneLow;
        }

        return sequence.Target1 < sequence.CorrectionZoneLow
            && sequence.Target2 < sequence.Target1
            && sequence.InvalidationLevel > sequence.CorrectionZoneHigh;
    }

    private static bool IsConfirmed(SkLivePaperSession session, SkSequenceDto sequence, Candle closedCandle)
    {
        if (!string.Equals(session.ConfirmationMode, "CloseBackInDirection", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var zoneLow = sequence.CorrectionZoneLow;
        var zoneHigh = sequence.CorrectionZoneHigh;
        var midpoint = (zoneLow + zoneHigh) / 2m;
        var inZone = closedCandle.Close >= zoneLow && closedCandle.Close <= zoneHigh;
        var wasInZone = closedCandle.Low <= zoneHigh && closedCandle.High >= zoneLow;

        if (!wasInZone && !inZone)
        {
            return false;
        }

        return sequence.Direction == "Upward"
            ? closedCandle.Close >= midpoint && closedCandle.Close >= closedCandle.Open
            : closedCandle.Close <= midpoint && closedCandle.Close <= closedCandle.Open;
    }

    private static SkLivePaperEvaluationResult Reject(string reason) => new()
    {
        CanOpenTrade = false,
        RejectionReason = reason
    };
}

public sealed class SkLivePaperPositionSizingResult
{
    public bool IsValid { get; init; }
    public decimal Quantity { get; init; }
    public decimal NotionalValue { get; init; }
    public decimal MarginUsed { get; init; }
    public decimal RiskAmount { get; init; }
}

public static class SkLivePaperPositionSizer
{
    public static SkLivePaperPositionSizingResult TrySize(
        decimal currentBalance,
        decimal riskPercent,
        decimal entryPrice,
        decimal stopLoss,
        decimal leverage)
    {
        var riskAmount = currentBalance * riskPercent / 100m;
        var stopDistance = Math.Abs(entryPrice - stopLoss);
        if (stopDistance <= 0 || entryPrice <= 0 || leverage <= 0 || riskAmount <= 0)
        {
            return new SkLivePaperPositionSizingResult { IsValid = false };
        }

        var quantity = riskAmount / stopDistance;
        var notional = quantity * entryPrice;
        var margin = notional / leverage;
        if (quantity <= 0 || margin > currentBalance)
        {
            return new SkLivePaperPositionSizingResult { IsValid = false };
        }

        return new SkLivePaperPositionSizingResult
        {
            IsValid = true,
            Quantity = decimal.Round(quantity, 8),
            NotionalValue = decimal.Round(notional, 8),
            MarginUsed = decimal.Round(margin, 8),
            RiskAmount = decimal.Round(riskAmount, 8)
        };
    }
}

public static class SkLivePaperTradeCloser
{
    public static (bool ShouldClose, decimal ExitPrice, SkLivePaperTradeExitReason Reason)? Evaluate(
        SkLivePaperTrade trade,
        Candle closedCandle)
    {
        if (trade.Status != SkLivePaperTradeStatus.Open)
        {
            return null;
        }

        var isLong = trade.Direction is "Bullish" or "Upward" or "Long";
        if (isLong)
        {
            if (closedCandle.Low <= trade.StopLoss)
            {
                return (true, trade.StopLoss, SkLivePaperTradeExitReason.StopLoss);
            }

            if (closedCandle.High >= trade.TakeProfit1)
            {
                return (true, trade.TakeProfit1, SkLivePaperTradeExitReason.Target1);
            }
        }
        else
        {
            if (closedCandle.High >= trade.StopLoss)
            {
                return (true, trade.StopLoss, SkLivePaperTradeExitReason.StopLoss);
            }

            if (closedCandle.Low <= trade.TakeProfit1)
            {
                return (true, trade.TakeProfit1, SkLivePaperTradeExitReason.Target1);
            }
        }

        return null;
    }

    public static (decimal GrossPnl, decimal Fees, decimal Slippage, decimal NetPnl, decimal NetPnlPercent) ComputePnl(
        SkLivePaperTrade trade,
        decimal exitPrice)
    {
        var isLong = trade.Direction is "Bullish" or "Upward" or "Long";
        var gross = isLong
            ? (exitPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - exitPrice) * trade.Quantity;

        var notional = trade.Quantity * exitPrice;
        var fees = notional * SkLivePaperConstants.DefaultTakerFeePercent / 100m * 2m;
        var slippage = notional * SkLivePaperConstants.DefaultSlippagePercent / 100m;
        var net = gross - fees - slippage;
        var netPct = trade.MarginUsed > 0 ? net / trade.MarginUsed * 100m : 0m;

        return (
            decimal.Round(gross, 8),
            decimal.Round(fees, 8),
            decimal.Round(slippage, 8),
            decimal.Round(net, 8),
            decimal.Round(netPct, 4));
    }
}
