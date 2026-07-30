using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies;

/// <summary>Supported production strategy versions for canonical validation training (Milestone 23.1B1C).</summary>
public static class CanonicalStrategyVersionPolicy
{
    public static bool IsSupportedProductionVersion(StrategyCode code, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return code switch
        {
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout =>
                string.Equals(version, MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version, StringComparison.Ordinal),
            StrategyCode.PriceStructureBreakoutRetest =>
                string.Equals(version, PriceStructureBreakoutRetestEvaluator.StrategyVersion, StringComparison.Ordinal)
                || string.Equals(version, PriceStructureBreakoutRetestEvaluator.StrategyVersionV10, StringComparison.Ordinal),
            StrategyCode.MomoVolatilityRangeReversion =>
                string.Equals(version, MomoVolatilityRangeReversionStrategy.Version, StringComparison.Ordinal),
            _ => false
        };
    }
}
