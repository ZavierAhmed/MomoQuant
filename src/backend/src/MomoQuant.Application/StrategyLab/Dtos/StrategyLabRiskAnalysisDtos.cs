namespace MomoQuant.Application.StrategyLab.Dtos;

public sealed class StrategyLabRiskAnalysisDto
{
    public string RiskAssessmentVersion { get; init; } = "Legacy";
    public StrategyLabFinancialRiskSummaryDto FinancialRiskSummary { get; init; } = new();
    public ScoreDistributionDiagnosticsDto? CandidateRiskScoreDistribution { get; init; }
    public StrategyLabWinnerLoserRiskDto WinnerLoserRiskComparison { get; init; } = new();
    public StrategyLabRejectedRiskSubsetDto RejectedWinnerAnalysis { get; init; } = new();
    public StrategyLabRejectedRiskSubsetDto RejectedLoserAnalysis { get; init; } = new();
    public IReadOnlyList<StrategyLabRiskRuleEffectivenessDto> RuleEffectiveness { get; init; } = [];
    public IReadOnlyList<StrategyLabRiskScoreBucketDto> RiskScoreBuckets { get; init; } = [];
    public StrategyLabRiskPolicySummaryDto RiskPolicySummary { get; init; } = new();
    public StrategyLabPortfolioRiskSummaryDto PortfolioRiskSummary { get; init; } = new();
    public StrategyLabExposureAnalyticsDto ExposureAnalytics { get; init; } = new();
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public sealed class StrategyLabExposureAnalyticsDto
{
    public decimal? AverageNotionalExposurePercent { get; init; }
    public decimal? MedianNotionalExposurePercent { get; init; }
    public decimal? AverageMarginUsagePercent { get; init; }
    public decimal? MedianMarginUsagePercent { get; init; }
    public decimal? AverageMinimumRequiredLeverage { get; init; }
    public decimal? AverageAssessmentLeverage { get; init; }
    public decimal? AverageConcurrentRiskPercent { get; init; }
}

public sealed class StrategyLabFinancialRiskSummaryDto
{
    public int EvaluatedCandidateCount { get; init; }
    public int ApprovedCount { get; init; }
    public int RejectedCount { get; init; }
    public decimal ApprovalRate { get; init; }
    public decimal? AverageCandidateRiskScore { get; init; }
    public decimal? MedianCandidateRiskScore { get; init; }
    public decimal? MinimumCandidateRiskScore { get; init; }
    public decimal? MaximumCandidateRiskScore { get; init; }
    public decimal? StandardDeviation { get; init; }
    public int UniqueScoreCount { get; init; }
}

public sealed class StrategyLabWinnerLoserRiskDto
{
    public decimal? AverageWinnerRiskScore { get; init; }
    public decimal? MedianWinnerRiskScore { get; init; }
    public decimal? AverageLoserRiskScore { get; init; }
    public decimal? MedianLoserRiskScore { get; init; }
    public decimal? RiskScoreSeparation { get; init; }
}

public sealed class StrategyLabRejectedRiskSubsetDto
{
    public int Count { get; init; }
    public decimal PercentageOfOutcomeGroup { get; init; }
    public decimal? AverageRiskScore { get; init; }
    public decimal HypotheticalNetPnl { get; init; }
    public decimal? AverageR { get; init; }
    public IReadOnlyList<string> TopRejectionReasons { get; init; } = [];
}

public sealed class StrategyLabRiskRuleEffectivenessDto
{
    public string RuleKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public int EvaluatedCount { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int WarningCount { get; set; }
    public int RejectedWinners { get; set; }
    public int RejectedLosers { get; set; }
    public decimal RejectedWinnerPercent { get; init; }
    public decimal RejectedLoserPercent { get; init; }
    public decimal HypotheticalPnlOfRejected { get; set; }
}

public sealed class StrategyLabRiskScoreBucketDto
{
    public string Label { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public int ExpiredCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal NetRawPnl { get; init; }
    public decimal? AverageR { get; init; }
    public decimal? AverageStopDistancePercent { get; init; }
    public decimal? AverageLeverage { get; init; }
    public decimal? AverageExposure { get; init; }
}

public sealed class StrategyLabRiskPolicySummaryDto
{
    public int EvaluatedCount { get; init; }
    public int EligibleCount { get; init; }
    public int IneligibleCount { get; init; }
    public IReadOnlyList<string> TopPolicyReasons { get; init; } = [];
}

public sealed class StrategyLabPortfolioRiskSummaryDto
{
    public string Status { get; init; } = "Unavailable";
    public string? Note { get; init; }
}

public sealed class StrategyLabRiskProfileComparisonDto
{
    public bool Comparable { get; init; }
    public IReadOnlyList<string> IncompatibilityReasons { get; init; } = [];
    public StrategyLabRiskProfileSideSummaryDto? ProfileA { get; init; }
    public StrategyLabRiskProfileSideSummaryDto? ProfileB { get; init; }
    public IReadOnlyList<StrategyLabCandidateRiskDecisionDifferenceDto> CandidateDecisionDifferences { get; init; } = [];
    public IReadOnlyList<string> Summary { get; init; } = [];
}

public sealed class StrategyLabRiskProfileSideSummaryDto
{
    public long? RiskProfileId { get; init; }
    public string? ProfileName { get; init; }
    public decimal? RiskPerTradePercent { get; init; }
    public decimal? MaxLeverage { get; init; }
    public int FinancialRiskApproved { get; init; }
    public int FinancialRiskRejected { get; init; }
    public int RiskPolicyRejected { get; init; }
    public int WinnersApproved { get; init; }
    public int WinnersRejected { get; init; }
    public int LosersApproved { get; init; }
    public int LosersRejected { get; init; }
    public decimal? AverageRiskScore { get; init; }
    public decimal? AverageRequiredLeverage { get; init; }
    public decimal? AverageExposure { get; init; }
}

public sealed class StrategyLabCandidateRiskDecisionDifferenceDto
{
    public string SetupFingerprint { get; init; } = string.Empty;
    public string? ProfileADecision { get; init; }
    public string? ProfileBDecision { get; init; }
    public string? ProfileAFailedRules { get; init; }
    public string? ProfileBFailedRules { get; init; }
    public decimal? ProfileAPositionSize { get; init; }
    public decimal? ProfileBPositionSize { get; init; }
    public decimal? ProfileALeverage { get; init; }
    public decimal? ProfileBLeverage { get; init; }
}
