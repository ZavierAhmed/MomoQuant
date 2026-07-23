namespace MomoQuant.Application.Ai.Dtos;

public sealed class AiSetupAdvisorRequestDto
{
    public string Mode { get; init; } = "Benchmark";
    public IReadOnlyList<long> SymbolIds { get; init; } = [];
    public IReadOnlyList<long> StrategyIds { get; init; } = [];
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public long? RiskProfileId { get; init; }
    public string? ExecutionMode { get; init; }
    public bool UseAiScoring { get; init; }
}

public sealed class AiSetupAdvisorResponseDto
{
    public required string Summary { get; init; }
    public required string RecommendedExecutionScope { get; init; }
    public required IReadOnlyList<long> RecommendedStrategies { get; init; }
    public required IReadOnlyList<string> RequiredTimeframes { get; init; }
    public required IReadOnlyList<AiSetupAdvisorPlanItemDto> ImportPlan { get; init; }
    public required IReadOnlyList<AiSetupAdvisorPlanItemDto> IndicatorPlan { get; init; }
    public required IReadOnlyList<string> RiskWarnings { get; init; }
    public required IReadOnlyList<string> DataWarnings { get; init; }
    public required string ExpectedRuntime { get; init; }
    public int EstimatedRunCount { get; init; }
    public required IReadOnlyList<string> Suggestions { get; init; }
    public required IReadOnlyList<string> BlockingIssues { get; init; }
}

public sealed class AiSetupAdvisorPlanItemDto
{
    public long? SymbolId { get; init; }
    public string? Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Reason { get; init; }
}
