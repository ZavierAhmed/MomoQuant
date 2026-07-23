using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class AtrVolatilityBreakoutStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.AtrVolatilityBreakout;
    public override string Name => "ATR Volatility Breakout";
    public override string Description => "Trades volatility expansion after range compression.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Breakout, MarketRegime.HighVolatility, MarketRegime.Trending];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by ATR Volatility Breakout.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by ATR Volatility Breakout.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.Atr14 is null)
        {
            return NoTrade("Insufficient candle or ATR indicator data.");
        }

        var parameters = context.StrategyParameters;
        var rangeLookback = StrategyParameterReader.GetInt(parameters, "RangeLookback", 20);
        var compressionAtrPercent = StrategyParameterReader.GetDecimal(parameters, "CompressionAtrPercent", 1.0m);
        var breakoutBufferPercent = StrategyParameterReader.GetDecimal(parameters, "BreakoutBufferPercent", 0.05m);
        var maxAtrPercent = StrategyParameterReader.GetDecimal(parameters, "MaxAtrPercent", 4.0m);
        var requireVolume = StrategyParameterReader.GetBool(parameters, "RequireVolumeConfirmation", false);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.5m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.5m);

        var atr = indicators.Atr14.Value;
        if (candle.Close > 0m && (atr / candle.Close * 100m) > maxAtrPercent)
        {
            return NoTrade("ATR is too extreme for volatility breakout entry.");
        }

        if (!HadRecentCompression(context.RecentIndicatorSnapshots, rangeLookback, compressionAtrPercent))
        {
            return NoTrade("No recent ATR compression detected.");
        }

        var rangeHigh = StrategyCandleHelper.RangeHigh(context.Candles, rangeLookback);
        var rangeLow = StrategyCandleHelper.RangeLow(context.Candles, rangeLookback);
        if (!rangeHigh.HasValue || !rangeLow.HasValue)
        {
            return NoTrade("Insufficient range data for volatility breakout.");
        }

        var buffer = breakoutBufferPercent / 100m;
        var volumeSma = indicators.VolumeSma20;
        var breakoutHigh = rangeHigh.Value * (1m + buffer);
        var breakoutLow = rangeLow.Value * (1m - buffer);

        if (candle.Close > breakoutHigh)
        {
            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value)
            {
                return NoTrade("Volume confirmation failed for long volatility breakout.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 10m, minStrength);
            var stopLoss = (decimal?)(candle.Close - (atr * stopLossMultiplier));
            var takeProfit = (decimal?)(candle.Close + (atr * takeProfitMultiplier));

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Price broke above compressed range with rising volatility.",
                JsonSerializer.Serialize(new { rangeHigh, rangeLow, atr }));
        }

        if (candle.Close < breakoutLow)
        {
            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value)
            {
                return NoTrade("Volume confirmation failed for short volatility breakout.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 10m, minStrength);
            var stopLoss = (decimal?)(candle.Close + (atr * stopLossMultiplier));
            var takeProfit = (decimal?)(candle.Close - (atr * takeProfitMultiplier));

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Price broke below compressed range with rising volatility.",
                JsonSerializer.Serialize(new { rangeHigh, rangeLow, atr }));
        }

        return NoTrade("No ATR volatility breakout detected.");
    }

    private static bool HadRecentCompression(
        IReadOnlyList<IndicatorSnapshot> snapshots,
        int lookback,
        decimal compressionAtrPercent)
    {
        if (snapshots.Count < 2)
        {
            return false;
        }

        var start = Math.Max(0, snapshots.Count - lookback - 1);
        for (var i = start; i < snapshots.Count - 1; i++)
        {
            var atr = snapshots[i].Atr14;
            var close = snapshots[i].Ema20 ?? snapshots[i].BollingerMiddle20;
            if (!atr.HasValue || !close.HasValue || close.Value <= 0m)
            {
                continue;
            }

            if (atr.Value / close.Value * 100m <= compressionAtrPercent)
            {
                return true;
            }
        }

        return false;
    }
}
