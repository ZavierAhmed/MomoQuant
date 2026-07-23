using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class SupportResistanceBreakoutRetestStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.SupportResistanceBreakoutRetest;
    public override string Name => "Support/Resistance Breakout Retest";
    public override string Description => "Breakout and retest confirmation at support or resistance levels.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Breakout, MarketRegime.Trending, MarketRegime.Ranging];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by Support/Resistance Breakout Retest.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by Support/Resistance Breakout Retest.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null)
        {
            return NoTrade("Insufficient candle data.");
        }

        var parameters = context.StrategyParameters;
        var retestLookbackCandles = StrategyParameterReader.GetInt(parameters, "RetestLookbackCandles", 10);
        var retestTolerancePercent = StrategyParameterReader.GetDecimal(parameters, "RetestTolerancePercent", 0.15m);
        var requireVolumeOnBreakout = StrategyParameterReader.GetBool(parameters, "RequireVolumeOnBreakout", false);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.3m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.0m);

        var resistance = indicators?.ResistanceLevel;
        var support = indicators?.SupportLevel;
        var atr = indicators?.Atr14 ?? 0m;
        var volumeSma = indicators?.VolumeSma20;
        var candles = context.Candles;

        if (resistance.HasValue && TryFindLongRetest(candles, resistance.Value, retestLookbackCandles, retestTolerancePercent,
                requireVolumeOnBreakout, volumeSma, out var breakoutIndex))
        {
            if (candle.Close > resistance.Value && StrategyCandleHelper.IsBullish(candle))
            {
                var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 10m, minStrength);
                var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : resistance;
                var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

                return Entry(
                    TradeDirection.Long,
                    strength,
                    strength,
                    candle.Close,
                    stopLoss,
                    takeProfit,
                    "Resistance breakout retest held with upward continuation.",
                    JsonSerializer.Serialize(new { resistance, breakoutIndex }));
            }
        }

        if (support.HasValue && TryFindShortRetest(candles, support.Value, retestLookbackCandles, retestTolerancePercent,
                requireVolumeOnBreakout, volumeSma, out breakoutIndex))
        {
            if (candle.Close < support.Value && StrategyCandleHelper.IsBearish(candle))
            {
                var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 10m, minStrength);
                var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : support;
                var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

                return Entry(
                    TradeDirection.Short,
                    strength,
                    strength,
                    candle.Close,
                    stopLoss,
                    takeProfit,
                    "Support breakdown retest rejected with downward continuation.",
                    JsonSerializer.Serialize(new { support, breakoutIndex }));
            }
        }

        if (!resistance.HasValue && !support.HasValue)
        {
            return NoTrade("Support and resistance levels are missing.");
        }

        return NoTrade("No support/resistance breakout retest setup detected.");
    }

    private static bool TryFindLongRetest(
        IReadOnlyList<Candle> candles,
        decimal resistance,
        int lookback,
        decimal tolerancePercent,
        bool requireVolume,
        decimal? volumeSma,
        out int breakoutIndex)
    {
        breakoutIndex = -1;
        var tolerance = resistance * tolerancePercent / 100m;
        var start = Math.Max(0, candles.Count - lookback - 1);

        for (var i = start; i < candles.Count - 2; i++)
        {
            var breakout = candles[i];
            if (breakout.Close <= resistance)
            {
                continue;
            }

            if (requireVolume && volumeSma.HasValue && breakout.Volume < volumeSma.Value)
            {
                continue;
            }

            var retested = false;
            for (var j = i + 1; j < candles.Count - 1; j++)
            {
                if (candles[j].Low <= resistance + tolerance && candles[j].Close >= resistance - tolerance)
                {
                    retested = true;
                    breakoutIndex = i;
                    break;
                }
            }

            if (retested)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindShortRetest(
        IReadOnlyList<Candle> candles,
        decimal support,
        int lookback,
        decimal tolerancePercent,
        bool requireVolume,
        decimal? volumeSma,
        out int breakoutIndex)
    {
        breakoutIndex = -1;
        var tolerance = support * tolerancePercent / 100m;
        var start = Math.Max(0, candles.Count - lookback - 1);

        for (var i = start; i < candles.Count - 2; i++)
        {
            var breakout = candles[i];
            if (breakout.Close >= support)
            {
                continue;
            }

            if (requireVolume && volumeSma.HasValue && breakout.Volume < volumeSma.Value)
            {
                continue;
            }

            var retested = false;
            for (var j = i + 1; j < candles.Count - 1; j++)
            {
                if (candles[j].High >= support - tolerance && candles[j].Close <= support + tolerance)
                {
                    retested = true;
                    breakoutIndex = i;
                    break;
                }
            }

            if (retested)
            {
                return true;
            }
        }

        return false;
    }
}
