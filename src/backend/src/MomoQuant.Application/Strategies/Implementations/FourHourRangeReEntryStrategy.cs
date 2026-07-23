using System.Text.Json;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class FourHourRangeReEntryStrategy : StrategyBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IFourHourRangeService _rangeService;
    private readonly ILogger<FourHourRangeReEntryStrategy>? _logger;

    public FourHourRangeReEntryStrategy()
        : this(new FourHourRangeService(), null)
    {
    }

    public FourHourRangeReEntryStrategy(
        IFourHourRangeService rangeService,
        ILogger<FourHourRangeReEntryStrategy>? logger = null)
    {
        _rangeService = rangeService;
        _logger = logger;
    }

    public override StrategyCode Code => StrategyCode.FourHourRangeReEntry;

    public override string Name => "4H Range Re-Entry Scalping";

    public override string Description =>
        "Uses the first 4 hours of the New York trading day. Enters after price closes outside the range and then closes back inside.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Ranging, MarketRegime.Reversal, MarketRegime.Breakout, MarketRegime.LowVolatility, MarketRegime.Unknown];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5, Timeframe.M15];

    public void PrecomputeRanges(
        long symbolId,
        Timeframe timeframe,
        IReadOnlyList<Candle> candles,
        IReadOnlyDictionary<string, string> rawParameters)
    {
        var parameters = FourHourRangeReEntryParameters.From(rawParameters);
        var startedAtUtc = DateTime.UtcNow;
        _logger?.LogInformation(
            "4H range precompute started. StrategyCode={StrategyCode}, SymbolId={SymbolId}, Timeframe={Timeframe}, CandleCount={CandleCount}",
            StrategyCode.FourHourRangeReEntry.ToCode(),
            symbolId,
            TimeframeParser.ToApiString(timeframe),
            candles.Count);

        var ranges = _rangeService.BuildRangesFromCandles(symbolId, timeframe, candles, parameters);
        var durationMs = (int)Math.Max(0, (DateTime.UtcNow - startedAtUtc).TotalMilliseconds);
        _logger?.LogInformation(
            "4H range precompute completed. StrategyCode={StrategyCode}, SymbolId={SymbolId}, Timeframe={Timeframe}, BuiltRangeCount={BuiltRangeCount}, DurationMs={DurationMs}",
            StrategyCode.FourHourRangeReEntry.ToCode(),
            symbolId,
            TimeframeParser.ToApiString(timeframe),
            ranges.Count,
            durationMs);
    }

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        var parameters = FourHourRangeReEntryParameters.From(context.StrategyParameters);
        var supportedTimeframes = parameters.ResolveSupportedTimeframes();

        if (!IsSupportedTimeframe(context.Timeframe, supportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by 4H Range Re-Entry Scalping.");
        }

        if (context.MarketRegime == MarketRegime.Abnormal)
        {
            return NoTrade("Market regime Abnormal is not allowed.");
        }

        if (context.MarketRegime == MarketRegime.Choppy && !parameters.AllowChoppy)
        {
            return NoTrade("Choppy market conditions are not allowed for 4H Range Re-Entry Scalping.");
        }

        if (context.MarketRegime == MarketRegime.HighVolatility && !parameters.AllowHighVolatility)
        {
            return NoTrade("High volatility is not allowed for 4H Range Re-Entry Scalping.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by 4H Range Re-Entry Scalping.");
        }

        var current = context.CurrentCandle;
        if (current is null)
        {
            return NoTrade("No current candle is available.");
        }

        if (!current.IsClosed)
        {
            return NoTrade("Current candle is still forming; waiting for close.");
        }

        var range = _rangeService.GetRangeForCandle(
            context.SymbolId,
            context.Timeframe,
            current.CloseTimeUtc,
            context.Candles,
            parameters);

        if (!range.IsValid || range.RangeHigh is null || range.RangeLow is null)
        {
            var reason = MapRangeInvalidReason(range.InvalidReason);
            return NoTradeWithDiagnostics(reason, BuildDiagnostics(context, range, parameters, FourHourRangeState.RangeNotReady, reason));
        }

        if (parameters.DisableAfterNewYorkDayEnd &&
            FourHourRangeService.EnsureUtc(current.CloseTimeUtc) >= FourHourRangeService.EnsureUtc(range.NewYorkDayEndUtc))
        {
            var reason = "Setup expired because New York trading day ended.";
            return NoTradeWithDiagnostics(reason, BuildDiagnostics(context, range, parameters, FourHourRangeState.CompletedOrExpired, reason));
        }

        if (range.RangePercent is null ||
            range.RangePercent.Value < parameters.MinRangePercent ||
            range.RangePercent.Value > parameters.MaxRangePercent)
        {
            var reason = "First 4H range is outside the configured range-size limits.";
            return NoTradeWithDiagnostics(reason, BuildDiagnostics(context, range, parameters, FourHourRangeState.WaitingForBreakout, reason));
        }

        var sequence = AnalyzeSession(context, parameters, range, current);
        if (sequence.Signal is null)
        {
            return NoTradeWithDiagnostics(
                sequence.Reason,
                BuildDiagnostics(
                    context,
                    range,
                    parameters,
                    sequence.State,
                    sequence.Reason,
                    sequence.BreakoutDirection,
                    sequence.BreakoutCandleCloseTimeUtc,
                    sequence.BreakoutExtreme,
                    completedTradesToday: sequence.CompletedTradesToday));
        }

        if (!parameters.AllowMultipleTradesPerDay && sequence.CompletedTradesToday >= 1)
        {
            const string reason = "Maximum trades for this day already reached.";
            return NoTradeWithDiagnostics(
                reason,
                BuildDiagnostics(
                    context,
                    range,
                    parameters,
                    FourHourRangeState.CompletedOrExpired,
                    reason,
                    sequence.BreakoutDirection,
                    sequence.BreakoutCandleCloseTimeUtc,
                    sequence.BreakoutExtreme,
                    completedTradesToday: sequence.CompletedTradesToday));
        }

        if (sequence.CompletedTradesToday >= parameters.MaxTradesPerDay)
        {
            const string reason = "Maximum trades for this day already reached.";
            return NoTradeWithDiagnostics(
                reason,
                BuildDiagnostics(
                    context,
                    range,
                    parameters,
                    FourHourRangeState.CompletedOrExpired,
                    reason,
                    sequence.BreakoutDirection,
                    sequence.BreakoutCandleCloseTimeUtc,
                    sequence.BreakoutExtreme,
                    completedTradesToday: sequence.CompletedTradesToday));
        }

        var signal = sequence.Signal;
        var stopResult = CalculateStopTarget(context, parameters, range, current, signal.Value);
        if (!stopResult.Valid)
        {
            return NoTradeWithDiagnostics(
                stopResult.Reason,
                BuildDiagnostics(
                    context,
                    range,
                    parameters,
                    signal.Value.Direction == TradeDirection.Short ? FourHourRangeState.AwaitingShortReentry : FourHourRangeState.AwaitingLongReentry,
                    stopResult.Reason,
                    sequence.BreakoutDirection,
                    sequence.BreakoutCandleCloseTimeUtc,
                    sequence.BreakoutExtreme,
                    completedTradesToday: sequence.CompletedTradesToday,
                    entryPrice: current.Close,
                    stopLoss: stopResult.StopLoss,
                    takeProfit: stopResult.TakeProfit,
                    stopDistancePercent: stopResult.StopDistancePercent));
        }

        var strength = CalculateStrength(context, parameters, range, current, signal.Value.Direction);
        var entryReason = signal.Value.Direction == TradeDirection.Short
            ? "Price closed above the 4H New York range and then closed back inside; short re-entry setup is ready."
            : "Price closed below the 4H New York range and then closed back inside; long re-entry setup is ready.";

        var rawDataJson = BuildDiagnostics(
            context,
            range,
            parameters,
            signal.Value.Direction == TradeDirection.Short ? FourHourRangeState.ShortSignalReady : FourHourRangeState.LongSignalReady,
            entryReason,
            sequence.BreakoutDirection,
            sequence.BreakoutCandleCloseTimeUtc,
            sequence.BreakoutExtreme,
            reentryCandleCloseTimeUtc: current.CloseTimeUtc,
            completedTradesToday: sequence.CompletedTradesToday,
            entryPrice: current.Close,
            stopLoss: stopResult.StopLoss,
            takeProfit: stopResult.TakeProfit,
            riskPerUnit: stopResult.RiskPerUnit,
            stopDistancePercent: stopResult.StopDistancePercent,
            tradeNumber: sequence.CompletedTradesToday + 1);

        return Entry(
            signal.Value.Direction,
            strength,
            strength,
            current.Close,
            stopResult.StopLoss,
            stopResult.TakeProfit,
            entryReason,
            rawDataJson);
    }

    private static string MapRangeInvalidReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "First 4H range is not ready.";
        }

        if (reason.Contains("not closed", StringComparison.OrdinalIgnoreCase))
        {
            return "First 4H NY range has not closed yet.";
        }

        if (reason.Contains("Not enough candles", StringComparison.OrdinalIgnoreCase))
        {
            return "Not enough candles to build first 4H range.";
        }

        return reason;
    }

    private static SessionAnalysis AnalyzeSession(
        StrategyContext context,
        FourHourRangeReEntryParameters parameters,
        FourHourRangeDto range,
        Candle current)
    {
        var rangeHigh = range.RangeHigh!.Value;
        var rangeLow = range.RangeLow!.Value;
        var currentCloseUtc = FourHourRangeService.EnsureUtc(current.CloseTimeUtc);
        var dayEndUtc = FourHourRangeService.EnsureUtc(range.NewYorkDayEndUtc);

        var state = FourHourRangeState.WaitingForBreakout;
        var breakoutDirection = "None";
        DateTime? breakoutCloseTimeUtc = null;
        decimal? breakoutExtreme = null;
        var completedTradesToday = 0;
        string? wickOnlyReason = null;

        var rangeEndUtc = FourHourRangeService.EnsureUtc(range.RangeEndUtc);
        // Context candles are chronological in backtests; avoid OrderBy on the full list each bar.
        var candles = new List<Candle>();
        foreach (var candle in context.Candles)
        {
            if (candle.SymbolId != context.SymbolId || candle.Timeframe != context.Timeframe || !candle.IsClosed)
            {
                continue;
            }

            var closeUtc = FourHourRangeService.EnsureUtc(candle.CloseTimeUtc);
            if (closeUtc > rangeEndUtc && closeUtc <= currentCloseUtc && closeUtc < dayEndUtc)
            {
                candles.Add(candle);
            }
        }

        foreach (var candle in candles)
        {
            var isCurrent = (candle.Id == current.Id && current.Id != 0) ||
                FourHourRangeService.EnsureUtc(candle.CloseTimeUtc) == currentCloseUtc;

            if (state == FourHourRangeState.WaitingForBreakout)
            {
                var closeAbove = candle.Close > rangeHigh;
                var closeBelow = candle.Close < rangeLow;
                var wickAbove = candle.High > rangeHigh;
                var wickBelow = candle.Low < rangeLow;

                var brokeAbove = parameters.RequireCloseOutsideRange
                    ? closeAbove
                    : parameters.UseWicksForBreakout ? wickAbove : closeAbove;
                var brokeBelow = parameters.RequireCloseOutsideRange
                    ? closeBelow
                    : parameters.UseWicksForBreakout ? wickBelow : closeBelow;

                if (!brokeAbove && wickAbove && !closeAbove)
                {
                    wickOnlyReason = "Wick crossed range high, but candle did not close outside.";
                }

                if (!brokeBelow && wickBelow && !closeBelow)
                {
                    wickOnlyReason = "Wick crossed range low, but candle did not close outside.";
                }

                if (brokeAbove)
                {
                    state = FourHourRangeState.AwaitingShortReentry;
                    breakoutDirection = "Above";
                    breakoutCloseTimeUtc = candle.CloseTimeUtc;
                    breakoutExtreme = candle.High;
                    continue;
                }

                if (brokeBelow)
                {
                    state = FourHourRangeState.AwaitingLongReentry;
                    breakoutDirection = "Below";
                    breakoutCloseTimeUtc = candle.CloseTimeUtc;
                    breakoutExtreme = candle.Low;
                    continue;
                }

                continue;
            }

            if (state == FourHourRangeState.AwaitingShortReentry)
            {
                breakoutExtreme = Math.Max(breakoutExtreme ?? candle.High, candle.High);
                if (CandleClosedBackInsideFromAbove(candle, rangeHigh, rangeLow, parameters))
                {
                    if (isCurrent)
                    {
                        return new SessionAnalysis(
                            FourHourRangeState.ShortSignalReady,
                            "Short re-entry setup is ready.",
                            breakoutDirection,
                            breakoutCloseTimeUtc,
                            breakoutExtreme,
                            completedTradesToday,
                            new ReentrySignal(TradeDirection.Short, breakoutExtreme!.Value));
                    }

                    completedTradesToday++;
                    state = FourHourRangeState.WaitingForBreakout;
                    breakoutDirection = "None";
                    breakoutCloseTimeUtc = null;
                    breakoutExtreme = null;
                    continue;
                }

                if (candle.Close < rangeLow)
                {
                    state = FourHourRangeState.AwaitingLongReentry;
                    breakoutDirection = "Below";
                    breakoutCloseTimeUtc = candle.CloseTimeUtc;
                    breakoutExtreme = candle.Low;
                }

                continue;
            }

            if (state == FourHourRangeState.AwaitingLongReentry)
            {
                breakoutExtreme = Math.Min(breakoutExtreme ?? candle.Low, candle.Low);
                if (CandleClosedBackInsideFromBelow(candle, rangeHigh, rangeLow, parameters))
                {
                    if (isCurrent)
                    {
                        return new SessionAnalysis(
                            FourHourRangeState.LongSignalReady,
                            "Long re-entry setup is ready.",
                            breakoutDirection,
                            breakoutCloseTimeUtc,
                            breakoutExtreme,
                            completedTradesToday,
                            new ReentrySignal(TradeDirection.Long, breakoutExtreme!.Value));
                    }

                    completedTradesToday++;
                    state = FourHourRangeState.WaitingForBreakout;
                    breakoutDirection = "None";
                    breakoutCloseTimeUtc = null;
                    breakoutExtreme = null;
                    continue;
                }

                if (candle.Close > rangeHigh)
                {
                    state = FourHourRangeState.AwaitingShortReentry;
                    breakoutDirection = "Above";
                    breakoutCloseTimeUtc = candle.CloseTimeUtc;
                    breakoutExtreme = candle.High;
                }
            }
        }

        var reason = state switch
        {
            FourHourRangeState.AwaitingShortReentry => "Breakout above range detected; waiting for close back inside.",
            FourHourRangeState.AwaitingLongReentry => "Breakout below range detected; waiting for close back inside.",
            _ => wickOnlyReason ?? "Waiting for close outside 4H range."
        };

        return new SessionAnalysis(
            state,
            reason,
            breakoutDirection,
            breakoutCloseTimeUtc,
            breakoutExtreme,
            completedTradesToday,
            Signal: null);
    }

    private static bool CandleClosedBackInsideFromAbove(
        Candle candle,
        decimal rangeHigh,
        decimal rangeLow,
        FourHourRangeReEntryParameters parameters) =>
        parameters.RequireCloseBackInsideRange
            ? candle.Close < rangeHigh && candle.Close >= rangeLow
            : candle.Low < rangeHigh && candle.Close >= rangeLow;

    private static bool CandleClosedBackInsideFromBelow(
        Candle candle,
        decimal rangeHigh,
        decimal rangeLow,
        FourHourRangeReEntryParameters parameters) =>
        parameters.RequireCloseBackInsideRange
            ? candle.Close > rangeLow && candle.Close <= rangeHigh
            : candle.High > rangeLow && candle.Close <= rangeHigh;

    private static StopTargetResult CalculateStopTarget(
        StrategyContext context,
        FourHourRangeReEntryParameters parameters,
        FourHourRangeDto range,
        Candle current,
        ReentrySignal signal)
    {
        if (signal.BreakoutExtreme <= 0m)
        {
            return StopTargetResult.Invalid("Price re-entered, but breakout structure is invalid.");
        }

        var entry = current.Close;
        var atrBuffer = context.IndicatorSnapshot?.Atr14 is decimal atr && parameters.StopLossBufferAtrMultiplier > 0m
            ? atr * parameters.StopLossBufferAtrMultiplier
            : 0m;
        var percentBuffer = signal.BreakoutExtreme * parameters.StopLossBufferPercent / 100m;
        var buffer = Math.Max(percentBuffer, parameters.StopLossBufferTicks) + atrBuffer;

        var stopLoss = signal.Direction == TradeDirection.Short
            ? signal.BreakoutExtreme + buffer
            : signal.BreakoutExtreme - buffer;

        var riskPerUnit = signal.Direction == TradeDirection.Short
            ? stopLoss - entry
            : entry - stopLoss;

        if (stopLoss <= 0m || entry <= 0m || riskPerUnit <= 0m)
        {
            return StopTargetResult.Invalid("Price re-entered, but stop placement is invalid.", stopLoss);
        }

        var stopDistancePercent = riskPerUnit / entry * 100m;
        var takeProfit = signal.Direction == TradeDirection.Short
            ? entry - (riskPerUnit * parameters.RewardRiskRatio)
            : entry + (riskPerUnit * parameters.RewardRiskRatio);

        if (!parameters.AllowLargeBreakoutStructureStop &&
            stopDistancePercent > parameters.MaxStopDistancePercent)
        {
            return StopTargetResult.Invalid(
                "Price re-entered, but stop distance is too large.",
                stopLoss,
                takeProfit,
                riskPerUnit,
                stopDistancePercent);
        }

        if (range.RangeHigh <= range.RangeLow)
        {
            return StopTargetResult.Invalid("Price re-entered, but the range structure is invalid.", stopLoss, takeProfit);
        }

        return new StopTargetResult(true, string.Empty, stopLoss, takeProfit, riskPerUnit, stopDistancePercent);
    }

    private static decimal CalculateStrength(
        StrategyContext context,
        FourHourRangeReEntryParameters parameters,
        FourHourRangeDto range,
        Candle current,
        TradeDirection direction)
    {
        var strength = 60m;
        var rangePercent = range.RangePercent ?? 0m;

        if (rangePercent >= parameters.MinRangePercent * 2m && rangePercent <= parameters.MaxRangePercent * 0.75m)
        {
            strength += 6m;
        }

        var rangeSize = (range.RangeHigh!.Value - range.RangeLow!.Value);
        if (rangeSize > 0m)
        {
            var closeDepth = direction == TradeDirection.Short
                ? (range.RangeHigh.Value - current.Close) / rangeSize
                : (current.Close - range.RangeLow.Value) / rangeSize;

            if (closeDepth >= 0.25m)
            {
                strength += 6m;
            }
        }

        if (context.IndicatorSnapshot?.VolumeSma20 is decimal volumeSma && current.Volume > volumeSma * 1.1m)
        {
            strength += 4m;
        }

        if (context.MarketRegime == MarketRegime.Reversal)
        {
            strength += 5m;
        }

        if (context.MarketRegime is MarketRegime.Choppy or MarketRegime.HighVolatility)
        {
            strength -= 10m;
        }

        return StrategyStrengthHelper.ResolveStrength(strength, parameters.MinStrength);
    }

    private static StrategySignalResult NoTradeWithDiagnostics(string reason, string rawDataJson) => new()
    {
        SignalType = SignalType.NoTrade,
        Direction = TradeDirection.None,
        Strength = 0m,
        ConfidenceContribution = 0m,
        Reason = reason,
        RawDataJson = rawDataJson
    };

    private static string BuildDiagnostics(
        StrategyContext context,
        FourHourRangeDto range,
        FourHourRangeReEntryParameters parameters,
        FourHourRangeState state,
        string reason,
        string breakoutDirection = "None",
        DateTime? breakoutCandleCloseTimeUtc = null,
        decimal? breakoutExtreme = null,
        DateTime? reentryCandleCloseTimeUtc = null,
        int completedTradesToday = 0,
        decimal? entryPrice = null,
        decimal? stopLoss = null,
        decimal? takeProfit = null,
        decimal? riskPerUnit = null,
        decimal? stopDistancePercent = null,
        int? tradeNumber = null) =>
        JsonSerializer.Serialize(new
        {
            strategyCode = StrategyCode.FourHourRangeReEntry.ToCode(),
            state = state.ToString(),
            reason,
            symbolId = context.SymbolId,
            symbol = context.Symbol,
            timeframe = range.Timeframe,
            marketRegime = context.MarketRegime.ToString(),
            range.NewYorkTradingDate,
            range.RangeStartUtc,
            range.RangeEndUtc,
            range.NewYorkDayEndUtc,
            range.RangeHigh,
            range.RangeLow,
            range.RangePercent,
            range.CandleCountUsed,
            range.ExpectedCandleCount,
            range.RangeReady,
            breakoutDirection,
            breakoutCandleCloseTimeUtc,
            breakoutExtreme,
            reentryCandleCloseTimeUtc,
            completedTradesToday,
            tradeNumber,
            entryPrice,
            stopLoss,
            takeProfit,
            riskPerUnit,
            stopDistancePercent,
            parameters.RewardRiskRatio,
            parameters.MaxTradesPerDay,
            parameters.AllowMultipleTradesPerDay,
            parameters.RequireCloseOutsideRange,
            parameters.RequireCloseBackInsideRange,
            parameters.UseWicksForBreakout,
            parameters.StopLossBufferPercent,
            parameters.StopLossBufferTicks,
            parameters.StopLossBufferAtrMultiplier,
            parameters.MaxStopDistancePercent,
            parameters.MinRangePercent,
            parameters.MaxRangePercent,
            evaluatedAtUtc = context.EvaluatedAtUtc
        }, JsonOptions);

    private readonly record struct ReentrySignal(TradeDirection Direction, decimal BreakoutExtreme);

    private sealed record SessionAnalysis(
        FourHourRangeState State,
        string Reason,
        string BreakoutDirection,
        DateTime? BreakoutCandleCloseTimeUtc,
        decimal? BreakoutExtreme,
        int CompletedTradesToday,
        ReentrySignal? Signal);

    private sealed record StopTargetResult(
        bool Valid,
        string Reason,
        decimal? StopLoss = null,
        decimal? TakeProfit = null,
        decimal? RiskPerUnit = null,
        decimal? StopDistancePercent = null)
    {
        public static StopTargetResult Invalid(
            string reason,
            decimal? stopLoss = null,
            decimal? takeProfit = null,
            decimal? riskPerUnit = null,
            decimal? stopDistancePercent = null) =>
            new(false, reason, stopLoss, takeProfit, riskPerUnit, stopDistancePercent);
    }
}
