using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Optimization.Dtos;

public sealed class TargetOptimizationRulesDto
{
    public decimal MinTrainingNetPnlPercent { get; init; } = 2.0m;
    public decimal MinValidationNetPnlPercent { get; init; } = 0.5m;
    public decimal MinTrainingProfitFactor { get; init; } = 1.2m;
    public decimal MinValidationProfitFactor { get; init; } = 1.1m;
    public decimal MaxTrainingDrawdownPercent { get; init; } = 10.0m;
    public decimal MaxValidationDrawdownPercent { get; init; } = 8.0m;
    public int MinTrainingTrades { get; init; } = 20;
    public int MinValidationTrades { get; init; } = 10;
    public decimal MaxValidationPnLDropPercent { get; init; } = 70m;
    public decimal MaxValidationProfitFactorDropPercent { get; init; } = 50m;
    public decimal MinRobustnessScore { get; init; } = 60m;
    public bool AllowSaveIfValidationWarning { get; init; }
    public bool AutoApproveIfPassed { get; init; }

    public static TargetOptimizationRulesDto DefaultResearch() => new();
}

public sealed record TargetPassSummary
{
    public bool TrainingPnlPassed { get; init; }
    public bool ValidationPnlPassed { get; init; }
    public bool TrainingProfitFactorPassed { get; init; }
    public bool ValidationProfitFactorPassed { get; init; }
    public bool TrainingDrawdownPassed { get; init; }
    public bool ValidationDrawdownPassed { get; init; }
    public bool TrainingTradesPassed { get; init; }
    public bool ValidationTradesPassed { get; init; }
    public bool RobustnessPassed { get; init; }

    public bool TrainingPassed =>
        TrainingPnlPassed && TrainingProfitFactorPassed && TrainingDrawdownPassed && TrainingTradesPassed;

    public bool ValidationPassed =>
        ValidationPnlPassed && ValidationProfitFactorPassed && ValidationDrawdownPassed &&
        ValidationTradesPassed && RobustnessPassed;
}

public sealed record TargetParameterSetResultDto
{
    public int Rank { get; init; }
    public ParameterSetTestStatus Status { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public StrategyPerformanceMetricsDto? TrainingMetrics { get; init; }
    public StrategyPerformanceMetricsDto? ValidationMetrics { get; init; }
    public decimal RobustnessScore { get; init; }
    public decimal Score { get; init; }
    public TargetPassSummary TargetPassSummary { get; init; } = new();
    public IReadOnlyList<string> FailReasons { get; init; } = [];
    public IReadOnlyList<string> OverfitWarnings { get; init; } = [];
    public long? SavedParameterSetId { get; init; }
    public bool IsApproved { get; init; }
}

public sealed class TargetOptimizationSummaryDto
{
    public string BestStatus { get; init; } = "None";
    public int PassedCount { get; init; }
    public int OverfitCount { get; init; }
    public int FailedCount { get; init; }
    public int TrainingPassedCount { get; init; }
    public decimal? BestRobustnessScore { get; init; }
    public decimal? BestValidationNetPnlPercent { get; init; }
    public decimal? BestValidationProfitFactor { get; init; }
    public decimal? BestValidationDrawdownPercent { get; init; }
}

public sealed record TargetOptimizationRunDto
{
    public long Id { get; init; }
    public required string StrategyCode { get; init; }
    public long SymbolId { get; init; }
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required DateRangeDto DateRange { get; init; }
    public required DateRangeDto TrainingRange { get; init; }
    public required DateRangeDto ValidationRange { get; init; }
    public TargetOptimizationRulesDto TargetRules { get; init; } = TargetOptimizationRulesDto.DefaultResearch();
    public ParameterSearchMode ParameterSearchMode { get; init; } = ParameterSearchMode.GridSearch;
    public int MaxCombinations { get; init; }
    public int CompletedCombinations { get; init; }
    public TargetOptimizationStatus Status { get; init; } = TargetOptimizationStatus.Pending;
    public TargetParameterSetResultDto? BestPassedParameterSet { get; init; }
    public TargetParameterSetResultDto? BestFailedParameterSet { get; init; }
    public IReadOnlyList<TargetParameterSetResultDto> Results { get; init; } = [];
    public TargetOptimizationSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyDictionary<string, string>? CurrentParameters { get; init; }
    public int TrainingPassedCount { get; init; }
    public int ValidationPassedCount { get; init; }
    public int OverfitCount { get; init; }
    public int FailedCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? HeartbeatAtUtc { get; init; }
}

public sealed class TargetOptimizationRequest
{
    public required string StrategyCode { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public string? FromDate { get; init; }
    public string? ToDate { get; init; }
    public ValidationSplitMode ValidationSplitMode { get; init; } = ValidationSplitMode.InSampleOutOfSample70_30;
    public ParameterSearchMode ParameterSearchMode { get; init; } = ParameterSearchMode.GridSearch;
    public TargetOptimizationRulesDto? TargetRules { get; init; }
    public IReadOnlyDictionary<string, string>? ParameterRanges { get; init; }
    public int MaxCombinations { get; init; } = 200;
    public int MaxAttempts { get; init; } = 200;
    public int MaxRuntimeMinutes { get; init; } = 30;
    public decimal InitialBalance { get; init; } = 10000m;
    public long RiskProfileId { get; init; }
    public decimal? MakerFeeRate { get; init; }
    public decimal? TakerFeeRate { get; init; }
    public decimal? SlippagePercent { get; init; }
    public string? ExecutionMode { get; init; }
    public int? OrderExpiryCandles { get; init; }
    public bool? UseAiScoring { get; init; }
    public decimal? MinimumConfidenceScore { get; init; }
    public bool AutoImportMissingCandles { get; init; } = true;
    public bool SaveBestIfPassed { get; init; }
    public bool AutoApproveIfPassed { get; init; }
    public IReadOnlyDictionary<string, string>? FixedParameters { get; init; }
    public string? VgResearchProfile { get; init; }
}

public sealed class SaveTargetOptimizationBestRequest
{
    public bool Approve { get; init; }
    public string? Name { get; init; }
    public bool SaveAsFailedResearch { get; init; }
}
