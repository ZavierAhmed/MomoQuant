namespace MomoQuant.Domain.Execution;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Order : AuditableEntity
{
    public long TradingSessionId { get; set; }
    public long SymbolId { get; set; }
    public long? TradeId { get; set; }
    public string? ExternalOrderId { get; set; }
    public TradingMode Mode { get; set; }
    public OrderSide Side { get; set; }
    public OrderType OrderType { get; set; }
    public PositionSide PositionSide { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsPostOnly { get; set; }
    public bool IsReduceOnly { get; set; }
    public TimeInForce TimeInForce { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? FilledAtUtc { get; set; }
    public string? FailureReason { get; set; }
}
