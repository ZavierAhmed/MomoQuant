namespace MomoQuant.Domain.PaperTrading;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class PaperTradingSession : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public long PaperAccountId { get; set; }
    public long TradingSessionId { get; set; }
    public PaperSessionStatus Status { get; set; } = PaperSessionStatus.Created;
    public PaperTradingMode Mode { get; set; } = PaperTradingMode.HistoricalPaper;
    public long ExchangeId { get; set; }
    public long RiskProfileId { get; set; }
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.MarketFill;
    public bool UseAiScoring { get; set; }
    public decimal MinConfidenceScore { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public DateTime? CurrentCandleTimeUtc { get; set; }
    public int CurrentCandleIndex { get; set; } = -1;
    public int TotalCandles { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? RequestedByUserId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ConfigJson { get; set; }
}
