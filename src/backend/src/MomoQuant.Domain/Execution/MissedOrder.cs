namespace MomoQuant.Domain.Execution;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class MissedOrder : Entity
{
    public long TradingSessionId { get; set; }
    public long SignalId { get; set; }
    public long SymbolId { get; set; }
    public decimal RequestedPrice { get; set; }
    public decimal? BestBid { get; set; }
    public decimal? BestAsk { get; set; }
    public MissedOrderReason Reason { get; set; }
    public DateTime ExpiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
