using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1 — Catalog Content and Capability Policy Tests.
/// </summary>
public sealed class Milestone231A1CatalogAndCapabilityTests
{
    [Fact]
    public void CatalogContent_Adaptive_Mentions25R()
    {
        var content = GetAdaptiveCatalogContent();
        
        Assert.Contains("2.5R", content);
        Assert.DoesNotContain("2R", content.Replace("2.5R", ""));
    }

    [Fact]
    public void CatalogContent_Adaptive_RealRejectionCodes()
    {
        var content = GetAdaptiveCatalogContent();
        
        Assert.Contains("MtfDataUnavailable", content);
        Assert.Contains("VolatilityTooLow", content);
        Assert.Contains("BreakoutBufferNotMet", content);
        Assert.DoesNotContain("HtfStructure", content);
    }

    [Fact]
    public void CatalogContent_Range_MentionsMidpoint()
    {
        var content = GetRangeCatalogContent();
        
        Assert.Contains("midpoint", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RangeMidpoint", content);
    }

    [Fact]
    public void CatalogContent_Range_NoFixed2R()
    {
        var content = GetRangeCatalogContent();
        
        Assert.DoesNotContain("Fixed 2R", content);
    }

    [Fact]
    public void CatalogContent_Psbr_ApproximationNotesContainsV110()
    {
        var content = GetPsbrCatalogContent();
        
        Assert.Contains("v1.1.0", content);
    }

    [Fact]
    public void StrategyCapabilityPolicy_Psbr_SupportsOptimization()
    {
        var supports = StrategyCapabilityPolicy.SupportsOptimization(StrategyCode.PriceStructureBreakoutRetest);
        
        Assert.True(supports);
    }

    [Fact]
    public void StrategyCapabilityPolicy_Psbr_SupportsValidation()
    {
        var supports = StrategyCapabilityPolicy.SupportsValidation(StrategyCode.PriceStructureBreakoutRetest);
        
        Assert.True(supports);
    }

    [Fact]
    public void StrategyCapabilityPolicy_Adaptive_DoesNotSupportOptimization()
    {
        var supports = StrategyCapabilityPolicy.SupportsOptimization(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        
        Assert.False(supports);
    }

    [Fact]
    public void StrategyCapabilityPolicy_Adaptive_DoesNotSupportValidation()
    {
        var supports = StrategyCapabilityPolicy.SupportsValidation(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        
        Assert.False(supports);
    }

    [Fact]
    public void StrategyCapabilityPolicy_Range_DoesNotSupportOptimization()
    {
        var supports = StrategyCapabilityPolicy.SupportsOptimization(StrategyCode.MomoVolatilityRangeReversion);
        
        Assert.False(supports);
    }

    [Fact]
    public void StrategyCapabilityPolicy_Range_DoesNotSupportValidation()
    {
        var supports = StrategyCapabilityPolicy.SupportsValidation(StrategyCode.MomoVolatilityRangeReversion);
        
        Assert.False(supports);
    }

    [Fact]
    public void StrategyCapabilityPolicy_ArchivedStrategy_Rejected()
    {
        var supports = StrategyCapabilityPolicy.SupportsOptimization(StrategyCode.PriceStructureLiquiditySweepReclaim);
        
        Assert.False(supports);
    }

    [Fact]
    public void CatalogDefaults_Adaptive_MatchGetDefaultParameterContract()
    {
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var parameters = MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(contract);
        
        Assert.Equal(50, parameters.HtfFastEmaPeriod);
        Assert.Equal(200, parameters.HtfSlowEmaPeriod);
        Assert.Equal(20, parameters.BreakoutLookback);
        Assert.Equal(2.50m, parameters.FixedRewardRisk);
        Assert.Equal(70m, parameters.MinStrength);
    }

    [Fact]
    public void CatalogDefaults_Range_MatchGetDefaultParameterContract()
    {
        var contract = MomoVolatilityRangeReversionParameters.GetDefaultParameterContract();
        var parameters = MomoVolatilityRangeReversionParameters.Read(contract);
        
        Assert.Equal(48, parameters.RangeLookback);
        Assert.Equal(3.0m, parameters.MinRangeWidthAtr);
        Assert.Equal(12.0m, parameters.MaxRangeWidthAtr);
        Assert.Equal("RangeMidpoint", parameters.TargetMode);
        Assert.Equal(65m, parameters.MinStrength);
    }

    private static string GetAdaptiveCatalogContent()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var catalog = StrategyCatalogContentProvider.BuildDetail(
            new MomoQuant.Domain.Strategies.Strategy
            {
                Id = 1,
                Code = strategy.Code,
                Name = strategy.Name,
                Description = strategy.Description,
                Version = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            null,
            new List<MomoQuant.Application.Strategies.Optimization.StrategyParameterDefinitionDto>(),
            strategy);
        
        return $"{catalog.HowItWorks} {catalog.EntryLogic} {catalog.ExitLogic} {catalog.NoTradeConditions} {catalog.RiskManagement} {catalog.ApproximationNotes}";
    }

    private static string GetRangeCatalogContent()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var catalog = StrategyCatalogContentProvider.BuildDetail(
            new MomoQuant.Domain.Strategies.Strategy
            {
                Id = 2,
                Code = strategy.Code,
                Name = strategy.Name,
                Description = strategy.Description,
                Version = MomoVolatilityRangeReversionStrategy.Version,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            null,
            new List<MomoQuant.Application.Strategies.Optimization.StrategyParameterDefinitionDto>(),
            strategy);
        
        return $"{catalog.HowItWorks} {catalog.EntryLogic} {catalog.ExitLogic} {catalog.NoTradeConditions} {catalog.RiskManagement} {catalog.ApproximationNotes}";
    }

    private static string GetPsbrCatalogContent()
    {
        var strategy = new PriceStructureBreakoutRetestStrategy();
        var catalog = StrategyCatalogContentProvider.BuildDetail(
            new MomoQuant.Domain.Strategies.Strategy
            {
                Id = 3,
                Code = strategy.Code,
                Name = strategy.Name,
                Description = strategy.Description,
                Version = PriceStructureBreakoutRetestStrategy.Version,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            null,
            new List<MomoQuant.Application.Strategies.Optimization.StrategyParameterDefinitionDto>(),
            strategy);
        
        return $"{catalog.HowItWorks} {catalog.EntryLogic} {catalog.ExitLogic} {catalog.NoTradeConditions} {catalog.RiskManagement} {catalog.ApproximationNotes}";
    }
}
