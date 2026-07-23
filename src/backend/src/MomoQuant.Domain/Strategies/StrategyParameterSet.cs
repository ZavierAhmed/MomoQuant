using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.Strategies;

/// <summary>
/// Frozen, user-approved strategy parameter set for backtest, benchmark, and LivePaper simulation.
/// Parameters must not mutate during live/paper execution.
/// </summary>
public class StrategyParameterSet : Entity
{
    public string Name { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public long? SymbolId { get; set; }
    public string Timeframe { get; set; } = string.Empty;
    public string? MarketRegime { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public StrategyParameterSetSource Source { get; set; } = StrategyParameterSetSource.Manual;
    public long? OptimizationRunId { get; set; }
    public string? TrainingRangeJson { get; set; }
    public string? ValidationRangeJson { get; set; }
    public string? TrainingMetricsJson { get; set; }
    public string? ValidationMetricsJson { get; set; }
    public decimal? RobustnessScore { get; set; }
    public bool IsApproved { get; set; }
    public bool IsDefaultForStrategy { get; set; }
    public bool IsDefaultForSymbolTimeframe { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}
