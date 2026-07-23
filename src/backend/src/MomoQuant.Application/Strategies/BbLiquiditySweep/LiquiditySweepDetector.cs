using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public sealed class LiquiditySweepDetector
{
    public LiquiditySweepSignalDto DetectLongSweep(
        Candle candle,
        LiquidityLevelDto liquidityLevel,
        BollingerBandsValueDto bollinger,
        bool requireSweepOutsideBb = true,
        bool requireCloseBackInsideBb = true,
        bool requireCloseBackAcrossLiquidityLine = true)
    {
        var swept = candle.Low < liquidityLevel.Price;
        var closedBackAcross = candle.Close > liquidityLevel.Price;
        var sweptOutsideBb = candle.Low < bollinger.Lower;
        var closedBackInsideBb = candle.Close > bollinger.Lower;

        var isValid = swept
            && (!requireCloseBackAcrossLiquidityLine || closedBackAcross)
            && (!requireSweepOutsideBb || sweptOutsideBb)
            && (!requireCloseBackInsideBb || closedBackInsideBb || closedBackAcross);

        string? rejection = null;
        if (!swept)
        {
            rejection = "NoLiquiditySweep";
        }
        else if (requireCloseBackAcrossLiquidityLine && !closedBackAcross)
        {
            rejection = "DidNotCloseBackInside";
        }
        else if (requireSweepOutsideBb && !sweptOutsideBb)
        {
            rejection = "NoBbSweep";
        }
        else if (requireCloseBackInsideBb && !closedBackInsideBb && !closedBackAcross)
        {
            rejection = "DidNotCloseBackInside";
        }

        return new LiquiditySweepSignalDto
        {
            Direction = TradeDirection.Long,
            SweptLiquidityLevelId = liquidityLevel.Id,
            SweptLiquidityPrice = liquidityLevel.Price,
            CandleTimeUtc = candle.CloseTimeUtc,
            CandleHigh = candle.High,
            CandleLow = candle.Low,
            CandleClose = candle.Close,
            BbUpper = bollinger.Upper,
            BbLower = bollinger.Lower,
            SweptOutsideBb = sweptOutsideBb,
            ClosedBackInsideBb = closedBackInsideBb,
            ClosedBackAcrossLiquidityLine = closedBackAcross,
            IsValidSweep = isValid,
            RejectionReason = rejection
        };
    }

    public LiquiditySweepSignalDto DetectShortSweep(
        Candle candle,
        LiquidityLevelDto liquidityLevel,
        BollingerBandsValueDto bollinger,
        bool requireSweepOutsideBb = true,
        bool requireCloseBackInsideBb = true,
        bool requireCloseBackAcrossLiquidityLine = true)
    {
        var swept = candle.High > liquidityLevel.Price;
        var closedBackAcross = candle.Close < liquidityLevel.Price;
        var sweptOutsideBb = candle.High > bollinger.Upper;
        var closedBackInsideBb = candle.Close < bollinger.Upper;

        var isValid = swept
            && (!requireCloseBackAcrossLiquidityLine || closedBackAcross)
            && (!requireSweepOutsideBb || sweptOutsideBb)
            && (!requireCloseBackInsideBb || closedBackInsideBb || closedBackAcross);

        string? rejection = null;
        if (!swept)
        {
            rejection = "NoLiquiditySweep";
        }
        else if (requireCloseBackAcrossLiquidityLine && !closedBackAcross)
        {
            rejection = "DidNotCloseBackInside";
        }
        else if (requireSweepOutsideBb && !sweptOutsideBb)
        {
            rejection = "NoBbSweep";
        }
        else if (requireCloseBackInsideBb && !closedBackInsideBb && !closedBackAcross)
        {
            rejection = "DidNotCloseBackInside";
        }

        return new LiquiditySweepSignalDto
        {
            Direction = TradeDirection.Short,
            SweptLiquidityLevelId = liquidityLevel.Id,
            SweptLiquidityPrice = liquidityLevel.Price,
            CandleTimeUtc = candle.CloseTimeUtc,
            CandleHigh = candle.High,
            CandleLow = candle.Low,
            CandleClose = candle.Close,
            BbUpper = bollinger.Upper,
            BbLower = bollinger.Lower,
            SweptOutsideBb = sweptOutsideBb,
            ClosedBackInsideBb = closedBackInsideBb,
            ClosedBackAcrossLiquidityLine = closedBackAcross,
            IsValidSweep = isValid,
            RejectionReason = rejection
        };
    }
}
