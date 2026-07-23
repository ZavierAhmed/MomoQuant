using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Simulation.Dtos;

public sealed class SimulationRunSummaryDto
{
    public required long Id { get; init; }
    public required SimulationRunSourceType SourceType { get; init; }
    public required long SourceId { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }

    public IReadOnlyList<string> Symbols { get; init; } = [];
    public IReadOnlyList<string> Strategies { get; init; } = [];
    public IReadOnlyList<string> Timeframes { get; init; } = [];
    public string? EvaluationMode { get; init; }

    public decimal InitialBalance { get; init; }
    public decimal FinalBalance { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal MaxDrawdown { get; init; }

    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }

    public int CandidateSignals { get; init; }
    public int ConfidenceRejected { get; init; }
    public int RiskRejected { get; init; }
    public int ExecutedTrades { get; init; }
    public int ShadowTrades { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public int RejectedWouldHaveWon { get; init; }
    public int RejectedWouldHaveLost { get; init; }

    public string SummaryText { get; init; } = string.Empty;
    public IReadOnlyList<string> KeyFindings { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

/// <summary>
/// Input model used by run/session completion hooks to record a unified simulation summary.
/// </summary>
public sealed class SimulationRunSummaryInput
{
    public required SimulationRunSourceType SourceType { get; init; }
    public required long SourceId { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }

    public IReadOnlyList<string> Symbols { get; init; } = [];
    public IReadOnlyList<string> Strategies { get; init; } = [];
    public IReadOnlyList<string> Timeframes { get; init; } = [];
    public string? EvaluationMode { get; init; }

    public decimal InitialBalance { get; init; }
    public decimal FinalBalance { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal MaxDrawdown { get; init; }

    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }

    public int CandidateSignals { get; init; }
    public int ConfidenceRejected { get; init; }
    public int RiskRejected { get; init; }
    public int ExecutedTrades { get; init; }
    public int ShadowTrades { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public int RejectedWouldHaveWon { get; init; }
    public int RejectedWouldHaveLost { get; init; }

    public IReadOnlyList<string> KeyFindings { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
