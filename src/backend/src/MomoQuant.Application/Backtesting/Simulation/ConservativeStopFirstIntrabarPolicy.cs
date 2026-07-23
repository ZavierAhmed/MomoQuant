using MomoQuant.Application.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Backtesting.Simulation;

public enum IntrabarChosenEvent
{
    None = 0,
    OriginalStop = 1,
    Target = 2,
    BreakevenActivation = 3,
    MakerEntry = 4,
    StopEntry = 5,
    GapBeyondStop = 6,
    GapBeyondTarget = 7
}

public sealed class IntrabarEvaluationResult
{
    public required string PolicyVersion { get; init; }
    public required IntrabarChosenEvent ChosenEvent { get; init; }
    public required string Reason { get; init; }
    public bool Ambiguity { get; init; }
    public IReadOnlyList<IntrabarChosenEvent> ConflictingEvents { get; init; } = [];
    public decimal? ExitPrice { get; init; }
    public bool ActivateBreakeven { get; init; }
}

public interface IIntrabarExecutionPolicy
{
    string Version { get; }

    IntrabarEvaluationResult EvaluateProtectiveExits(
        TradeDirection direction,
        decimal originalStop,
        decimal target,
        Candle candle,
        SameCandleExitPolicy configuredPolicy);

    IntrabarEvaluationResult EvaluateBreakevenActivation(
        TradeDirection direction,
        decimal originalStop,
        decimal? breakevenTriggerPrice,
        Candle candle);

    bool LimitEntryTouched(TradeDirection direction, decimal limitPrice, Candle candle);

    bool StopEntryTriggered(TradeDirection direction, decimal stopEntryPrice, Candle candle);
}

/// <summary>
/// ConservativeStopFirst/v1 — when OHLC cannot prove order, original protective stop wins;
/// breakeven is not activated before evaluating the original stop on the same candle.
/// </summary>
public sealed class ConservativeStopFirstIntrabarPolicy : IIntrabarExecutionPolicy
{
    public const string PolicyVersion = "ConservativeStopFirst/v1";
    public string Version => PolicyVersion;

    public static ConservativeStopFirstIntrabarPolicy Instance { get; } = new();

    public IntrabarEvaluationResult EvaluateProtectiveExits(
        TradeDirection direction,
        decimal originalStop,
        decimal target,
        Candle candle,
        SameCandleExitPolicy configuredPolicy)
    {
        var stopHit = IsStopHit(direction, originalStop, candle);
        var targetHit = IsTargetHit(direction, target, candle);
        var gapStop = IsGapBeyondStop(direction, originalStop, candle);
        var gapTarget = IsGapBeyondTarget(direction, target, candle);

        if (gapStop && !stopHit)
        {
            // Open gapped through stop.
            stopHit = true;
        }

        if (gapTarget && !targetHit)
        {
            targetHit = true;
        }

        if (stopHit && targetHit)
        {
            var stopFirst = ResolveAmbiguousStopFirst(configuredPolicy, direction);
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = stopFirst ? IntrabarChosenEvent.OriginalStop : IntrabarChosenEvent.Target,
                Reason = stopFirst
                    ? "Ambiguous OHLC: original protective stop takes priority."
                    : "Ambiguous OHLC: configured policy prefers target.",
                Ambiguity = true,
                ConflictingEvents = [IntrabarChosenEvent.OriginalStop, IntrabarChosenEvent.Target],
                ExitPrice = stopFirst ? originalStop : target
            };
        }

        if (stopHit)
        {
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = gapStop ? IntrabarChosenEvent.GapBeyondStop : IntrabarChosenEvent.OriginalStop,
                Reason = gapStop ? "Gap beyond original stop." : "Original stop touched.",
                Ambiguity = false,
                ExitPrice = originalStop
            };
        }

        if (targetHit)
        {
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = gapTarget ? IntrabarChosenEvent.GapBeyondTarget : IntrabarChosenEvent.Target,
                Reason = gapTarget ? "Gap beyond target." : "Target touched.",
                Ambiguity = false,
                ExitPrice = target
            };
        }

        return new IntrabarEvaluationResult
        {
            PolicyVersion = PolicyVersion,
            ChosenEvent = IntrabarChosenEvent.None,
            Reason = "No protective exit on this candle.",
            Ambiguity = false
        };
    }

    public IntrabarEvaluationResult EvaluateBreakevenActivation(
        TradeDirection direction,
        decimal originalStop,
        decimal? breakevenTriggerPrice,
        Candle candle)
    {
        if (breakevenTriggerPrice is not decimal trigger)
        {
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = IntrabarChosenEvent.None,
                Reason = "No breakeven trigger configured.",
                Ambiguity = false,
                ActivateBreakeven = false
            };
        }

        var stopHit = IsStopHit(direction, originalStop, candle);
        var beHit = direction == TradeDirection.Long
            ? candle.High >= trigger
            : candle.Low <= trigger;

        if (stopHit && beHit)
        {
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = IntrabarChosenEvent.OriginalStop,
                Reason = "Breakeven trigger and original stop same candle: original stop wins; do not activate breakeven.",
                Ambiguity = true,
                ConflictingEvents = [IntrabarChosenEvent.OriginalStop, IntrabarChosenEvent.BreakevenActivation],
                ExitPrice = originalStop,
                ActivateBreakeven = false
            };
        }

        if (stopHit)
        {
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = IntrabarChosenEvent.OriginalStop,
                Reason = "Original stop hit; breakeven not considered.",
                Ambiguity = false,
                ExitPrice = originalStop,
                ActivateBreakeven = false
            };
        }

        if (beHit)
        {
            return new IntrabarEvaluationResult
            {
                PolicyVersion = PolicyVersion,
                ChosenEvent = IntrabarChosenEvent.BreakevenActivation,
                Reason = "Breakeven trigger reached without original stop.",
                Ambiguity = false,
                ActivateBreakeven = true
            };
        }

        return new IntrabarEvaluationResult
        {
            PolicyVersion = PolicyVersion,
            ChosenEvent = IntrabarChosenEvent.None,
            Reason = "Neither original stop nor breakeven trigger.",
            Ambiguity = false,
            ActivateBreakeven = false
        };
    }

    public bool LimitEntryTouched(TradeDirection direction, decimal limitPrice, Candle candle) =>
        candle.Low <= limitPrice && candle.High >= limitPrice;

    public bool StopEntryTriggered(TradeDirection direction, decimal stopEntryPrice, Candle candle) =>
        direction == TradeDirection.Long
            ? candle.High >= stopEntryPrice
            : candle.Low <= stopEntryPrice;

    private static bool IsStopHit(TradeDirection direction, decimal stop, Candle candle) =>
        direction == TradeDirection.Long ? candle.Low <= stop : candle.High >= stop;

    private static bool IsTargetHit(TradeDirection direction, decimal target, Candle candle) =>
        direction == TradeDirection.Long ? candle.High >= target : candle.Low <= target;

    private static bool IsGapBeyondStop(TradeDirection direction, decimal stop, Candle candle) =>
        direction == TradeDirection.Long ? candle.Open < stop : candle.Open > stop;

    private static bool IsGapBeyondTarget(TradeDirection direction, decimal target, Candle candle) =>
        direction == TradeDirection.Long ? candle.Open > target : candle.Open < target;

    private static bool ResolveAmbiguousStopFirst(SameCandleExitPolicy policy, TradeDirection direction) =>
        policy switch
        {
            SameCandleExitPolicy.TargetFirst => false,
            SameCandleExitPolicy.OpenHighLowCloseHeuristic => direction == TradeDirection.Long,
            _ => true // ConservativeStopFirst
        };
}
