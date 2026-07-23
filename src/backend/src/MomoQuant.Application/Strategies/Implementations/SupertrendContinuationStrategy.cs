using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class SupertrendContinuationStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.SupertrendContinuation;
    public override string Name => "Supertrend Continuation";
    public override string Description => "ATR-based trend following using Supertrend direction and pullbacks.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Trending, MarketRegime.Breakout];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by Supertrend Continuation.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by Supertrend Continuation.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.Supertrend is null || indicators.SupertrendDirection is null)
        {
            return NoTrade("Insufficient candle or Supertrend indicator data.");
        }

        var parameters = context.StrategyParameters;
        var pullbackTolerancePercent = StrategyParameterReader.GetDecimal(parameters, "PullbackTolerancePercent", 0.25m);
        var requireVolume = StrategyParameterReader.GetBool(parameters, "RequireVolumeConfirmation", false);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.5m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.0m);

        var supertrend = indicators.Supertrend.Value;
        var direction = indicators.SupertrendDirection.Value;
        var ema20 = indicators.Ema20;
        var atr = indicators.Atr14 ?? 0m;
        var volumeSma = indicators.VolumeSma20;

        var nearSupertrend = supertrend != 0m &&
            Math.Abs(candle.Close - supertrend) / supertrend <= pullbackTolerancePercent / 100m;
        var nearEma20 = ema20.HasValue && ema20.Value != 0m &&
            Math.Abs(candle.Close - ema20.Value) / ema20.Value <= pullbackTolerancePercent / 100m;
        var nearPullbackLevel = nearSupertrend || nearEma20;

        if (direction > 0)
        {
            if (!nearPullbackLevel)
            {
                return NoTrade("Price is not near Supertrend or EMA20 for long continuation.");
            }

            if (!StrategyCandleHelper.IsBullish(candle))
            {
                return NoTrade("Bullish continuation candle is required.");
            }

            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value)
            {
                return NoTrade("Volume confirmation failed for long Supertrend continuation.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 8m, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : supertrend;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Bullish Supertrend with pullback and upward resumption.",
                JsonSerializer.Serialize(new { supertrend, direction, ema20 }));
        }

        if (direction < 0)
        {
            if (!nearPullbackLevel)
            {
                return NoTrade("Price is not near Supertrend or EMA20 for short continuation.");
            }

            if (!StrategyCandleHelper.IsBearish(candle))
            {
                return NoTrade("Bearish continuation candle is required.");
            }

            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value)
            {
                return NoTrade("Volume confirmation failed for short Supertrend continuation.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 8m, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : supertrend;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Bearish Supertrend with pullback and downward resumption.",
                JsonSerializer.Serialize(new { supertrend, direction, ema20 }));
        }

        return NoTrade("Supertrend direction is neutral.");
    }
}
