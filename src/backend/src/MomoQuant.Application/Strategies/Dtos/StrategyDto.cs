namespace MomoQuant.Application.Strategies.Dtos;

using MomoQuant.Application.Strategies.Optimization;

public class StrategyDto
{
    public required long Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required bool IsEnabled { get; init; }
    public required string Version { get; init; }
    public string? Category { get; init; }
    public bool IsBuiltIn { get; init; } = true;
    public IReadOnlyList<string> SupportedModes { get; init; } = [];
    public string? PreferredTimeframe { get; init; }
    public IReadOnlyList<string> PreferredTimeframes { get; init; } = [];
    public IReadOnlyList<string> AllowedTimeframes { get; init; } = [];
    public IReadOnlyList<string> RequiredTimeframes { get; init; } = [];
    public IReadOnlyList<string> RequiredDataTimeframes { get; init; } = [];
    public IReadOnlyList<string> RequiredIndicators { get; init; } = [];
    public bool ParameterDefinitionsAvailable { get; init; }
    public bool SupportsOptimization { get; init; }
    public bool SupportsValidation { get; init; }
    public bool SupportsLivePaper { get; init; }
    public bool SupportsBacktest { get; init; }
    public bool SupportsBenchmark { get; init; }
    public bool SupportsStrategyLab { get; init; }
    public string? ResearchStatus { get; init; }
    public bool DeploymentQualificationEligible { get; init; } = true;
    public long? CanonicalValidationExperimentId { get; init; }
}

public sealed class StrategyDetailDto : StrategyDto
{
    public required IReadOnlyList<string> SupportedRegimes { get; init; }
    public required IReadOnlyList<string> SupportedTimeframes { get; init; }
}

public sealed class StrategyCatalogDetailDto : StrategyDto
{
    public required string Status { get; init; }
    public IReadOnlyList<string> AnchorTimeframes { get; init; } = [];
    public int WarmupCandles { get; init; }
    public bool SupportsHistoricalPaper { get; init; }
    public bool SupportsReplay { get; init; }
    public IReadOnlyList<StrategyParameterDefinitionDto> ParameterDefinitions { get; init; } = [];
    public string? HowItWorks { get; init; }
    public string? EntryLogic { get; init; }
    public string? ExitLogic { get; init; }
    public string? NoTradeConditions { get; init; }
    public string? RiskManagement { get; init; }
    public string? ApproximationNotes { get; init; }
    public string? ImplementationNotes { get; init; }
    public string? RecommendedValidationMode { get; init; }
    public IReadOnlyList<string> OptimizationGuardrails { get; init; } = [];
    public IReadOnlyList<string> SupportedRegimes { get; init; } = [];
    public IReadOnlyList<string> SupportedTimeframes { get; init; } = [];
}
