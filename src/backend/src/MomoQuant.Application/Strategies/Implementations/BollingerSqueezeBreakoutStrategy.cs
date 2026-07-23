using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class BollingerSqueezeBreakoutStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.BollingerSqueezeBreakout;
    public override string Name => "Bollinger Squeeze Breakout";
    public override string Description => "Volatility contraction followed by Bollinger band breakout.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Breakout, MarketRegime.Trending, MarketRegime.HighVolatility];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by Bollinger Squeeze Breakout.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by Bollinger Squeeze Breakout.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.BollingerUpper20 is null || indicators.BollingerLower20 is null ||
            indicators.BollingerBandwidth20 is null)
        {
            return NoTrade("Insufficient candle or Bollinger indicator data.");
        }

        var parameters = context.StrategyParameters;
        var squeezeBandwidthPercent = StrategyParameterReader.GetDecimal(parameters, "SqueezeBandwidthPercent", 1.0m);
        var squeezeLookback = StrategyParameterReader.GetInt(parameters, "SqueezeLookback", 20);
        var volumeMultiplier = StrategyParameterReader.GetDecimal(parameters, "VolumeMultiplier", 1.1m);
        var requireVolume = StrategyParameterReader.GetBool(parameters, "RequireVolumeConfirmation", true);
        var maxAtrPercent = StrategyParameterReader.GetDecimal(parameters, "MaxAtrPercent", 4.0m);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.5m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.5m);

        var atr = indicators.Atr14 ?? 0m;
        if (candle.Close > 0m && atr > 0m && (atr / candle.Close * 100m) > maxAtrPercent)
        {
            return NoTrade("ATR is too high for Bollinger squeeze breakout.");
        }

        if (!HadRecentSqueeze(context.RecentIndicatorSnapshots, squeezeLookback, squeezeBandwidthPercent))
        {
            return NoTrade("No recent Bollinger bandwidth squeeze detected.");
        }

        var upper = indicators.BollingerUpper20.Value;
        var lower = indicators.BollingerLower20.Value;
        var volumeSma = indicators.VolumeSma20;

        if (candle.Close > upper)
        {
            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value * volumeMultiplier)
            {
                return NoTrade("Volume confirmation failed for long Bollinger breakout.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 10m, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Bollinger squeeze resolved with upside breakout above upper band.",
                JsonSerializer.Serialize(new { upper, lower, bandwidth = indicators.BollingerBandwidth20 }));
        }

        if (candle.Close < lower)
        {
            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value * volumeMultiplier)
            {
                return NoTrade("Volume confirmation failed for short Bollinger breakout.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 10m, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Bollinger squeeze resolved with downside breakout below lower band.",
                JsonSerializer.Serialize(new { upper, lower, bandwidth = indicators.BollingerBandwidth20 }));
        }

        return NoTrade("No Bollinger squeeze breakout detected.");
    }

    private static bool HadRecentSqueeze(
        IReadOnlyList<IndicatorSnapshot> snapshots,
        int lookback,
        decimal squeezeBandwidthPercent)
    {
        if (snapshots.Count == 0)
        {
            return false;
        }

        var start = Math.Max(0, snapshots.Count - lookback);
        for (var i = start; i < snapshots.Count; i++)
        {
            var bandwidth = snapshots[i].BollingerBandwidth20;
            if (bandwidth.HasValue && bandwidth.Value <= squeezeBandwidthPercent)
            {
                return true;
            }
        }

        return false;
    }
}
