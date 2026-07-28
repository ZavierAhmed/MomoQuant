using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Integration stubs replaced by unit production-path tests in
/// Milestone231A1ProductionPathRejectionUnitTests (real service invocation + mock persistence).
/// Keep this file as a thin smoke that capability policy rejects Validation Lab create codes.
/// </summary>
[Collection("Integration")]
public sealed class Milestone231A1ProductionPathRejectionTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone231A1ProductionPathRejectionTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void CapabilityPolicy_RejectsMtfAndRangeValidation()
    {
        Assert.NotNull(StrategyCapabilityPolicy.RejectValidationReason(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.NotNull(StrategyCapabilityPolicy.RejectValidationReason(StrategyCodes.MomoVolatilityRangeReversion));
        Assert.Null(StrategyCapabilityPolicy.RejectValidationReason(StrategyCodes.PriceStructureBreakoutRetest));
    }

    [Fact]
    public void CapabilityPolicy_RejectsArchivedAndUnsupportedOptimization()
    {
        Assert.Contains("archived", StrategyCapabilityPolicy.RejectOptimizationReason(StrategyCodes.EmaPullback)!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optimization", StrategyCapabilityPolicy.RejectOptimizationReason(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout)!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(StrategyCapabilityPolicy.RejectOptimizationReason(StrategyCodes.PriceStructureBreakoutRetest));
    }
}
