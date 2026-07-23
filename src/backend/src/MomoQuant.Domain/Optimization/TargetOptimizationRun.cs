using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.Optimization;

public class TargetOptimizationRun : Entity
{
    public string StrategyCode { get; set; } = string.Empty;
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string Timeframe { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public ValidationSplitMode ValidationSplitMode { get; set; } = ValidationSplitMode.InSampleOutOfSample70_30;
    public ParameterSearchMode ParameterSearchMode { get; set; } = ParameterSearchMode.GridSearch;
    public int MaxCombinations { get; set; } = 200;
    public int MaxAttempts { get; set; } = 200;
    public int TotalCombinations { get; set; }
    public int CompletedCombinations { get; set; }
    public TargetOptimizationStatus Status { get; set; } = TargetOptimizationStatus.Pending;
    public string? TargetRulesJson { get; set; }
    public string? ResultJson { get; set; }
    public string? WarningsJson { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CurrentParametersJson { get; set; }
    public int TrainingPassedCount { get; set; }
    public int ValidationPassedCount { get; set; }
    public int OverfitCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime? HeartbeatAtUtc { get; set; }
    public long? RequestedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
