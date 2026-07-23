using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.Benchmarks;

public class StrategyBenchmarkRunItem : Entity
{
    public long BenchmarkRunId { get; set; }
    public long StrategyId { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public StrategyBenchmarkRunItemStatus Status { get; set; } = StrategyBenchmarkRunItemStatus.Pending;
    public long? BacktestRunId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public int? CandleCount { get; set; }
    public DateTime? LastProcessedCandleTimeUtc { get; set; }
    public int? LastProcessedCandleIndex { get; set; }
    public int? TotalCandles { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
