using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public interface IVolatilityGatedSuperTrendRetestTracker
{
    void ResetRun(long tradingSessionId);
    void OnTrendFlip(long tradingSessionId, int direction, int barIndex);
    void RecordRetest(long tradingSessionId, int direction);
    bool HasRetest(long tradingSessionId, int direction);
    int GetBarsSinceFlip(long tradingSessionId, int direction);
    void IncrementBar(long tradingSessionId, int direction);
}

public sealed class VolatilityGatedSuperTrendRetestTracker : IVolatilityGatedSuperTrendRetestTracker
{
    private sealed class SessionState
    {
        public int LongBarsSinceFlip { get; set; }
        public int ShortBarsSinceFlip { get; set; }
        public bool LongRetestSeen { get; set; }
        public bool ShortRetestSeen { get; set; }
    }

    private readonly Dictionary<long, SessionState> _sessions = new();

    public void ResetRun(long tradingSessionId) => _sessions.Remove(tradingSessionId);

    public void OnTrendFlip(long tradingSessionId, int direction, int barIndex)
    {
        var state = GetOrCreate(tradingSessionId);
        if (direction > 0)
        {
            state.LongBarsSinceFlip = 0;
            state.LongRetestSeen = false;
        }
        else if (direction < 0)
        {
            state.ShortBarsSinceFlip = 0;
            state.ShortRetestSeen = false;
        }
    }

    public void RecordRetest(long tradingSessionId, int direction)
    {
        var state = GetOrCreate(tradingSessionId);
        if (direction > 0) state.LongRetestSeen = true;
        else if (direction < 0) state.ShortRetestSeen = true;
    }

    public bool HasRetest(long tradingSessionId, int direction)
    {
        if (!_sessions.TryGetValue(tradingSessionId, out var state)) return false;
        return direction > 0 ? state.LongRetestSeen : state.ShortRetestSeen;
    }

    public int GetBarsSinceFlip(long tradingSessionId, int direction)
    {
        if (!_sessions.TryGetValue(tradingSessionId, out var state)) return 0;
        return direction > 0 ? state.LongBarsSinceFlip : state.ShortBarsSinceFlip;
    }

    public void IncrementBar(long tradingSessionId, int direction)
    {
        var state = GetOrCreate(tradingSessionId);
        if (direction > 0) state.LongBarsSinceFlip++;
        else if (direction < 0) state.ShortBarsSinceFlip++;
    }

    private SessionState GetOrCreate(long tradingSessionId)
    {
        if (!_sessions.TryGetValue(tradingSessionId, out var state))
        {
            state = new SessionState();
            _sessions[tradingSessionId] = state;
        }

        return state;
    }
}

public sealed class VolatilityGatedSuperTrendEvaluator
{
    public VolatilityGatedSuperTrendEvaluationResult Evaluate(
        IReadOnlyList<Candle> candles,
        int index,
        VolatilityGatedSuperTrendParameters parameters,
        IVolatilityGatedSuperTrendContextService contextService,
        IVolatilityGatedSuperTrendRetestTracker retestTracker,
        long symbolId,
        long tradingSessionId)
    {
        var candle = candles[index];
        var data = contextService.GetCandleData(symbolId, index);
        if (data is null || data.SuperTrendLine is null || data.TrendDirection == 0)
        {
            return Reject(candle, data, VolatilityGatedSuperTrendRejectionCodes.NoSuperTrendDirection,
                data is null ? VolatilityGatedSuperTrendRejectionCodes.MissingIndicators : VolatilityGatedSuperTrendRejectionCodes.NoSuperTrendDirection);
        }

        if (data.TrendFlip)
        {
            retestTracker.OnTrendFlip(tradingSessionId, data.TrendDirection, index);
        }

        retestTracker.IncrementBar(tradingSessionId, data.TrendDirection);

        var trendDir = data.TrendDirection > 0 ? "Bullish" : "Bearish";
        var volPassed = data.VolatilityRatio.HasValue && data.VolatilityRatio.Value >= parameters.MinVolatilityRatio;
        var momentumPassed = data.TrendDirection > 0
            ? data.MacdHistogram.HasValue && data.MacdHistogram.Value > parameters.MinHistogramStrength
            : data.MacdHistogram.HasValue && data.MacdHistogram.Value < -parameters.MinHistogramStrength;

        var distance = data.SuperTrendLine.Value != 0m
            ? Math.Abs(candle.Close - data.SuperTrendLine.Value)
            : (decimal?)null;

        var diagnostics = BuildDiagnostics(candle, data, trendDir, volPassed, momentumPassed, distance);

        if (!volPassed)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.VolatilityGateFailed;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.VolatilityGateFailed, Diagnostics = diagnostics };
        }

        if (!momentumPassed)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.MomentumFailed;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.MomentumFailed, Diagnostics = diagnostics };
        }

        var barsSinceFlip = retestTracker.GetBarsSinceFlip(tradingSessionId, data.TrendDirection);
        if (barsSinceFlip > parameters.MaxBarsAfterTrendFlip)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.ExpiredAfterTrendFlip;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.ExpiredAfterTrendFlip, Diagnostics = diagnostics };
        }

        var atr = data.AtrForStops ?? data.FastAtr ?? 0m;
        var tolerance = atr > 0m ? atr * parameters.RetestAtrTolerance : 0m;
        var retestDetected = false;
        var confirmationDetected = false;

        if (data.TrendDirection > 0)
        {
            var nearSt = tolerance > 0m && candle.Low <= data.SuperTrendLine.Value + tolerance;
            if (nearSt) retestTracker.RecordRetest(tradingSessionId, 1);
            retestDetected = retestTracker.HasRetest(tradingSessionId, 1) || nearSt;
            confirmationDetected = candle.Close > data.SuperTrendLine.Value && StrategyCandleHelper.IsBullish(candle);
        }
        else
        {
            var nearSt = tolerance > 0m && candle.High >= data.SuperTrendLine.Value - tolerance;
            if (nearSt) retestTracker.RecordRetest(tradingSessionId, -1);
            retestDetected = retestTracker.HasRetest(tradingSessionId, -1) || nearSt;
            confirmationDetected = candle.Close < data.SuperTrendLine.Value && StrategyCandleHelper.IsBearish(candle);
        }

        diagnostics.RetestDetected = retestDetected;
        diagnostics.ConfirmationDetected = confirmationDetected;

        var canEnterWithoutRetest = !parameters.RequireRetest && parameters.AllowTrendContinuationEntry;
        if (parameters.RequireRetest && !retestDetected)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.NoRetest;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.NoRetest, Diagnostics = diagnostics };
        }

        if (!canEnterWithoutRetest && !confirmationDetected)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.NoConfirmation;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.NoConfirmation, Diagnostics = diagnostics };
        }

        var direction = data.TrendDirection > 0 ? TradeDirection.Long : TradeDirection.Short;
        var stopLoss = CalculateStopLoss(candle, data, parameters, direction);
        if (!stopLoss.HasValue || stopLoss.Value <= 0m)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.InvalidStopDistance;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.InvalidStopDistance, Diagnostics = diagnostics };
        }

        var entry = candle.Close;
        var risk = direction == TradeDirection.Long ? entry - stopLoss.Value : stopLoss.Value - entry;
        if (risk <= 0m)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.InvalidStopDistance;
            diagnostics.FinalDecision = "NoTrade";
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.InvalidStopDistance, Diagnostics = diagnostics };
        }

        var takeProfit = direction == TradeDirection.Long
            ? entry + (risk * parameters.FixedRewardRisk)
            : entry - (risk * parameters.FixedRewardRisk);

        var rewardRisk = parameters.FixedRewardRisk;
        if (rewardRisk < parameters.MinRewardRisk)
        {
            diagnostics.RejectionReason = VolatilityGatedSuperTrendRejectionCodes.TargetLessThanMinimumR;
            diagnostics.FinalDecision = "NoTrade";
            diagnostics.RiskReward = rewardRisk;
            return new VolatilityGatedSuperTrendEvaluationResult { RejectionCode = VolatilityGatedSuperTrendRejectionCodes.TargetLessThanMinimumR, Diagnostics = diagnostics };
        }

        var strength = StrategyStrengthHelper.ResolveStrength(parameters.MinStrength + 10m, parameters.MinStrength);
        var dirLabel = direction == TradeDirection.Long ? "Long" : "Short";
        diagnostics.CandidateDirection = dirLabel;
        diagnostics.RiskReward = rewardRisk;
        diagnostics.FinalDecision = "Entry";
        diagnostics.RejectionReason = null;

        return new VolatilityGatedSuperTrendEvaluationResult
        {
            IsEntry = true,
            Direction = dirLabel,
            EntryPrice = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            Strength = strength,
            Reason = $"Volatility-gated SuperTrend {dirLabel.ToLowerInvariant()} retest confirmation.",
            Diagnostics = diagnostics
        };
    }

    private static decimal? CalculateStopLoss(
        Candle candle,
        VolatilityGatedSuperTrendCandleData data,
        VolatilityGatedSuperTrendParameters parameters,
        TradeDirection direction)
    {
        var atr = data.AtrForStops ?? data.FastAtr ?? 0m;
        var buffer = atr > 0m ? atr * parameters.StopBufferAtrMultiplier : 0m;

        return parameters.StopMode switch
        {
            "RecentSwing" when direction == TradeDirection.Long && data.RecentSwingLow.HasValue =>
                data.RecentSwingLow.Value - buffer,
            "RecentSwing" when direction == TradeDirection.Short && data.RecentSwingHigh.HasValue =>
                data.RecentSwingHigh.Value + buffer,
            "AtrMultiple" when atr > 0m && direction == TradeDirection.Long =>
                candle.Close - (atr * parameters.StopAtrMultiplier),
            "AtrMultiple" when atr > 0m && direction == TradeDirection.Short =>
                candle.Close + (atr * parameters.StopAtrMultiplier),
            _ when data.SuperTrendLine.HasValue && direction == TradeDirection.Long =>
                data.SuperTrendLine.Value - buffer,
            _ when data.SuperTrendLine.HasValue && direction == TradeDirection.Short =>
                data.SuperTrendLine.Value + buffer,
            _ => null
        };
    }

    private static VolatilityGatedSuperTrendDiagnosticsDto BuildDiagnostics(
        Candle candle,
        VolatilityGatedSuperTrendCandleData data,
        string trendDir,
        bool volPassed,
        bool momentumPassed,
        decimal? distance) => new()
    {
        CandleTimeUtc = candle.OpenTimeUtc,
        Close = candle.Close,
        SuperTrendLine = data.SuperTrendLine,
        TrendDirection = trendDir,
        TrendFlip = data.TrendFlip,
        FastAtr = data.FastAtr,
        SlowAtr = data.SlowAtr,
        VolatilityRatio = data.VolatilityRatio,
        VolatilityGatePassed = volPassed,
        MacdLine = data.MacdLine,
        MacdSignal = data.MacdSignal,
        MacdHistogram = data.MacdHistogram,
        MomentumPassed = momentumPassed,
        DistanceFromSuperTrend = distance,
        FinalDecision = "NoTrade"
    };

    private static VolatilityGatedSuperTrendEvaluationResult Reject(
        Candle candle,
        VolatilityGatedSuperTrendCandleData? data,
        string code,
        string rejectionCode) => new()
    {
        RejectionCode = rejectionCode,
        Diagnostics = new VolatilityGatedSuperTrendDiagnosticsDto
        {
            CandleTimeUtc = candle.OpenTimeUtc,
            Close = candle.Close,
            SuperTrendLine = data?.SuperTrendLine,
            TrendDirection = data?.TrendDirection > 0 ? "Bullish" : data?.TrendDirection < 0 ? "Bearish" : "Neutral",
            RejectionReason = code,
            FinalDecision = "NoTrade"
        }
    };
}
