using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationQualificationProfile
{
    public string ProfileVersion { get; init; } = "StandardHoldoutQualification/v1";
    public ValidationPrimaryQualificationLayer PrimaryQualificationLayer { get; init; } =
        ValidationPrimaryQualificationLayer.RawStrategy;
    public int MinimumTrainingClosedTrades { get; init; } = 30;
    public int MinimumValidationClosedTrades { get; init; } = 15;
    public decimal MinimumTrainingProfitFactor { get; init; } = 1.10m;
    public decimal MinimumValidationProfitFactor { get; init; } = 1.05m;
    public decimal MinimumTrainingNetExpectancyR { get; init; }
    public decimal MinimumValidationNetExpectancyR { get; init; }
    public decimal MaximumTrainingDrawdownPercent { get; init; } = 25m;
    public decimal MaximumValidationDrawdownPercent { get; init; } = 25m;
    public decimal MinimumOpportunityRetentionPercent { get; init; } = 40m;
    public decimal MaximumAllowedExpectancyDegradation { get; init; } = 0.50m;
    public decimal MaximumSingleTradePnlContributionPercent { get; init; } = 40m;
    public bool RequirePositiveValidationNetPnl { get; init; } = true;
    public bool RequirePositiveValidationNetExpectancy { get; init; } = true;
    public bool RequireParameterStability { get; init; } = true;
    public ExpectancyMetricType ExpectancyMetric { get; init; } = ExpectancyMetricType.NetExpectancyR;
    public ProfitFactorMetricType ProfitFactorMetric { get; init; } = ProfitFactorMetricType.NetProfitFactor;
    public ExpiredTradeMetricPolicy ExpiredTradeMetricPolicy { get; init; } =
        ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry;

    public static ValidationQualificationProfile StandardDefault() => new();
}

/// <summary>
/// Segment/layer metrics. ValidationMetrics/v1.1 separates Gross* from Net*.
/// Legacy ValidationMetrics/v1 incorrectly set NetExpectancyR = AverageR (gross).
/// </summary>
public sealed class LayerSegmentMetrics
{
    public int CandleCount { get; init; }
    public int CandidateCount { get; init; }
    public decimal OpportunityRatePer1000Candles { get; init; }
    public int ClosedTradeCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public int ExpiredCount { get; init; }
    public int ExpiredIncludedInClosedCount { get; init; }
    public int ExpiredExcludedCount { get; init; }
    public decimal? WinRate { get; init; }
    /// <summary>Legacy alias for GrossAverageR.</summary>
    public decimal? AverageR { get; init; }
    public decimal? MedianR { get; init; }
    public decimal? GrossAverageR { get; init; }
    public decimal? GrossMedianR { get; init; }
    public decimal? NetAverageR { get; init; }
    public decimal? NetMedianR { get; init; }
    public decimal? GrossExpectancyR { get; init; }
    public decimal? NetExpectancyR { get; init; }
    public decimal? GrossPnl { get; init; }
    public decimal? TransactionCosts { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? NetReturnPercent { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? GrossLoss { get; init; }
    public decimal? NetProfit { get; init; }
    public decimal? NetLoss { get; init; }
    public decimal? GrossProfitFactor { get; init; }
    public decimal? NetProfitFactor { get; init; }
    /// <summary>Legacy alias — prefer NetProfitFactor under v1.1.</summary>
    public decimal? ProfitFactor { get; init; }
    public ProfitFactorStatus? ProfitFactorStatus { get; init; }
    public ProfitFactorStatus? GrossProfitFactorStatus { get; init; }
    public ProfitFactorStatus? NetProfitFactorStatus { get; init; }
    public decimal? MaximumRealizedDrawdownPercent { get; init; }
    public decimal? FeeToGrossProfitPercent { get; init; }
    public int BoundaryCensoredCount { get; init; }
    public int PersistedCandidateRowCount { get; set; }
    public int MetricIncludedCandidateCount { get; set; }
    public int MetricExcludedCandidateCount { get; set; }
    public int CrossSegmentOverlapCount { get; set; }
    public int BreakevenCount { get; set; }
    public string? IncludedOutcomeTypes { get; init; }
    public string? PnlSizingMode { get; init; }
    public string? ExpectancyCalculationMode { get; init; }
    public string? ProfitFactorCalculationMode { get; init; }
    public string? CostModelVersion { get; init; }
    public string MetricsVersion { get; set; } = ValidationMetricsContract.VersionV12;
    public string? RiskBasisVersion { get; set; }
    public ValidationRiskBasisType? RiskBasisType { get; set; }
    public ValidationRiskBasisValidationStatus? RiskBasisValidationStatus { get; set; }
    public ValidationMetricApplicability? NetExpectancyApplicability { get; set; }
    public int? NetExpectancyIncludedTradeCount { get; set; }
    public int? NetExpectancyExcludedTradeCount { get; set; }
    public IReadOnlyList<string>? NetExpectancyExclusionReasons { get; set; }
}

public sealed class RobustnessVerdictResult
{
    public StrategyRobustnessDecision Decision { get; init; }
    public string? PrimaryFailureReason { get; init; }
    public IReadOnlyList<string> FailureReasons { get; init; } = [];
    public IReadOnlyList<string> RuleResults { get; init; } = [];
    public IReadOnlyList<QualificationRuleResult> StructuredRuleResults { get; init; } = [];
    public string Explanation { get; init; } = string.Empty;
    public bool PerformanceCollapseDetected { get; init; }
}

public sealed class QualificationRuleResult
{
    public string RuleKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string? Segment { get; init; }
    public string? Layer { get; init; }
    public string? MetricKey { get; init; }
    public string? ActualValue { get; init; }
    public string? LimitValue { get; init; }
    public string? Unit { get; init; }
    public string? ComparisonOperator { get; init; }
    public QualificationRuleStatus Status { get; init; }
    public QualificationRuleApplicability Applicability { get; init; } =
        QualificationRuleApplicability.Applicable;
    public string Reason { get; init; } = string.Empty;
    public string MetricVersion { get; init; } = ValidationMetricsContract.VersionV11;
}
