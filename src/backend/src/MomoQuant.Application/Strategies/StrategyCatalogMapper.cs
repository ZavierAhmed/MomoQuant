using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public static class StrategyCatalogMapper
{
    public static StrategyDto MapToCatalogDto(
        Strategy strategy,
        StrategyDataRequirementDto? requirement,
        bool parameterDefinitionsAvailable)
    {
        var code = strategy.Code.ToCode();
        return new StrategyDto
        {
            Id = strategy.Id,
            Code = code,
            Name = strategy.Name,
            Description = strategy.Description,
            IsEnabled = strategy.IsEnabled,
            Version = strategy.Version,
            Category = ResolveCategory(strategy.Code),
            IsBuiltIn = true,
            SupportedModes = BuildSupportedModes(requirement),
            PreferredTimeframe = requirement?.PreferredExecutionTimeframe,
            PreferredTimeframes = requirement?.PreferredTimeframes ?? [],
            AllowedTimeframes = requirement?.AllowedExecutionTimeframes ?? [],
            RequiredTimeframes = requirement?.RequiredDataTimeframes ?? [],
            RequiredDataTimeframes = requirement?.RequiredDataTimeframes ?? [],
            RequiredIndicators = requirement?.RequiredIndicators ?? [],
            ParameterDefinitionsAvailable = parameterDefinitionsAvailable,
            SupportsOptimization = requirement?.SupportsOptimization ?? false,
            SupportsValidation = requirement?.SupportsValidation ?? true,
            SupportsLivePaper = requirement?.SupportsLivePaper ?? true,
            SupportsBacktest = requirement?.SupportsBacktest ?? true,
            SupportsBenchmark = requirement?.SupportsBenchmark ?? true,
            SupportsStrategyLab = requirement?.SupportsStrategyLab
                ?? (strategy.Code is StrategyCode.PriceStructureBreakoutRetest
                    or StrategyCode.PriceStructureLiquiditySweepReclaim),
            ResearchStatus = strategy.ResearchStatus.ToString(),
            DeploymentQualificationEligible = strategy.DeploymentQualificationEligible,
            CanonicalValidationExperimentId = strategy.CanonicalValidationExperimentId
        };
    }

    private static string ResolveCategory(StrategyCode code) =>
        code switch
        {
            StrategyCode.BbLiquiditySweepCisd or StrategyCode.BbLiquiditySweepCisdRsiPrimed => "Liquidity",
            StrategyCode.FourHourRangeReEntry => "Range",
            StrategyCode.VolatilityGatedSupertrendMomentum => "Momentum",
            StrategyCode.SupertrendContinuation => "Trend",
            StrategyCode.EmaPullback => "Pullback",
            StrategyCode.VwapMeanReversion => "MeanReversion",
            StrategyCode.LiquiditySweep => "Liquidity",
            StrategyCode.BollingerSqueezeBreakout => "Breakout",
            StrategyCode.DonchianBreakout => "Breakout",
            StrategyCode.RsiDivergenceReversal => "Reversal",
            StrategyCode.MacdMomentumContinuation => "Momentum",
            StrategyCode.AtrVolatilityBreakout => "Breakout",
            StrategyCode.SupportResistanceBreakoutRetest => "Breakout",
            StrategyCode.PriceStructureBreakoutRetest => "Price Action / Market Structure",
            StrategyCode.PriceStructureLiquiditySweepReclaim => "Price Action / Liquidity",
            _ => "General"
        };

    private static IReadOnlyList<string> BuildSupportedModes(StrategyDataRequirementDto? requirement)
    {
        if (requirement is null)
        {
            return ["Backtest", "Benchmark", "HistoricalPaper", "LivePaper"];
        }

        var modes = new List<string>();
        if (requirement.SupportsStrategyLab) modes.Add("StrategyLab");
        if (requirement.SupportsBacktest) modes.Add("Backtest");
        if (requirement.SupportsBenchmark) modes.Add("Benchmark");
        if (requirement.SupportsReplay) modes.Add("Replay");
        if (requirement.SupportsHistoricalPaper) modes.Add("HistoricalPaper");
        if (requirement.SupportsLivePaper) modes.Add("LivePaper");
        return modes;
    }
}
