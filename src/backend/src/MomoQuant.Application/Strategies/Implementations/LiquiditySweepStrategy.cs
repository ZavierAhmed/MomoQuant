using System.Text.Json;
using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class LiquiditySweepStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.LiquiditySweep;
    public override string Name => "Liquidity Sweep Reclaim";
    public override string Description => "Stop-hunt reversal strategy that looks for liquidity sweeps and reclaims.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Reversal, MarketRegime.Ranging, MarketRegime.HighVolatility];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by Liquidity Sweep Reclaim.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by Liquidity Sweep Reclaim.");
        }

        var candles = context.Candles;
        if (candles.Count < 5)
        {
            return NoTrade("Insufficient recent candles for liquidity sweep detection.");
        }

        var parameters = context.StrategyParameters;
        var swingLookback = StrategyParameterReader.GetInt(parameters, "SwingLookback", 2);
        var sweepLookbackCandles = StrategyParameterReader.GetInt(parameters, "SweepLookbackCandles", 3);
        var minWickPercent = StrategyParameterReader.GetDecimal(parameters, "MinWickPercent", 30m);
        var requireVolumeSpike = StrategyParameterReader.GetBool(parameters, "RequireVolumeSpike", false);
        var volumeSpikeMultiplier = StrategyParameterReader.GetDecimal(parameters, "VolumeSpikeMultiplier", 1.2m);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 50m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.2m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.0m);

        var indicators = context.IndicatorSnapshot;
        var atr = indicators?.Atr14 ?? 0m;
        var volumeSma = indicators?.VolumeSma20;

        var swingLow = FindRecentSwingLow(candles, swingLookback);
        if (swingLow.HasValue)
        {
            var sweepCandle = FindSweepBelow(candles, swingLow.Value, sweepLookbackCandles);
            if (sweepCandle is not null &&
                sweepCandle.Close > swingLow.Value &&
                StrategyCandleHelper.WickPercent(sweepCandle, lowerWick: true) >= minWickPercent &&
                PassesVolumeCheck(sweepCandle, volumeSma, requireVolumeSpike, volumeSpikeMultiplier))
            {
                var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 15m, minStrength);
                var stopLoss = atr > 0m ? (decimal?)(sweepCandle.Close - (atr * stopLossMultiplier)) : sweepCandle.Low;
                var takeProfit = atr > 0m ? (decimal?)(sweepCandle.Close + (atr * takeProfitMultiplier)) : null;

                return Entry(
                    TradeDirection.Long,
                    strength,
                    strength,
                    sweepCandle.Close,
                    stopLoss,
                    takeProfit,
                    "Price swept below prior swing low and reclaimed with a meaningful lower wick.",
                    JsonSerializer.Serialize(new { swingLow = swingLow.Value, sweptLow = sweepCandle.Low, atr }));
            }
        }

        var swingHigh = FindRecentSwingHigh(candles, swingLookback);
        if (swingHigh.HasValue)
        {
            var sweepCandle = FindSweepAbove(candles, swingHigh.Value, sweepLookbackCandles);
            if (sweepCandle is not null &&
                sweepCandle.Close < swingHigh.Value &&
                StrategyCandleHelper.WickPercent(sweepCandle, lowerWick: false) >= minWickPercent &&
                PassesVolumeCheck(sweepCandle, volumeSma, requireVolumeSpike, volumeSpikeMultiplier))
            {
                var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 15m, minStrength);
                var stopLoss = atr > 0m ? (decimal?)(sweepCandle.Close + (atr * stopLossMultiplier)) : sweepCandle.High;
                var takeProfit = atr > 0m ? (decimal?)(sweepCandle.Close - (atr * takeProfitMultiplier)) : null;

                return Entry(
                    TradeDirection.Short,
                    strength,
                    strength,
                    sweepCandle.Close,
                    stopLoss,
                    takeProfit,
                    "Price swept above prior swing high and rejected with a meaningful upper wick.",
                    JsonSerializer.Serialize(new { swingHigh = swingHigh.Value, sweptHigh = sweepCandle.High, atr }));
            }
        }

        return NoTrade("No liquidity sweep setup was detected.");
    }

    private static decimal? FindRecentSwingLow(IReadOnlyList<Candle> candles, int lookback)
    {
        for (var index = candles.Count - lookback - 2; index >= lookback; index--)
        {
            var value = SwingPointCalculator.DetectSwingLow(candles, index, lookback);
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    private static decimal? FindRecentSwingHigh(IReadOnlyList<Candle> candles, int lookback)
    {
        for (var index = candles.Count - lookback - 2; index >= lookback; index--)
        {
            var value = SwingPointCalculator.DetectSwingHigh(candles, index, lookback);
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    private static Candle? FindSweepBelow(IReadOnlyList<Candle> candles, decimal swingLow, int lookback)
    {
        for (var index = candles.Count - 1; index >= Math.Max(0, candles.Count - lookback); index--)
        {
            var candle = candles[index];
            if (candle.Low < swingLow && candle.Close > swingLow)
            {
                return candle;
            }
        }

        return null;
    }

    private static Candle? FindSweepAbove(IReadOnlyList<Candle> candles, decimal swingHigh, int lookback)
    {
        for (var index = candles.Count - 1; index >= Math.Max(0, candles.Count - lookback); index--)
        {
            var candle = candles[index];
            if (candle.High > swingHigh && candle.Close < swingHigh)
            {
                return candle;
            }
        }

        return null;
    }

    private static bool PassesVolumeCheck(
        Candle candle,
        decimal? volumeSma,
        bool requireVolumeSpike,
        decimal volumeSpikeMultiplier)
    {
        if (!requireVolumeSpike || !volumeSma.HasValue)
        {
            return true;
        }

        return candle.Volume >= volumeSma.Value * volumeSpikeMultiplier;
    }
}
