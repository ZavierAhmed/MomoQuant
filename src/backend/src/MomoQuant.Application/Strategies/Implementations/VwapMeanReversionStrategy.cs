using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class VwapMeanReversionStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.VwapMeanReversion;
    public override string Name => "VWAP Mean Reversion";
    public override string Description => "Mean-reversion strategy using VWAP deviation and RSI extremes.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Ranging, MarketRegime.Reversal];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by VWAP Mean Reversion.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by VWAP Mean Reversion.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.Vwap is null || indicators.Rsi14 is null)
        {
            return NoTrade("Insufficient candle, VWAP, or RSI indicator data.");
        }

        var parameters = context.StrategyParameters;
        var deviationPercent = StrategyParameterReader.GetDecimal(parameters, "VwapDeviationPercent", 0.15m);
        var rsiOversold = StrategyParameterReader.GetDecimal(parameters, "RsiOversold", 35m);
        var rsiOverbought = StrategyParameterReader.GetDecimal(parameters, "RsiOverbought", 65m);
        var maxAtrPercent = StrategyParameterReader.GetDecimal(parameters, "MaxAtrPercent", 3.0m);
        var requireWickRejection = StrategyParameterReader.GetBool(parameters, "RequireWickRejection", false);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 50m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.2m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 1.5m);

        var vwap = indicators.Vwap.Value;
        var rsi = indicators.Rsi14.Value;
        var atr = indicators.Atr14 ?? 0m;

        if (candle.Close > 0m && atr > 0m && (atr / candle.Close * 100m) > maxAtrPercent)
        {
            return NoTrade("ATR is too high for mean-reversion entry.");
        }

        var deviation = vwap == 0m ? 0m : ((candle.Close - vwap) / vwap) * 100m;
        var lowerWickPercent = StrategyCandleHelper.WickPercent(candle, lowerWick: true);
        var upperWickPercent = StrategyCandleHelper.WickPercent(candle, lowerWick: false);

        if (deviation <= -deviationPercent && rsi <= rsiOversold)
        {
            var bullishRecovery = StrategyCandleHelper.IsBullish(candle) ||
                (!requireWickRejection || lowerWickPercent >= 35m);
            if (!bullishRecovery)
            {
                return NoTrade("Bullish recovery or lower wick rejection confirmation is missing.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(
                minStrength + Math.Min(30m, Math.Abs(deviation) * 10m),
                minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m
                ? (decimal?)(candle.Close + (atr * takeProfitMultiplier))
                : vwap;

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Price is below VWAP with oversold RSI and bullish recovery.",
                JsonSerializer.Serialize(new { vwap, rsi, deviation, atr }));
        }

        if (deviation >= deviationPercent && rsi >= rsiOverbought)
        {
            var bearishRejection = StrategyCandleHelper.IsBearish(candle) ||
                (!requireWickRejection || upperWickPercent >= 35m);
            if (!bearishRejection)
            {
                return NoTrade("Bearish rejection or upper wick rejection confirmation is missing.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(
                minStrength + Math.Min(30m, Math.Abs(deviation) * 10m),
                minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m
                ? (decimal?)(candle.Close - (atr * takeProfitMultiplier))
                : vwap;

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Price is above VWAP with overbought RSI and bearish rejection.",
                JsonSerializer.Serialize(new { vwap, rsi, deviation, atr }));
        }

        return NoTrade("VWAP deviation and RSI conditions are not met.");
    }
}
