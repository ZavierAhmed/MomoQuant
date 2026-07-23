using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.StrategyLab;

public class StrategyLabRun : Entity
{
    public string Name { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string StrategyVersion { get; set; } = "1.0.0";
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public StrategyLabExecutionMode ExecutionMode { get; set; } = StrategyLabExecutionMode.RawStrategy;
    public string ParametersJson { get; set; } = "{}";
    public string StrategyFeatureFlagsJson { get; set; } = "{}";
    public decimal InitialBalance { get; set; }
    public string FeeSettingsJson { get; set; } = "{}";
    public string SlippageSettingsJson { get; set; } = "{}";
    public StrategyLabRunStatus Status { get; set; } = StrategyLabRunStatus.Created;
    public string ExperimentFingerprint { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string? GitCommit { get; set; }
    public string CandleDatasetFingerprint { get; set; } = string.Empty;
    public string StrategyCodeFingerprint { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public long? RiskProfileId { get; set; }
    public string? RiskProfileSnapshotJson { get; set; }
    public string ResultSummaryJson { get; set; } = "{}";
    public int EvaluationsCount { get; set; }
    public int RawCandidateCount { get; set; }
    public string? CurrentStage { get; set; }
    public decimal PercentComplete { get; set; }
}
