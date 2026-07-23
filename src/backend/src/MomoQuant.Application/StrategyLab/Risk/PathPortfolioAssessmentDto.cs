using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyLab.Risk;

public static class IndependentPathsVersions
{
    public const string Current = "IndependentPaths/v1";
}

/// <summary>Path-specific financial + portfolio assessment for one candidate.</summary>
public sealed class PathPortfolioAssessmentDto
{
    public string PortfolioPath { get; init; } = string.Empty;
    public decimal AssessmentBalance { get; init; }
    public decimal? RiskAmount { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? PositionNotional { get; init; }
    public decimal? MinimumRequiredLeverage { get; init; }
    public decimal? AssessmentLeverage { get; init; }
    public decimal? InitialMarginRequired { get; init; }
    public decimal? CandidateMarginUsagePercent { get; init; }
    public decimal? CandidateNotionalExposurePercent { get; init; }
    public decimal? CurrentNotionalExposurePercent { get; init; }
    public decimal? CurrentMarginUsagePercent { get; init; }
    public decimal? ProjectedTotalNotionalExposurePercent { get; init; }
    public decimal? ProjectedTotalMarginUsagePercent { get; init; }
    public decimal? CurrentConcurrentRiskPercent { get; init; }
    public decimal? ProjectedConcurrentRiskPercent { get; init; }
    public decimal? CurrentDailyLossUsagePercent { get; init; }
    public decimal? CurrentDrawdownPercent { get; init; }
    public int CurrentOpenPositionCount { get; init; }
    public decimal? PortfolioRiskScore { get; init; }
    public ResearchRiskScoreDecision RiskScoreDecision { get; init; } = ResearchRiskScoreDecision.NotEvaluated;
    public ResearchHardRuleComplianceDecision HardRuleComplianceDecision { get; init; } =
        ResearchHardRuleComplianceDecision.NotEvaluated;
    public ResearchRiskDecision FinancialRiskDecision { get; init; } = ResearchRiskDecision.NotEvaluated;
    public string RiskReason { get; init; } = string.Empty;
    public IReadOnlyList<string> FailedRuleKeys { get; init; } = [];
    public IReadOnlyList<string> WarningRuleKeys { get; init; } = [];
    public IReadOnlyList<RiskRuleResultDto> RuleResults { get; init; } = [];
    public ShadowEntryDecision EntryDecision { get; init; } = ShadowEntryDecision.NotEvaluated;
    public string EntryDecisionReason { get; init; } = string.Empty;
    public IReadOnlyList<string> RejectionSources { get; init; } = [];
    public DateTime EvaluatedAtUtc { get; init; }
    public string AssessmentVersion { get; init; } = IndependentPathsVersions.Current;
}

public sealed class PortfolioPathDivergenceDto
{
    public DateTime? FirstDivergenceAtUtc { get; init; }
    public decimal? FinalBalanceDifference { get; init; }
    public decimal? MaxDrawdownDifference { get; init; }
    public int TradeCountDifference { get; init; }
    public int DifferentPortfolioRiskDecisions { get; init; }
    public int OpenedOnlyInRiskOnly { get; init; }
    public int OpenedOnlyInFullPipeline { get; init; }
    public int OpenedInBoth { get; init; }
    public int OpenedInNeither { get; init; }
}

public static class PathAssessmentFactory
{
    public static PathPortfolioAssessmentDto FromObservation(
        StrategyLabPortfolioPath path,
        StrategyLabRiskObservationResult result,
        ShadowEntryDecision entryDecision,
        string entryReason,
        IReadOnlyList<string> rejectionSources)
    {
        var projectedNotional = (result.CurrentNotionalExposurePercent ?? 0m)
            + (result.Sizing.NotionalExposurePercent ?? 0m);
        var projectedMargin = (result.CurrentMarginUsagePercent ?? 0m)
            + (result.Sizing.MarginUsagePercent ?? 0m);
        var projectedConcurrent = (result.ConcurrentRiskPercent ?? 0m);
        if (result.Sizing.RiskAmount > 0 && result.CurrentMarginUsagePercent is not null)
        {
            // Concurrent risk already includes open; projected adds this candidate's risk-at-stop %.
            // Approximate using sizing risk vs assessment balance from result sizing.
        }

        return new PathPortfolioAssessmentDto
        {
            PortfolioPath = path.ToString(),
            AssessmentBalance = result.Sizing.RiskAmount > 0 && result.Sizing.RiskPerTradePercent > 0
                ? Math.Round(result.Sizing.RiskAmount / (result.Sizing.RiskPerTradePercent / 100m), 8)
                : 0m,
            RiskAmount = result.Sizing.RiskAmount,
            Quantity = result.Sizing.Quantity,
            PositionNotional = result.Sizing.PositionNotional,
            MinimumRequiredLeverage = result.Sizing.MinimumRequiredLeverage,
            AssessmentLeverage = result.Sizing.AssessmentLeverage,
            InitialMarginRequired = result.Sizing.InitialMarginRequired,
            CandidateMarginUsagePercent = result.Sizing.MarginUsagePercent,
            CandidateNotionalExposurePercent = result.Sizing.NotionalExposurePercent,
            CurrentNotionalExposurePercent = result.CurrentNotionalExposurePercent,
            CurrentMarginUsagePercent = result.CurrentMarginUsagePercent,
            ProjectedTotalNotionalExposurePercent = result.Sizing.NotionalExposurePercent.HasValue
                ? Math.Round(projectedNotional, 6)
                : null,
            ProjectedTotalMarginUsagePercent = result.Sizing.MarginUsagePercent.HasValue
                ? Math.Round(projectedMargin, 6)
                : null,
            CurrentConcurrentRiskPercent = result.ConcurrentRiskPercent,
            ProjectedConcurrentRiskPercent = result.ConcurrentRiskPercent,
            CurrentDailyLossUsagePercent = result.DailyLossUsagePercent,
            CurrentDrawdownPercent = result.CurrentDrawdownPercent,
            CurrentOpenPositionCount = result.ConcurrentPositionCount ?? 0,
            PortfolioRiskScore = result.PortfolioRiskScore,
            RiskScoreDecision = result.RiskScoreDecision,
            HardRuleComplianceDecision = result.HardRuleComplianceDecision,
            FinancialRiskDecision = result.FinancialRiskDecision,
            RiskReason = result.FinancialRiskReason,
            FailedRuleKeys = result.FailedRuleKeys,
            WarningRuleKeys = result.WarningRuleKeys,
            RuleResults = result.RuleResults.Where(r =>
                !string.Equals(r.Category, nameof(RiskRuleCategory.Policy), StringComparison.OrdinalIgnoreCase)).ToList(),
            EntryDecision = entryDecision,
            EntryDecisionReason = entryReason,
            RejectionSources = rejectionSources,
            EvaluatedAtUtc = DateTime.UtcNow,
            AssessmentVersion = IndependentPathsVersions.Current
        };
    }

    public static PathPortfolioAssessmentDto WithBalance(
        PathPortfolioAssessmentDto dto,
        decimal assessmentBalance,
        decimal? projectedConcurrentRisk) =>
        new()
        {
            PortfolioPath = dto.PortfolioPath,
            AssessmentBalance = assessmentBalance,
            RiskAmount = dto.RiskAmount,
            Quantity = dto.Quantity,
            PositionNotional = dto.PositionNotional,
            MinimumRequiredLeverage = dto.MinimumRequiredLeverage,
            AssessmentLeverage = dto.AssessmentLeverage,
            InitialMarginRequired = dto.InitialMarginRequired,
            CandidateMarginUsagePercent = dto.CandidateMarginUsagePercent,
            CandidateNotionalExposurePercent = dto.CandidateNotionalExposurePercent,
            CurrentNotionalExposurePercent = dto.CurrentNotionalExposurePercent,
            CurrentMarginUsagePercent = dto.CurrentMarginUsagePercent,
            ProjectedTotalNotionalExposurePercent = dto.ProjectedTotalNotionalExposurePercent,
            ProjectedTotalMarginUsagePercent = dto.ProjectedTotalMarginUsagePercent,
            CurrentConcurrentRiskPercent = dto.CurrentConcurrentRiskPercent,
            ProjectedConcurrentRiskPercent = projectedConcurrentRisk ?? dto.ProjectedConcurrentRiskPercent,
            CurrentDailyLossUsagePercent = dto.CurrentDailyLossUsagePercent,
            CurrentDrawdownPercent = dto.CurrentDrawdownPercent,
            CurrentOpenPositionCount = dto.CurrentOpenPositionCount,
            PortfolioRiskScore = dto.PortfolioRiskScore,
            RiskScoreDecision = dto.RiskScoreDecision,
            HardRuleComplianceDecision = dto.HardRuleComplianceDecision,
            FinancialRiskDecision = dto.FinancialRiskDecision,
            RiskReason = dto.RiskReason,
            FailedRuleKeys = dto.FailedRuleKeys,
            WarningRuleKeys = dto.WarningRuleKeys,
            RuleResults = dto.RuleResults,
            EntryDecision = dto.EntryDecision,
            EntryDecisionReason = dto.EntryDecisionReason,
            RejectionSources = dto.RejectionSources,
            EvaluatedAtUtc = dto.EvaluatedAtUtc,
            AssessmentVersion = dto.AssessmentVersion
        };
}
