using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Optimization.Dtos;

public sealed class ParameterSetResultDto
{
    public int Rank { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public StrategyPerformanceMetricsDto? TrainingMetrics { get; init; }
    public StrategyPerformanceMetricsDto? ValidationMetrics { get; init; }
    public decimal RobustnessScore { get; init; }
    public decimal OptimizationScore { get; init; }
    public string PassStatus { get; init; } = "Failed";
    public IReadOnlyList<string> FailReasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class ParameterOptimizationResultDto
{
    public long OptimizationRunId { get; init; }
    public required string StrategyCode { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required ValidationSplitDto DateRange { get; init; }
    public int TotalCombinations { get; init; }
    public int CompletedCombinations { get; init; }
    public string Status { get; init; } = "Pending";
    public IReadOnlyList<ParameterSetResultDto> BestParameterSets { get; init; } = [];
    public IReadOnlyList<ParameterSetResultDto> RejectedParameterSets { get; init; } = [];
    public IReadOnlyList<ParameterSetResultDto> ZeroTradeParameterSets { get; init; } = [];
    public int ZeroTradeParameterSetCount { get; init; }
    public int TradeProducingParameterSetCount { get; init; }
    public ParameterSetResultDto? BestNonZeroTradeParameterSet { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}

public sealed class RunParameterOptimizationRequest
{
    public required string StrategyCode { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public string? FromDate { get; init; }
    public string? ToDate { get; init; }
    public ValidationMode ValidationMode { get; init; } = ValidationMode.InSampleOutOfSample70_30;
    public ParameterOptimizationMode OptimizationMode { get; init; } = ParameterOptimizationMode.GridSearch;
    public string ObjectivePreset { get; init; } = "Balanced";
    public int MaxCombinations { get; init; } = 500;
    public int MaxRuntimeMinutes { get; init; } = 30;
    public int MinTradesTraining { get; init; } = 20;
    public int MinTradesValidation { get; init; } = 10;
    public decimal MaxDrawdownPercent { get; init; } = 25m;
    public bool SaveBestParameterSet { get; init; }
    public string? ParameterSetName { get; init; }
    public IReadOnlyDictionary<string, string>? ParameterRangeOverrides { get; init; }
    public IReadOnlyDictionary<string, string>? FixedParameters { get; init; }
    public long RiskProfileId { get; init; }
    public decimal InitialBalance { get; init; } = 10000m;
    public string? ExecutionMode { get; init; }
    public decimal? MakerFeeRate { get; init; }
    public decimal? TakerFeeRate { get; init; }
    public int? OrderExpiryCandles { get; init; }
    public bool? UseAiScoring { get; init; }
    public decimal? MinConfidenceScore { get; init; }
    public decimal? SlippagePercent { get; init; }
    public bool AutoImportCandles { get; init; } = true;
    public string? VgResearchProfile { get; init; }
}

public sealed class RunStrategyValidationRequest
{
    public required string StrategyCode { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public string? FromDate { get; init; }
    public string? ToDate { get; init; }
    public ValidationMode ValidationMode { get; init; } = ValidationMode.InSampleOutOfSample70_30;
    public long? ParameterSetId { get; init; }
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
    public long RiskProfileId { get; init; }
    public decimal InitialBalance { get; init; } = 10000m;
    public decimal MaxDrawdownPercent { get; init; } = 25m;
    public string? ExecutionMode { get; init; }
    public decimal? MakerFeeRate { get; init; }
    public decimal? TakerFeeRate { get; init; }
    public int? OrderExpiryCandles { get; init; }
    public bool? UseAiScoring { get; init; }
    public decimal? MinConfidenceScore { get; init; }
    public decimal? SlippagePercent { get; init; }
    public bool AutoImportCandles { get; init; } = true;
    public string? VgResearchProfile { get; init; }
}

public sealed class SaveStrategyParameterSetRequest
{
    public required string Name { get; init; }
    public required string StrategyCode { get; init; }
    public long? SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public long? OptimizationRunId { get; init; }
    public long? TargetOptimizationRunId { get; init; }
    public string? Source { get; init; }
    public bool Approve { get; init; }
    public bool SetAsDefault { get; init; }
    public StrategyPerformanceMetricsDto? TrainingMetrics { get; init; }
    public StrategyPerformanceMetricsDto? ValidationMetrics { get; init; }
    public decimal? RobustnessScore { get; init; }
    public DateRangeDto? TrainingRange { get; init; }
    public DateRangeDto? ValidationRange { get; init; }
    public string? ValidationStatus { get; init; }
    public int? ValidationTradeCount { get; init; }
    public bool SaveAsFailedResearch { get; init; }
}

public sealed class StrategyParameterSetDto
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string StrategyCode { get; init; }
    public long? SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public string Source { get; init; } = "Manual";
    public long? OptimizationRunId { get; init; }
    public decimal? RobustnessScore { get; init; }
    public bool IsApproved { get; init; }
    public bool IsDefaultForStrategy { get; init; }
    public bool IsDefaultForSymbolTimeframe { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
}
