namespace MomoQuant.Domain.Replay;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class ReplaySession : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public long TradingSessionId { get; set; }
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal CurrentEquity { get; set; }
    public long RiskProfileId { get; set; }
    public ExecutionMode ExecutionMode { get; set; }
    public bool UseAiScoring { get; set; }
    public ReplaySpeed Speed { get; set; } = ReplaySpeed.ManualStep;
    public ReplaySessionStatus Status { get; set; }
    public int CurrentFrameIndex { get; set; } = -1;
    public long? CurrentCandleId { get; set; }
    public int TotalFrames { get; set; }
    public long? RequestedByUserId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public DateTime? CurrentReplayTimeUtc { get; set; }
}
