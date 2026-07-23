namespace MomoQuant.Domain.Trades;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Position : Entity
{
    public long TradingSessionId { get; set; }
    public long SymbolId { get; set; }
    public TradeDirection Direction { get; set; }
    public decimal Quantity { get; set; }
    public decimal AverageEntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal Leverage { get; set; }
    public decimal MarginUsed { get; set; }
    public PositionStatus Status { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}
