using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.StrategyLab;

/// <summary>Normalized IndependentPaths/v1 assessment row (one per candidate x path).</summary>
public class StrategyResearchCandidatePortfolioAssessment : Entity
{
    public long StrategyResearchCandidateId { get; set; }
    public StrategyLabPortfolioPath PortfolioPath { get; set; }
    public decimal AssessmentBalance { get; set; }
    public decimal? RiskAmount { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? PositionNotional { get; set; }
    public decimal? MinimumRequiredLeverage { get; set; }
    public decimal? AssessmentLeverage { get; set; }
    public decimal? InitialMarginRequired { get; set; }
    public decimal? CandidateMarginUsagePercent { get; set; }
    public decimal? CurrentNotionalExposurePercent { get; set; }
    public decimal? CurrentMarginUsagePercent { get; set; }
    public decimal? ProjectedTotalNotionalExposurePercent { get; set; }
    public decimal? ProjectedTotalMarginUsagePercent { get; set; }
    public decimal? CurrentConcurrentRiskPercent { get; set; }
    public decimal? ProjectedConcurrentRiskPercent { get; set; }
    public decimal? CurrentDailyLossUsagePercent { get; set; }
    public decimal? CurrentDrawdownPercent { get; set; }
    public int CurrentOpenPositionCount { get; set; }
    public decimal? PortfolioRiskScore { get; set; }
    public ResearchRiskScoreDecision RiskScoreDecision { get; set; } = ResearchRiskScoreDecision.NotEvaluated;
    public ResearchHardRuleComplianceDecision HardRuleComplianceDecision { get; set; } =
        ResearchHardRuleComplianceDecision.NotEvaluated;
    public ResearchRiskDecision FinancialRiskDecision { get; set; } = ResearchRiskDecision.NotEvaluated;
    public string RiskReason { get; set; } = string.Empty;
    public string? FailedRuleKeysJson { get; set; }
    public string? WarningRuleKeysJson { get; set; }
    public string? RuleResultsJson { get; set; }
    public ShadowEntryDecision EntryDecision { get; set; } = ShadowEntryDecision.NotEvaluated;
    public string EntryDecisionReason { get; set; } = string.Empty;
    public string? RejectionSourcesJson { get; set; }
    public string AssessmentVersion { get; set; } = "IndependentPaths/v1";
    public DateTime EvaluatedAtUtc { get; set; }
}