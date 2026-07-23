using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public sealed class CisdDetector
{
    public CisdSignalDto? DetectAfterSweep(
        IReadOnlyList<Candle> candles,
        int sweepIndex,
        TradeDirection sweepDirection,
        int cisdLookbackCandles = 5,
        bool requireCandleCloseBeyondCisdLevel = true,
        decimal displacementAtrMultiplier = 0.3m,
        int maxBarsAfterSweep = 5)
    {
        if (sweepIndex < 0 || sweepIndex >= candles.Count)
        {
            return null;
        }

        var atr = EstimateAtr(candles, sweepIndex, 14);
        var endIndex = Math.Min(candles.Count - 1, sweepIndex + maxBarsAfterSweep);

        if (sweepDirection == TradeDirection.Long)
        {
            var cisdLevel = FindMinorBearishHigh(candles, sweepIndex, cisdLookbackCandles);
            for (var index = sweepIndex + 1; index <= endIndex; index++)
            {
                var candle = candles[index];
                if (candle.Close > cisdLevel && (!requireCandleCloseBeyondCisdLevel || candle.Close > cisdLevel))
                {
                    var displacement = candle.Close - cisdLevel;
                    if (displacement >= atr * displacementAtrMultiplier)
                    {
                        return Confirmed(TradeDirection.Long, cisdLevel, candle, CisdConfirmationType.StructureBreak, 75m);
                    }

                    return Confirmed(TradeDirection.Long, cisdLevel, candle, CisdConfirmationType.BearishCandleHighBreak, 65m);
                }
            }

            return Rejected(TradeDirection.Long, cisdLevel, "NoCisdConfirmation");
        }

        var bullishLow = FindMinorBullishLow(candles, sweepIndex, cisdLookbackCandles);
        for (var index = sweepIndex + 1; index <= endIndex; index++)
        {
            var candle = candles[index];
            if (candle.Close < bullishLow)
            {
                var displacement = bullishLow - candle.Close;
                if (displacement >= atr * displacementAtrMultiplier)
                {
                    return Confirmed(TradeDirection.Short, bullishLow, candle, CisdConfirmationType.StructureBreak, 75m);
                }

                return Confirmed(TradeDirection.Short, bullishLow, candle, CisdConfirmationType.BullishCandleLowBreak, 65m);
            }
        }

        return Rejected(TradeDirection.Short, bullishLow, "NoCisdConfirmation");
    }

    private static decimal FindMinorBearishHigh(IReadOnlyList<Candle> candles, int sweepIndex, int lookback)
    {
        var start = Math.Max(0, sweepIndex - lookback);
        decimal level = candles[sweepIndex].High;
        for (var index = start; index <= sweepIndex; index++)
        {
            if (candles[index].Close < candles[index].Open)
            {
                level = Math.Max(level, candles[index].High);
            }
        }

        return level;
    }

    private static decimal FindMinorBullishLow(IReadOnlyList<Candle> candles, int sweepIndex, int lookback)
    {
        var start = Math.Max(0, sweepIndex - lookback);
        decimal level = candles[sweepIndex].Low;
        for (var index = start; index <= sweepIndex; index++)
        {
            if (candles[index].Close > candles[index].Open)
            {
                level = Math.Min(level, candles[index].Low);
            }
        }

        return level;
    }

    private static decimal EstimateAtr(IReadOnlyList<Candle> candles, int index, int period)
    {
        var start = Math.Max(1, index - period + 1);
        decimal sum = 0m;
        var count = 0;
        for (var i = start; i <= index; i++)
        {
            var current = candles[i];
            var previous = candles[i - 1];
            sum += Math.Max(current.High - current.Low,
                Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close)));
            count++;
        }

        return count == 0 ? 0m : sum / count;
    }

    private static CisdSignalDto Confirmed(
        TradeDirection direction,
        decimal level,
        Candle candle,
        CisdConfirmationType type,
        decimal confidence) => new()
    {
        Direction = direction,
        CisdLevel = level,
        ConfirmedAtUtc = candle.CloseTimeUtc,
        ConfirmedCandleId = candle.Id,
        ConfirmationType = type,
        ConfidenceScore = confidence,
        IsConfirmed = true
    };

    private static CisdSignalDto Rejected(TradeDirection direction, decimal level, string reason) => new()
    {
        Direction = direction,
        CisdLevel = level,
        IsConfirmed = false,
        RejectionReason = reason
    };
}
