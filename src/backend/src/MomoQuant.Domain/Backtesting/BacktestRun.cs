namespace MomoQuant.Domain.Backtesting;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class BacktestRun : Entity
{
    public long TradingSessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public Timeframe HigherTimeframe { get; set; }
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal? FinalBalance { get; set; }
    public long RiskProfileId { get; set; }
    public ExecutionMode ExecutionMode { get; set; }
    public bool UseAiScoring { get; set; }
    public long? RequestedByUserId { get; set; }
    public string StrategySetJson { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public BacktestRunStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
