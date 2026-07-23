namespace MomoQuant.Domain.Enums;

public enum StrategyCode
{
    LiquiditySweep = 1,
    VwapMeanReversion = 2,
    EmaPullback = 3,
    BollingerSqueezeBreakout = 4,
    DonchianBreakout = 5,
    RsiDivergenceReversal = 6,
    MacdMomentumContinuation = 7,
    AtrVolatilityBreakout = 8,
    SupportResistanceBreakoutRetest = 9,
    SupertrendContinuation = 10,
    FourHourRangeReEntry = 11,
    BbLiquiditySweepCisd = 12,
    BbLiquiditySweepCisdRsiPrimed = 13,
    VolatilityGatedSupertrendMomentum = 14,
    PriceStructureBreakoutRetest = 15,
    PriceStructureLiquiditySweepReclaim = 16
}
