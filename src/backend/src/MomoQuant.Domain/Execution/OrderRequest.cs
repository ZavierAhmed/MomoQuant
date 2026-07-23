namespace MomoQuant.Domain.Execution;

using MomoQuant.Domain.Enums;

public sealed class OrderRequest
{
    public required long TradingSessionId { get; init; }
    public required long SymbolId { get; init; }
    public required TradingMode Mode { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType OrderType { get; init; }
    public required PositionSide PositionSide { get; init; }
    public required decimal Price { get; init; }
    public required decimal Quantity { get; init; }
    public bool IsPostOnly { get; init; }
    public bool IsReduceOnly { get; init; }
    public TimeInForce TimeInForce { get; init; } = TimeInForce.Gtc;
    public DateTime RequestedAtUtc { get; init; }
}
