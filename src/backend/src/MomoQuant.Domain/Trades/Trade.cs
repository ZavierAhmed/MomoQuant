namespace MomoQuant.Domain.Trades;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Trade : AuditableEntity
{
    public long TradingSessionId { get; set; }
    public long SymbolId { get; set; }
    public long? StrategyId { get; set; }
    public long? SignalId { get; set; }
    public long? AiDecisionId { get; set; }
    public long? RiskDecisionId { get; set; }
    public TradeDirection Direction { get; set; }
    public long? EntryOrderId { get; set; }
    public long? ExitOrderId { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public TradeStatus Status { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public decimal GrossPnl { get; set; }
    public decimal Fees { get; set; }
    public decimal FundingFees { get; set; }
    public decimal NetPnl { get; set; }
    public decimal? RMultiple { get; set; }
    public CloseReason? CloseReason { get; set; }
}
