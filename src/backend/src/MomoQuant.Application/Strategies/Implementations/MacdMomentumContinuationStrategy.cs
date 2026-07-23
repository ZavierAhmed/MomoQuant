using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class MacdMomentumContinuationStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.MacdMomentumContinuation;
    public override string Name => "MACD Momentum Continuation";
    public override string Description => "Momentum continuation with MACD and EMA trend confirmation.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Trending, MarketRegime.Breakout];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by MACD Momentum Continuation.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by MACD Momentum Continuation.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.MacdLine is null || indicators.MacdSignal is null ||
            indicators.MacdHistogram is null || indicators.Ema20 is null || indicators.Ema50 is null)
        {
            return NoTrade("Insufficient candle, MACD, or EMA indicator data.");
        }

        var parameters = context.StrategyParameters;
        var requireEmaTrend = StrategyParameterReader.GetBool(parameters, "RequireEmaTrend", true);
        var requireHistogramExpansion = StrategyParameterReader.GetBool(parameters, "RequireHistogramExpansion", true);
        var minHistogramChange = StrategyParameterReader.GetDecimal(parameters, "MinHistogramChange", 0m);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.5m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.0m);

        var macdLine = indicators.MacdLine.Value;
        var macdSignal = indicators.MacdSignal.Value;
        var histogram = indicators.MacdHistogram.Value;
        var previousHistogram = context.RecentIndicatorSnapshots.Count >= 2
            ? context.RecentIndicatorSnapshots[^2].MacdHistogram
            : null;
        var ema20 = indicators.Ema20.Value;
        var ema50 = indicators.Ema50.Value;
        var atr = indicators.Atr14 ?? 0m;

        var histogramExpandingUp = !requireHistogramExpansion ||
            (previousHistogram.HasValue && histogram - previousHistogram.Value > minHistogramChange);
        var histogramExpandingDown = !requireHistogramExpansion ||
            (previousHistogram.HasValue && previousHistogram.Value - histogram > minHistogramChange);

        if (macdLine > macdSignal &&
            histogramExpandingUp &&
            (!requireEmaTrend || ema20 > ema50) &&
            (candle.Close > ema20 || candle.Close > ema50))
        {
            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + Math.Min(20m, Math.Abs(histogram) * 100m), minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "MACD bullish momentum with EMA trend confirmation.",
                JsonSerializer.Serialize(new { macdLine, macdSignal, histogram, ema20, ema50 }));
        }

        if (macdLine < macdSignal &&
            histogramExpandingDown &&
            (!requireEmaTrend || ema20 < ema50) &&
            (candle.Close < ema20 || candle.Close < ema50))
        {
            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + Math.Min(20m, Math.Abs(histogram) * 100m), minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : null;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "MACD bearish momentum with EMA trend confirmation.",
                JsonSerializer.Serialize(new { macdLine, macdSignal, histogram, ema20, ema50 }));
        }

        return NoTrade("MACD momentum continuation conditions are not met.");
    }
}
