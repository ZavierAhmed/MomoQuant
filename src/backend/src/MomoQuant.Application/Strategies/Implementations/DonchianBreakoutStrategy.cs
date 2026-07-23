using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class DonchianBreakoutStrategy : StrategyBase
{
    public override StrategyCode Code => StrategyCode.DonchianBreakout;
    public override string Name => "Donchian Breakout";
    public override string Description => "Range breakout continuation using Donchian channel levels.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Trending, MarketRegime.Breakout];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade("Timeframe is not supported by Donchian Breakout.");
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade($"Market regime '{context.MarketRegime}' is not supported by Donchian Breakout.");
        }

        var candle = context.CurrentCandle;
        var indicators = context.IndicatorSnapshot;
        if (candle is null || indicators?.DonchianHigh20 is null || indicators.DonchianLow20 is null)
        {
            return NoTrade("Insufficient candle or Donchian indicator data.");
        }

        var parameters = context.StrategyParameters;
        var requireVolume = StrategyParameterReader.GetBool(parameters, "RequireVolumeConfirmation", false);
        var volumeMultiplier = StrategyParameterReader.GetDecimal(parameters, "VolumeMultiplier", 1.0m);
        var minBreakoutPercent = StrategyParameterReader.GetDecimal(parameters, "MinBreakoutPercent", 0.05m);
        var minStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m);
        var stopLossMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrMultiplier", 1.5m);
        var takeProfitMultiplier = StrategyParameterReader.GetDecimal(parameters, "TakeProfitAtrMultiplier", 2.5m);

        var donchianHigh = indicators.DonchianHigh20.Value;
        var donchianLow = indicators.DonchianLow20.Value;
        var atr = indicators.Atr14 ?? 0m;
        var volumeSma = indicators.VolumeSma20;

        var breakoutBuffer = minBreakoutPercent / 100m;

        if (candle.Close > donchianHigh * (1m + breakoutBuffer))
        {
            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value * volumeMultiplier)
            {
                return NoTrade("Volume confirmation failed for long Donchian breakout.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 8m, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close - (atr * stopLossMultiplier)) : donchianHigh;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close + (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Long,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Close broke above Donchian high with breakout continuation.",
                JsonSerializer.Serialize(new { donchianHigh, donchianLow }));
        }

        if (candle.Close < donchianLow * (1m - breakoutBuffer))
        {
            if (requireVolume && volumeSma.HasValue && candle.Volume < volumeSma.Value * volumeMultiplier)
            {
                return NoTrade("Volume confirmation failed for short Donchian breakout.");
            }

            var strength = StrategyStrengthHelper.ResolveStrength(minStrength + 8m, minStrength);
            var stopLoss = atr > 0m ? (decimal?)(candle.Close + (atr * stopLossMultiplier)) : donchianLow;
            var takeProfit = atr > 0m ? (decimal?)(candle.Close - (atr * takeProfitMultiplier)) : null;

            return Entry(
                TradeDirection.Short,
                strength,
                strength,
                candle.Close,
                stopLoss,
                takeProfit,
                "Close broke below Donchian low with breakout continuation.",
                JsonSerializer.Serialize(new { donchianHigh, donchianLow }));
        }

        return NoTrade("No Donchian breakout detected.");
    }
}
