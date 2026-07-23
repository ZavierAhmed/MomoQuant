namespace MomoQuant.Domain.PaperTrading;

using MomoQuant.Domain.Common;

public class PaperAccountSnapshot : Entity
{
    public long PaperAccountId { get; set; }
    public long? PaperSessionId { get; set; }
    public long TradingSessionId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal TotalFees { get; set; }
    public decimal Drawdown { get; set; }
    public decimal DrawdownPercent { get; set; }
    public int OpenPositionCount { get; set; }
    public decimal MarginUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
