using System.Text.Json;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class EmaPullbackStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.EmaPullback;
    public override string Name => "EMA Pullback";
    public override string Description => "Trend-continuation strategy using EMA alignment and pullback entries.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Trending, MarketRegime.Breakout];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by EMA Pullback.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by EMA Pullback.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.Ema20 is null || indicators.Ema50 is null)
        {
            return NoTrade("Insufficient candle or EMA indicator data.");
        }

        var parameters = context.StrategyParameters;
        var pullbackTolerancePercent = StrategyParameterReader.GetDecimal(parameters, "PullbackTolerancePercent", 0.25m);
        var requireEma200 = StrategyParameterReader.GetBool(parameters, "RequireEma200", false);
        var requireVolume = StrategyParameterReader.GetBool(parameters, "RequireVolumeConfirmation", false);
        var requireCandleConfirmation = StrategyParameterReader.GetBool(parameters, "RequireCandleConfirmation", true);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 50m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.5m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.0m);

        var ema20 = indicators.Ema20.Value;
        var ema50 = indicators.Ema50.Value;
        var ema200 = indicators.Ema200;
        var atr = indicators.Atr14 ?? 0m;
        var volumeSma = indicators.VolumeSma20;

        if (requireEma200 && ema200 is null)
        {
            return NoTrade("EMA200 is required but missing.");
        }

        var nearEma20 = ema20 != 0m &&
            Math.Abs(candle.Close - ema20) / ema20 <= pullbackTolerancePercent / 100m;

        if (ema20 > ema50 && (!requireEma200 || ema50 > ema200!.Value))
        {
            if (!nearEma20)
            {
                return NoTrade("Price is not near EMA20 for a long pullback setup.");
            }

            if (candle.Close <= ema50)
            {
                return NoTrade("Close is not above EMA50 for a long setup.");
            }

            if (requireCandleConfirmation && !StrategyCandleHelper.IsBullish(candle))
            {
                return NoTrade("Bullish candle confirmation is required.");
            }

            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value)
            {
                return NoTrade("Volume confirmation failed for long setup.");
            }

            var strength = CalculateStrength(candle.Close, ema20, ema50, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Bullish EMA alignment with pullback to EMA20 and bullish confirmation.",
                JsonSerializer.Serialize(new { ema20, ema50, ema200, atr }));
        }

        if (ema20 < ema50 && (!requireEma200 || ema50 < ema200!.Value))
        {
            if (!nearEma20)
            {
                return NoTrade("Price is not near EMA20 for a short pullback setup.");
            }

            if (candle.Close >= ema50)
            {
                return NoTrade("Close is not below EMA50 for a short setup.");
            }

            if (requireCandleConfirmation && !StrategyCandleHelper.IsBearish(candle))
            {
                return NoTrade("Bearish candle confirmation is required.");
            }

            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value)
            {
                return NoTrade("Volume confirmation failed for short setup.");
            }

            var strength = CalculateStrength(ema50, candle.Close, ema20, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Bearish EMA alignment with pullback to EMA20 and bearish confirmation.",
                JsonSerializer.Serialize(new { ema20, ema50, ema200, atr }));
        }

        return NoTrade("EMA alignment is invalid for EMA Pullback.");
    }

    private static decimal CalculateStrength(decimal favorable, decimal neutral, decimal opposite, decimal minStrength)
    {
        var spread = Math.Abs(favorable - opposite);
        var distance = Math.Abs(favorable - neutral);
        var raw = spread == 0m ? minStrength : Math.Min(100m, distance / spread * 100m);
        return StrategyStrengthHelper.ResolveStrength(raw, minStrength);
    }
}
