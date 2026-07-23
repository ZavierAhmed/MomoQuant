namespace MomoQuant.Domain.Risk;

using MomoQuant.Domain.Enums;

public sealed class RiskEvaluationResult
{
    public required RiskDecisionType Decision { get; init; }
    public bool Approved => Decision is RiskDecisionType.Approved or RiskDecisionType.Adjusted;
    public required string Reason { get; init; }
    public string? RejectedRuleKey { get; init; }
    public decimal? ApprovedRiskPercent { get; init; }
    public decimal? PositionSize { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public decimal? RiskAmount { get; init; }
    public string? RawDataJson { get; init; }

    public static RiskEvaluationResult Reject(
        string ruleKey,
        string reason,
        RiskDecisionType decision = RiskDecisionType.Rejected,
        string? rawDataJson = null) =>
        new()
        {
            Decision = decision,
            Reason = reason,
            RejectedRuleKey = ruleKey,
            RawDataJson = rawDataJson
        };
}
