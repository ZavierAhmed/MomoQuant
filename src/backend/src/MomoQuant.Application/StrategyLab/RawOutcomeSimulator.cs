using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

public sealed class RawOutcomeSimulationRequest
{
    public required StrategyResearchCandidate Candidate { get; init; }
    public required IReadOnlyList<Candle> Candles { get; init; }
    public required int EntryCandleIndex { get; init; }
    public decimal TakerFeeRate { get; init; } = 0.0004m;
    public decimal SlippagePercent { get; init; }
    public decimal Quantity { get; init; } = 1m;
    public int MaxBarsOpen { get; init; } = 500;
}

public static class RawOutcomeSimulator
{
    public static void Simulate(RawOutcomeSimulationRequest request)
    {
        var candidate = request.Candidate;
        var candles = request.Candles;
        var entryIndex = request.EntryCandleIndex;

        if (entryIndex < 0 || entryIndex >= candles.Count)
        {
            MarkInvalid(candidate, "Entry candle index out of range.");
            return;
        }

        var entry = candidate.ProposedEntryPrice;
        var stop = candidate.StopLoss;
        var target = candidate.Target1;
        var isLong = candidate.Direction == TradeDirection.Long;

        if (entry <= 0 || stop <= 0 || target <= 0)
        {
            MarkInvalid(candidate, "Invalid entry/stop/target prices.");
            return;
        }

        if (isLong && (stop >= entry || target <= entry))
        {
            MarkInvalid(candidate, "Stop or target on wrong side for long.");
            return;
        }

        if (!isLong && (stop <= entry || target >= entry))
        {
            MarkInvalid(candidate, "Stop or target on wrong side for short.");
            return;
        }

        var risk = isLong ? entry - stop : stop - entry;
        if (risk <= 0)
        {
            MarkInvalid(candidate, "Zero or negative stop distance.");
            return;
        }

        if (request.Quantity <= 0)
        {
            MarkInvalid(candidate, "Zero or negative quantity.");
            return;
        }

        candidate.RawOutcomeStatus = RawOutcomeStatus.Open;
        candidate.CandidateStatus = StrategyResearchCandidateStatus.Simulated;

        decimal mfe = 0m;
        decimal mae = 0m;
        var endIndex = Math.Min(candles.Count - 1, entryIndex + request.MaxBarsOpen);

        for (var i = entryIndex + 1; i <= endIndex; i++)
        {
            var candle = candles[i];
            var barsHeld = i - entryIndex;

            if (isLong)
            {
                mfe = Math.Max(mfe, candle.High - entry);
                mae = Math.Max(mae, entry - candle.Low);

                var stopHit = candle.Low <= stop;
                var targetHit = candle.High >= target;
                if (stopHit && targetHit)
                {
                    Close(candidate, candle, stop, "StopLoss", request, risk, barsHeld, mfe, mae, isLong);
                    return;
                }

                if (stopHit)
                {
                    Close(candidate, candle, stop, "StopLoss", request, risk, barsHeld, mfe, mae, isLong);
                    return;
                }

                if (targetHit)
                {
                    Close(candidate, candle, target, "TakeProfit", request, risk, barsHeld, mfe, mae, isLong);
                    return;
                }
            }
            else
            {
                mfe = Math.Max(mfe, entry - candle.Low);
                mae = Math.Max(mae, candle.High - entry);

                var stopHit = candle.High >= stop;
                var targetHit = candle.Low <= target;
                if (stopHit && targetHit)
                {
                    Close(candidate, candle, stop, "StopLoss", request, risk, barsHeld, mfe, mae, isLong);
                    return;
                }

                if (stopHit)
                {
                    Close(candidate, candle, stop, "StopLoss", request, risk, barsHeld, mfe, mae, isLong);
                    return;
                }

                if (targetHit)
                {
                    Close(candidate, candle, target, "TakeProfit", request, risk, barsHeld, mfe, mae, isLong);
                    return;
                }
            }
        }

        var last = candles[endIndex];
        var exitPrice = last.Close;
        Close(candidate, last, exitPrice, "Expired", request, risk, endIndex - entryIndex, mfe, mae, isLong);
        candidate.RawOutcomeStatus = RawOutcomeStatus.Expired;
    }

    private static void Close(
        StrategyResearchCandidate candidate,
        Candle candle,
        decimal exitPrice,
        string reason,
        RawOutcomeSimulationRequest request,
        decimal risk,
        int durationBars,
        decimal mfe,
        decimal mae,
        bool isLong)
    {
        var entry = candidate.ProposedEntryPrice;
        var qty = request.Quantity;
        var gross = isLong
            ? (exitPrice - entry) * qty
            : (entry - exitPrice) * qty;

        var entryFee = entry * qty * request.TakerFeeRate;
        var exitFee = exitPrice * qty * request.TakerFeeRate;
        var net = gross - entryFee - exitFee;
        var pnlPercent = entry > 0 ? net / (entry * qty) * 100m : 0m;
        var rMultiple = risk > 0
            ? (isLong ? exitPrice - entry : entry - exitPrice) / risk
            : 0m;

        candidate.RawExitTimeUtc = candle.CloseTimeUtc;
        candidate.RawExitPrice = exitPrice;
        candidate.RawExitReason = reason;
        candidate.RawGrossPnl = gross;
        candidate.RawNetPnl = net;
        candidate.RawPnlPercent = pnlPercent;
        candidate.RawRMultiple = rMultiple;
        candidate.Mfe = mfe;
        candidate.Mae = mae;
        candidate.DurationBars = durationBars;
        candidate.CandidateStatus = StrategyResearchCandidateStatus.Closed;
        candidate.UpdatedAtUtc = DateTime.UtcNow;

        // Exit event classification (candle event) — separate from net profitability.
        candidate.ExitOutcome = reason switch
        {
            "TakeProfit" => ResearchExitOutcome.TargetHit,
            "StopLoss" => ResearchExitOutcome.StopHit,
            "Expired" => ResearchExitOutcome.Expired,
            _ => ResearchExitOutcome.Invalid
        };

        // Net profitability after fees.
        candidate.NetResult = net > 0.00000001m
            ? ResearchNetResult.Profitable
            : net < -0.00000001m
                ? ResearchNetResult.Losing
                : ResearchNetResult.Breakeven;

        // Legacy RawOutcomeStatus kept for historical compatibility (Winner/Loser conflated exit+PnL).
        candidate.RawOutcomeStatus = reason switch
        {
            "TakeProfit" when Math.Abs(rMultiple) < 0.05m => RawOutcomeStatus.Breakeven,
            "TakeProfit" => RawOutcomeStatus.Winner,
            "StopLoss" => RawOutcomeStatus.Loser,
            "Expired" => RawOutcomeStatus.Expired,
            _ => net > 0 ? RawOutcomeStatus.Winner : net < 0 ? RawOutcomeStatus.Loser : RawOutcomeStatus.Breakeven
        };
    }

    private static void MarkInvalid(StrategyResearchCandidate candidate, string reason)
    {
        candidate.CandidateStatus = StrategyResearchCandidateStatus.SimulationInvalid;
        candidate.RawOutcomeStatus = RawOutcomeStatus.Invalid;
        candidate.ExitOutcome = ResearchExitOutcome.Invalid;
        candidate.NetResult = ResearchNetResult.Unknown;
        candidate.RawExitReason = reason;
        candidate.UpdatedAtUtc = DateTime.UtcNow;
    }
}
