using MomoQuant.Domain.Constants;

namespace MomoQuant.Domain.Enums;

public static class StrategyCodeExtensions
{
    public static string ToCode(this StrategyCode code) => code switch
    {
        StrategyCode.LiquiditySweep => StrategyCodes.LiquiditySweepReclaim,
        StrategyCode.VwapMeanReversion => StrategyCodes.VwapMeanReversion,
        StrategyCode.EmaPullback => StrategyCodes.EmaPullback,
        StrategyCode.BollingerSqueezeBreakout => StrategyCodes.BollingerSqueezeBreakout,
        StrategyCode.DonchianBreakout => StrategyCodes.DonchianBreakout,
        StrategyCode.RsiDivergenceReversal => StrategyCodes.RsiDivergenceReversal,
        StrategyCode.MacdMomentumContinuation => StrategyCodes.MacdMomentumContinuation,
        StrategyCode.AtrVolatilityBreakout => StrategyCodes.AtrVolatilityBreakout,
        StrategyCode.SupportResistanceBreakoutRetest => StrategyCodes.SupportResistanceBreakoutRetest,
        StrategyCode.SupertrendContinuation => StrategyCodes.SupertrendContinuation,
        StrategyCode.FourHourRangeReEntry => StrategyCodes.FourHourRangeReEntry,
        StrategyCode.BbLiquiditySweepCisd => StrategyCodes.BbLiquiditySweepCisd,
        StrategyCode.BbLiquiditySweepCisdRsiPrimed => StrategyCodes.BbLiquiditySweepCisdRsiPrimed,
        StrategyCode.VolatilityGatedSupertrendMomentum => StrategyCodes.VolatilityGatedSupertrendMomentum,
        StrategyCode.PriceStructureBreakoutRetest => StrategyCodes.PriceStructureBreakoutRetest,
        StrategyCode.PriceStructureLiquiditySweepReclaim => StrategyCodes.PriceStructureLiquiditySweepReclaim,
        _ => code.ToString()
    };

    public static StrategyCode FromCode(string code) => code switch
    {
        StrategyCodes.LiquiditySweep or StrategyCodes.LiquiditySweepReclaim => StrategyCode.LiquiditySweep,
        StrategyCodes.VwapMeanReversion => StrategyCode.VwapMeanReversion,
        StrategyCodes.EmaPullback => StrategyCode.EmaPullback,
        StrategyCodes.BollingerSqueezeBreakout => StrategyCode.BollingerSqueezeBreakout,
        StrategyCodes.DonchianBreakout => StrategyCode.DonchianBreakout,
        StrategyCodes.RsiDivergenceReversal => StrategyCode.RsiDivergenceReversal,
        StrategyCodes.MacdMomentumContinuation => StrategyCode.MacdMomentumContinuation,
        StrategyCodes.AtrVolatilityBreakout => StrategyCode.AtrVolatilityBreakout,
        StrategyCodes.SupportResistanceBreakoutRetest => StrategyCode.SupportResistanceBreakoutRetest,
        StrategyCodes.SupertrendContinuation => StrategyCode.SupertrendContinuation,
        StrategyCodes.FourHourRangeReEntry => StrategyCode.FourHourRangeReEntry,
        StrategyCodes.BbLiquiditySweepCisd => StrategyCode.BbLiquiditySweepCisd,
        StrategyCodes.BbLiquiditySweepCisdRsiPrimed => StrategyCode.BbLiquiditySweepCisdRsiPrimed,
        StrategyCodes.VolatilityGatedSupertrendMomentum => StrategyCode.VolatilityGatedSupertrendMomentum,
        StrategyCodes.PriceStructureBreakoutRetest => StrategyCode.PriceStructureBreakoutRetest,
        StrategyCodes.PriceStructureLiquiditySweepReclaim => StrategyCode.PriceStructureLiquiditySweepReclaim,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown strategy code.")
    };
}
