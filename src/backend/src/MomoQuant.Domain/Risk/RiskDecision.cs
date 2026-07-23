namespace MomoQuant.Domain.Risk;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class RiskDecision : Entity
{
    public long? TradingSessionId { get; set; }
    public long? SignalId { get; set; }
    public long? AiDecisionId { get; set; }
    public long SymbolId { get; set; }
    public RiskDecisionType Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal? ApprovedRiskPercent { get; set; }
    public decimal? PositionSize { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public string? RejectedRuleKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
