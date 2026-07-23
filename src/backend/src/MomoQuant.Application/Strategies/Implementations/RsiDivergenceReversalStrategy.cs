using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class RsiDivergenceReversalStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.RsiDivergenceReversal;
    public override string Name => "RSI Divergence Reversal";
    public override string Description => "Momentum divergence reversal using price and RSI swing comparisons.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Reversal, MarketRegime.Ranging];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by RSI Divergence Reversal.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by RSI Divergence Reversal.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.Rsi14 is null)
        {
            return NoTrade("Insufficient candle or RSI indicator data.");
        }

        var candles = context.Candles;
        var snapshots = context.RecentIndicatorSnapshots;
        if (candles.Count < 5 || snapshots.Count < 5)
        {
            return NoTrade("Insufficient history for RSI divergence detection.");
        }

        var parameters = context.StrategyParameters;
        var divergenceLookback = StrategyParameterReader.GetInt(parameters, "DivergenceLookback", 20);
        var confirmationCandles = StrategyParameterReader.GetInt(parameters, "ConfirmationCandles", 3);
        var rsiOversoldZone = StrategyParameterReader.GetDecimal(parameters, "RsiOversoldZone", 40m);
        var rsiOverboughtZone = StrategyParameterReader.GetDecimal(parameters, "RsiOverboughtZone", 60m);
        var requireConfirmationClose = StrategyParameterReader.GetBool(parameters, "RequireConfirmationClose", true);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.3m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.0m);
        var atr = indicators.Atr14 ?? 0m;

        var lookback = Math.Min(divergenceLookback, Math.Min(candles.Count, snapshots.Count));
        var priceLows = FindSwingLows(candles, lookback);
        var priceHighs = FindSwingHighs(candles, lookback);
        var rsiValues = snapshots.TakeLast(lookback).Select(snapshot => snapshot.Rsi14).ToList();

        if (priceLows.Count >= 2)
        {
            var first = priceLows[^2];
            var second = priceLows[^1];
            var firstRsi = rsiValues.ElementAtOrDefault(first.Index);
            var secondRsi = rsiValues.ElementAtOrDefault(second.Index);
            if (second.Price < first.Price && firstRsi.HasValue && secondRsi.HasValue &&
                secondRsi.Value > firstRsi.Value && indicators.Rsi14.Value <= rsiOversoldZone)
            {
                var shortTermLevel = candles.TakeLast(confirmationCandles).Max(item => item.High);
                if (!requireConfirmationClose || candle.Close > shortTermLevel)
                {
                    var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 12m, minStrength);
                    var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : null;
                    var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

                    return Entry(
                        TradeDirection.Long,
                        strength,
                        strength,
                        candle.Close,
                        stopLoss,
                        takeProfit,
                        "Bullish RSI divergence with reclaim above short-term level.",
                        JsonSerializer.Serialize(new { firstLow = first.Price, secondLow = second.Price, rsi = indicators.Rsi14 }));
                }
            }
        }

        if (priceHighs.Count >= 2)
        {
            var first = priceHighs[^2];
            var second = priceHighs[^1];
            var firstRsi = rsiValues.ElementAtOrDefault(first.Index);
            var secondRsi = rsiValues.ElementAtOrDefault(second.Index);
            if (second.Price > first.Price && firstRsi.HasValue && secondRsi.HasValue &&
                secondRsi.Value < firstRsi.Value && indicators.Rsi14.Value >= rsiOverboughtZone)
            {
                var shortTermLevel = candles.TakeLast(confirmationCandles).Min(item => item.Low);
                if (!requireConfirmationClose || candle.Close < shortTermLevel)
                {
                    var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 12m, minStrength);
                    var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : null;
                    var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

                    return Entry(
                        TradeDirection.Short,
                        strength,
                        strength,
                        candle.Close,
                        stopLoss,
                        takeProfit,
                        "Bearish RSI divergence with rejection below short-term level.",
                        JsonSerializer.Serialize(new { firstHigh = first.Price, secondHigh = second.Price, rsi = indicators.Rsi14 }));
                }
            }
        }

        return NoTrade("No RSI divergence reversal setup detected.");
    }

    private static List<(int Index, decimal Price)> FindSwingLows(IReadOnlyList<Candle> candles, int lookback)
    {
        var swings = new List<(int Index, decimal Price)>();
        var start = Math.Max(1, candles.Count - lookback);
        for (var i = start; i < candles.Count - 1; i++)
        {
            if (candles[i].Low <= candles[i - 1].Low && candles[i].Low <= candles[i + 1].Low)
            {
                swings.Add((i, candles[i].Low));
            }
        }

        return swings;
    }

    private static List<(int Index, decimal Price)> FindSwingHighs(IReadOnlyList<Candle> candles, int lookback)
    {
        var swings = new List<(int Index, decimal Price)>();
        var start = Math.Max(1, candles.Count - lookback);
        for (var i = start; i < candles.Count - 1; i++)
        {
            if (candles[i].High >= candles[i - 1].High && candles[i].High >= candles[i + 1].High)
            {
                swings.Add((i, candles[i].High));
            }
        }

        return swings;
    }
}
