using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.ValidationLab;

public class ValidationParameterTrial : Entity
{
    public long ValidationExperimentId { get; set; }
    public int TrialNumber { get; set; }
    public string ParameterSnapshotJson { get; set; } = "{}";
    public string ParameterFingerprint { get; set; } = string.Empty;
    public ValidationTrialStatus Status { get; set; } = ValidationTrialStatus.Pending;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RawCandidateCount { get; set; }
    public int ClosedTradeCount { get; set; }
    public int WinnerCount { get; set; }
    public int LoserCount { get; set; }
    public int ExpiredCount { get; set; }
    public decimal? NetExpectancyR { get; set; }
    public decimal? GrossPnl { get; set; }
    public decimal? NetPnl { get; set; }
    public decimal? ProfitFactor { get; set; }
    public decimal? MaximumDrawdownPercent { get; set; }
    public decimal? FeeImpactPercent { get; set; }
    public decimal? TrainingScore { get; set; }
    public string GuardrailDecision { get; set; } = "NotEvaluated";
    public string? GuardrailFailureReasonsJson { get; set; }
    public int? Rank { get; set; }
    public string? DiagnosticWarningsJson { get; set; }
    public long? StrategyLabRunId { get; set; }
    public string? ErrorMessage { get; set; }
    public ValidationTrialRecoverySource RecoverySource { get; set; } = ValidationTrialRecoverySource.None;
}