namespace MomoQuant.Domain.Replay;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class ReplayFrame : Entity
{
    public long ReplaySessionId { get; set; }
    public int FrameIndex { get; set; }
    public long CandleId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public MarketRegime MarketRegime { get; set; }
    public string StrategyResultsJson { get; set; } = "[]";
    public long? AiDecisionId { get; set; }
    public long? RiskDecisionId { get; set; }
    public long? OrderId { get; set; }
    public long? TradeId { get; set; }
    public long? MissedOrderId { get; set; }
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal Drawdown { get; set; }
    public decimal DrawdownPercent { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
