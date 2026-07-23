namespace MomoQuant.Application.Strategies.Dtos;

public sealed class StrategyDataRequirementDto
{
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string PreferredExecutionTimeframe { get; init; }
    public required IReadOnlyList<string> AllowedExecutionTimeframes { get; init; }
    public required IReadOnlyList<string> RequiredDataTimeframes { get; init; }
    public required IReadOnlyList<string> OptionalDataTimeframes { get; init; }
    public required IReadOnlyList<string> AnchorTimeframes { get; init; }
    public required IReadOnlyList<string> HigherTimeframeFilters { get; init; }
    public int WarmupCandles { get; init; }
    public int MinBenchmarkDays { get; init; }
    public int RecommendedBenchmarkDays { get; init; }
    public bool RequiresIndicators { get; init; }
    public required IReadOnlyList<string> RequiredIndicators { get; init; }
    public required IReadOnlyList<string> RequiredIndicatorTimeframes { get; init; }
    public bool SupportsBacktest { get; init; }
    public bool SupportsReplay { get; init; }
    public bool SupportsHistoricalPaper { get; init; }
    public bool SupportsLivePaper { get; init; }
    public bool SupportsBenchmark { get; init; }
    public bool SupportsValidation { get; init; }
    public bool SupportsOptimization { get; init; }
    public bool SupportsStrategyLab { get; init; }
    public IReadOnlyList<string> PreferredTimeframes { get; init; } = [];
    public string? Notes { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class ResolveStrategyRequirementsRequest
{
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public IReadOnlyList<long> SymbolIds { get; init; } = [];
    public DateOnly? BenchmarkFromDate { get; init; }
    public DateOnly? BenchmarkToDate { get; init; }
    public string Mode { get; init; } = "Benchmark";
    public string ExecutionScope { get; init; } = "PreferredOnly";
    public IReadOnlyList<string>? ManualExecutionTimeframes { get; init; }
}

public sealed class ResolveStrategyRequirementsResponse
{
    public required IReadOnlyList<string> RequiredTimeframes { get; init; }
    public required IReadOnlyList<StrategyExecutionPlanItemDto> ExecutionPlan { get; init; }
    public required IReadOnlyList<StrategyImportPlanItemDto> ImportPlan { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> BlockingIssues { get; init; }
}

public sealed class StrategyExecutionPlanItemDto
{
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string PreferredExecutionTimeframe { get; init; }
    public required IReadOnlyList<string> ExecutionTimeframes { get; init; }
    public required IReadOnlyList<string> RequiredDataTimeframes { get; init; }
    public required IReadOnlyList<string> RequiredIndicatorTimeframes { get; init; }
    public required IReadOnlyList<string> AnchorTimeframes { get; init; }
}

public sealed class StrategyImportPlanItemDto
{
    public long? SymbolId { get; init; }
    public string? Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Reason { get; init; }
}
