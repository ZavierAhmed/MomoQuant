using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.Optimization;

public class ParameterOptimizationRun : Entity
{
    public string StrategyCode { get; set; } = string.Empty;
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string Timeframe { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public ValidationMode ValidationMode { get; set; }
    public ParameterOptimizationMode OptimizationMode { get; set; }
    public string ObjectivePreset { get; set; } = "Balanced";
    public int MaxCombinations { get; set; } = 500;
    public int TotalCombinations { get; set; }
    public int CompletedCombinations { get; set; }
    public ParameterOptimizationRunStatus Status { get; set; } = ParameterOptimizationRunStatus.Pending;
    public string? ResultJson { get; set; }
    public string? WarningsJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? HeartbeatAtUtc { get; set; }
    public long? RequestedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
