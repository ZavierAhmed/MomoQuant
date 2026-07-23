using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.TradingSystems;

/// <summary>Simulated SK LivePaper trade. Never creates real exchange orders.</summary>
public class SkLivePaperTrade : AuditableEntity
{
    public long SessionId { get; set; }
    public long? CandidateId { get; set; }
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public SkLivePaperTradeStatus Status { get; set; } = SkLivePaperTradeStatus.Open;
    public DateTime EntryTimeUtc { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal SimulatedLeverage { get; set; }
    public decimal MarginUsed { get; set; }
    public decimal NotionalValue { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit1 { get; set; }
    public decimal TakeProfit2 { get; set; }
    public DateTime? ExitTimeUtc { get; set; }
    public decimal? ExitPrice { get; set; }
    public SkLivePaperTradeExitReason? ExitReason { get; set; }
    public decimal GrossPnl { get; set; }
    public decimal Fees { get; set; }
    public decimal Slippage { get; set; }
    public decimal NetPnl { get; set; }
    public decimal NetPnlPercent { get; set; }
    public decimal ClarityScore { get; set; }
    public decimal UsefulnessScore { get; set; }
    public string HtfDirection { get; set; } = string.Empty;
    public string LtfDirection { get; set; } = string.Empty;
    public string SimulationMode { get; set; } = "SK_LIVE_PAPER";
}
