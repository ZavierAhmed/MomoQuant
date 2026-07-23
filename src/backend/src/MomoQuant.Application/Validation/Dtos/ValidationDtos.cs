namespace MomoQuant.Application.Validation.Dtos;

public sealed class DateRangeDto
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
}

public sealed class ValidationSplitDto
{
    public string SplitMethod { get; init; } = "TimeRatio";
    public decimal TrainingPercent { get; init; } = 70m;
    public decimal ValidationPercent { get; init; } = 30m;
    public required DateRangeDto FullDateRange { get; init; }
    public required DateRangeDto TrainingRange { get; init; }
    public required DateRangeDto ValidationRange { get; init; }
}

public sealed class StrategyPerformanceMetricsDto
{
    public decimal NetPnlPercent { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public int TradeCount { get; init; }
    public decimal AverageR { get; init; }
    public decimal Expectancy { get; init; }
    public decimal? SharpeLikeRatio { get; init; }
    public decimal RecoveryFactor { get; init; }
    public decimal LargestLoss { get; init; }
    public int ConsecutiveLosses { get; init; }
}

public sealed class StrategyValidationResultDto
{
    public required string StrategyCode { get; init; }
    public required string Symbol { get; init; }
    public required string Exchange { get; init; }
    public required string Timeframe { get; init; }
    public required DateRangeDto FullDateRange { get; init; }
    public required DateRangeDto TrainingRange { get; init; }
    public required DateRangeDto ValidationRange { get; init; }
    public ValidationSplitDto? SplitInfo { get; init; }
    public long? ParameterSetId { get; init; }
    public string? ParameterSetName { get; init; }
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
    public StrategyPerformanceMetricsDto? TrainingMetrics { get; init; }
    public StrategyPerformanceMetricsDto? ValidationMetrics { get; init; }
    public decimal RobustnessScore { get; init; }
    public string ValidationStatus { get; init; } = "Failed";
    public IReadOnlyList<string> FailReasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; }

    public string BacktestEngineUsed { get; init; } = "BacktestEngine";
    public long? TrainingBacktestRunId { get; init; }
    public long? ValidationBacktestRunId { get; init; }
    public IReadOnlyDictionary<string, string>? StrategyParametersUsed { get; init; }
    public string? ResolvedExecutionTimeframe { get; init; }
    public IReadOnlyList<string> RequiredDataTimeframes { get; init; } = [];
    public IReadOnlyList<CandleCoverageDto> CandleCoverage { get; init; } = [];
    public int TrainingCandleCount { get; init; }
    public int ValidationCandleCount { get; init; }
    public int TrainingWarmupCandlesLoaded { get; init; }
    public int ValidationWarmupCandlesLoaded { get; init; }
    public int TrainingEvaluationCandles { get; init; }
    public int ValidationEvaluationCandles { get; init; }
    public int TrainingEvaluations { get; init; }
    public int ValidationEvaluations { get; init; }
    public int SkippedForWarmupCount { get; init; }
    public bool EngineEvaluationBug { get; init; }
    public bool ImportedDuringRun { get; init; }
    public StrategyFunnelDiagnosticsDto? TrainingFunnel { get; init; }
    public StrategyFunnelDiagnosticsDto? ValidationFunnel { get; init; }
    public string? DiagnosticsSummary { get; init; }
    public ZeroTradeAnalysisDto? WhyZeroTrades { get; init; }
    public ZeroTradeAnalysisDto? TrainingWhyZeroTrades { get; init; }
    public ZeroTradeAnalysisDto? ValidationWhyZeroTrades { get; init; }
    public string? VgResearchProfile { get; init; }
    public bool IsExploratoryProfile { get; init; }
}
