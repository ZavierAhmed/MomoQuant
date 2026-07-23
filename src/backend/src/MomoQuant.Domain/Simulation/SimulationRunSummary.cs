using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.Simulation;

/// <summary>
/// Unified, human-readable summary for any simulation run or session
/// (Backtest, StrategyBenchmark, Replay, HistoricalPaper, LivePaper).
/// Simulation only — never represents real trading.
/// </summary>
public class SimulationRunSummary : Entity
{
    public SimulationRunSourceType SourceType { get; set; }
    public long SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public string SymbolsJson { get; set; } = "[]";
    public string StrategiesJson { get; set; } = "[]";
    public string TimeframesJson { get; set; } = "[]";
    public string? EvaluationMode { get; set; }

    public decimal InitialBalance { get; set; }
    public decimal FinalBalance { get; set; }
    public decimal NetPnl { get; set; }
    public decimal NetPnlPercent { get; set; }
    public decimal MaxDrawdown { get; set; }

    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRatePercent { get; set; }

    public int CandidateSignals { get; set; }
    public int ConfidenceRejected { get; set; }
    public int RiskRejected { get; set; }
    public int ExecutedTrades { get; set; }
    public int ShadowTrades { get; set; }
    public decimal ShadowNetPnl { get; set; }
    public int RejectedWouldHaveWon { get; set; }
    public int RejectedWouldHaveLost { get; set; }

    public string SummaryText { get; set; } = string.Empty;
    public string KeyFindingsJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
