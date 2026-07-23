using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.Benchmarks;

public class StrategyBenchmarkRun : Entity
{
    public string Name { get; set; } = string.Empty;
    public StrategyBenchmarkStatus Status { get; set; }
    public long ExchangeId { get; set; }
    public string SymbolsJson { get; set; } = "[]";
    public string TimeframesJson { get; set; } = "[]";
    public string StrategyIdsJson { get; set; } = "[]";
    public DateTime BenchmarkFromUtc { get; set; }
    public DateTime BenchmarkToUtc { get; set; }
    public DateTime WarmupFromUtc { get; set; }
    public DateTime WarmupToUtc { get; set; }
    public decimal InitialBalance { get; set; }
    public long RiskProfileId { get; set; }
    public ExecutionMode ExecutionMode { get; set; }
    public decimal MakerFeeRate { get; set; }
    public decimal TakerFeeRate { get; set; }
    public int OrderExpiryCandles { get; set; }
    public bool UseAiScoring { get; set; }
    public decimal MinConfidenceScore { get; set; }
    public bool IncludeDisabledStrategies { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public string? CurrentStage { get; set; }
    public decimal PercentComplete { get; set; }
    public string? CurrentSymbol { get; set; }
    public string? CurrentTimeframe { get; set; }
    public string? CurrentStrategy { get; set; }
    public int CompletedRuns { get; set; }
    public int TotalRuns { get; set; }
    public string? Message { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public decimal DataPreparationPercent { get; set; }
    public decimal BacktestPercent { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
