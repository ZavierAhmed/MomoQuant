using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.TradingSystems;

/// <summary>SK System LivePaper simulation session. Simulated orders only — never real execution.</summary>
public class SkLivePaperSession : AuditableEntity
{
    public string SessionName { get; set; } = string.Empty;
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string HigherTimeframe { get; set; } = "4h";
    public string PrimaryTimeframe { get; set; } = "1h";
    public string AdditionalTimeframesJson { get; set; } = "[]";
    public decimal StartingBalance { get; set; } = 10_000m;
    public decimal CurrentBalance { get; set; } = 10_000m;
    public decimal RiskPerPaperTradePercent { get; set; } = 0.5m;
    public int MaxPaperTradesPerDay { get; set; } = 3;
    public int MaxOpenPaperPositions { get; set; } = 1;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
    public bool RequireHtfAgreement { get; set; } = true;
    public decimal MinClarityScore { get; set; } = 60m;
    public decimal MinUsefulnessScore { get; set; } = 60m;
    public bool RequireReactionConfirmation { get; set; } = true;
    public string ConfirmationMode { get; set; } = "CloseBackInDirection";
    public decimal SimulatedLeverage { get; set; } = 3m;
    public SkLivePaperSessionStatus Status { get; set; } = SkLivePaperSessionStatus.Created;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public DateTime? LastAnalyzedCandleUtc { get; set; }
    public string? LastError { get; set; }
    public int TradesOpenedToday { get; set; }
    public DateTime? TradesOpenedDayUtc { get; set; }
    public long? CreatedByUserId { get; set; }

    /// <summary>Always SK_LIVE_PAPER — separates from real execution paths.</summary>
    public string SimulationMode { get; set; } = "SK_LIVE_PAPER";
}
