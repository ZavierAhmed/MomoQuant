using System.Text.Json;
using System.Text.Json.Serialization;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.StrategyLab.Risk;

public static class RiskObservationVersions
{
    public const string Legacy = "Legacy";
    public const string Current = "RiskObservation/v2.1";
    public const string CandidateRiskModel = "CandidateRiskQuality/v1.1";
    public const string PortfolioRiskModel = "PortfolioRiskQuality/v1";
}

public static class FinalPipelineRejectionSources
{
    public const string Confidence = "Confidence";
    public const string FinancialRisk = "FinancialRisk";
    public const string RiskPolicy = "RiskPolicy";
    public const string SimulationInvalid = "SimulationInvalid";
}

public static class RiskProfileSources
{
    public const string Saved = "Saved";
    public const string Custom = "Custom";
}

public enum RiskRuleResultStatus
{
    Passed = 1,
    Failed = 2,
    Warning = 3,
    NotApplicable = 4,
    NotAvailable = 5
}

public enum RiskRuleSeverity
{
    Info = 1,
    Warning = 2,
    HardReject = 3
}

public enum RiskRuleCategory
{
    Financial = 1,
    Policy = 2,
    Portfolio = 3,
    Sizing = 4
}

public sealed class RiskRuleResultDto
{
    public string RuleKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string Category { get; init; } = nameof(RiskRuleCategory.Financial);
    public string Status { get; init; } = nameof(RiskRuleResultStatus.NotApplicable);
    public string Severity { get; init; } = nameof(RiskRuleSeverity.Info);
    public decimal? ActualValue { get; init; }
    public decimal? LimitValue { get; init; }
    public string? Unit { get; init; }
    public decimal? ScoreContribution { get; init; }
    public decimal? MaxScoreContribution { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record CandidateRiskScoreComponent
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public decimal Score { get; init; }
    public decimal Max { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class CandidateRiskQualityResult
{
    public decimal Score { get; init; }
    public string ModelVersion { get; init; } = RiskObservationVersions.CandidateRiskModel;
    public IReadOnlyList<CandidateRiskScoreComponent> Components { get; init; } = [];
    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Immutable risk profile snapshot for Strategy Lab observation.
/// Nullable max-* percent limits mean the rule is disabled. Zero is a hard zero limit (not disabled).
/// </summary>
public sealed class RiskProfileSnapshotDto
{
    public long? RiskProfileId { get; init; }
    public string RiskProfileName { get; init; } = "Custom / Unknown";
    public string RiskProfileSource { get; init; } = RiskProfileSources.Custom;
    public string RiskProfileSnapshotId { get; init; } = string.Empty;
    public string RiskProfileVersion { get; init; } = RiskObservationVersions.Current;
    public ExposureSemanticsVersion ExposureSemanticsVersion { get; init; } = ExposureSemanticsVersion.ExplicitFuturesExposureV2;
    public DrawdownCalculationMode DrawdownCalculationMode { get; init; } = DrawdownCalculationMode.RealizedOnly;

    public decimal RiskPerTradePercent { get; init; }
    public decimal? PreferredLeverage { get; init; }
    public decimal MaxLeverage { get; init; } = 10m;

    public decimal? MaxNotionalExposurePerSymbolPercent { get; init; }
    public decimal? MaxTotalNotionalExposurePercent { get; init; }
    public decimal? MaxMarginUsagePerSymbolPercent { get; init; }
    public decimal? MaxTotalMarginUsagePercent { get; init; }
    public decimal? MaxConcurrentRiskPercent { get; init; }

    /// <summary>Legacy ambiguous exposure fields preserved for migration; never auto-enforced as notional or margin.</summary>
    public decimal? LegacyMaxExposurePerSymbolPercent { get; init; }
    public decimal? LegacyMaxTotalExposurePercent { get; init; }

    public decimal MaxDailyLossPercent { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public int MaxConcurrentPositions { get; init; }
    public decimal MinimumRewardRisk { get; init; }
    public decimal? FeeEfficiencyHardLimitPercent { get; init; } = 80m;
    public decimal? PolicyMinimumConfidence { get; init; }
    public decimal ObservationalRiskScoreThreshold { get; init; } = 50m;
    public IReadOnlyDictionary<string, string> ActiveRules { get; init; } = new Dictionary<string, string>();

    public static RiskProfileSnapshotDto FromSavedProfile(
        long? profileId,
        string profileName,
        RiskRuleSet rules,
        decimal maxLeverage,
        decimal? policyMinConfidence,
        decimal observationalThreshold,
        IReadOnlyList<RiskRule> rawRules,
        ExposureSemanticsVersion semantics,
        decimal? preferredLeverage = null,
        decimal? maxNotionalPerSymbol = null,
        decimal? maxTotalNotional = null,
        decimal? maxMarginPerSymbol = null,
        decimal? maxTotalMargin = null,
        decimal? maxConcurrentRisk = null)
    {
        var active = rawRules
            .Where(r => r.IsEnabled)
            .ToDictionary(r => r.RuleKey, r => r.RuleValue, StringComparer.OrdinalIgnoreCase);

        var snapshotId = $"saved-{profileId ?? 0}-{Guid.NewGuid():N}";

        return new RiskProfileSnapshotDto
        {
            RiskProfileId = profileId,
            RiskProfileName = profileName,
            RiskProfileSource = RiskProfileSources.Saved,
            RiskProfileSnapshotId = snapshotId,
            RiskProfileVersion = RiskObservationVersions.Current,
            ExposureSemanticsVersion = semantics,
            RiskPerTradePercent = rules.MaxRiskPerTradePercent,
            PreferredLeverage = preferredLeverage,
            MaxLeverage = maxLeverage,
            MaxNotionalExposurePerSymbolPercent = maxNotionalPerSymbol,
            MaxTotalNotionalExposurePercent = maxTotalNotional,
            MaxMarginUsagePerSymbolPercent = maxMarginPerSymbol,
            MaxTotalMarginUsagePercent = maxTotalMargin,
            MaxConcurrentRiskPercent = maxConcurrentRisk,
            LegacyMaxExposurePerSymbolPercent = rules.MaxExposurePerSymbolPercent,
            LegacyMaxTotalExposurePercent = rules.MaxTotalExposurePercent,
            MaxDailyLossPercent = rules.MaxDailyLossPercent,
            MaxDrawdownPercent = rules.MaxWeeklyLossPercent,
            MaxConcurrentPositions = rules.MaxOpenPositions,
            MinimumRewardRisk = rules.MinRewardRiskRatio,
            PolicyMinimumConfidence = policyMinConfidence ?? (rules.MinConfidenceScore > 0 ? rules.MinConfidenceScore : null),
            ObservationalRiskScoreThreshold = observationalThreshold,
            ActiveRules = active
        };
    }

    /// <summary>Legacy helper kept for older call sites; marks exposure as LegacyAmbiguous.</summary>
    public static RiskProfileSnapshotDto FromRules(
        long? profileId,
        string profileName,
        RiskRuleSet rules,
        decimal maxLeverage,
        decimal? policyMinConfidence,
        decimal observationalThreshold,
        IReadOnlyList<RiskRule> rawRules) =>
        FromSavedProfile(
            profileId,
            profileName,
            rules,
            maxLeverage,
            policyMinConfidence,
            observationalThreshold,
            rawRules,
            ExposureSemanticsVersion.LegacyAmbiguous);
}

public sealed class PositionSizingObservation
{
    public decimal EntryPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal StopDistanceAbsolute { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal RiskPerTradePercent { get; init; }
    public decimal RiskAmount { get; init; }
    public decimal RiskAtStopPercent { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? PositionNotional { get; init; }
    public decimal? NotionalExposurePercent { get; init; }
    public decimal? MinimumRequiredLeverage { get; init; }
    public decimal? AssessmentLeverage { get; init; }
    public decimal? PreferredLeverage { get; init; }
    public decimal? MaxLeverage { get; init; }
    public decimal? InitialMarginRequired { get; init; }
    public decimal? MarginUsagePercent { get; init; }

    /// <summary>Backward-compatible alias for MinimumRequiredLeverage.</summary>
    public decimal? RequiredLeverage => MinimumRequiredLeverage;

    /// <summary>Backward-compatible alias for NotionalExposurePercent.</summary>
    public decimal? PositionExposurePercent => NotionalExposurePercent;

    public decimal EstimatedEntryFee { get; init; }
    public decimal EstimatedExitFee { get; init; }
    public decimal EstimatedRoundTripFees { get; init; }
    public decimal TargetGrossProfit { get; init; }
    public decimal TargetNetProfitEstimate { get; init; }
    public decimal? FeeToTargetPercent { get; init; }
    public string? UnavailableReason { get; init; }
}

/// <summary>Legacy mutable shadow state retained for older tests; prefer ChronologicalShadowPortfolio.</summary>
public sealed class ShadowPortfolioState
{
    public decimal InitialBalance { get; init; }
    public decimal CurrentBalance { get; set; }
    public decimal PeakEquity { get; set; }
    public decimal CurrentExposureNotional { get; set; }
    public decimal ConcurrentRiskAtStop { get; set; }
    public decimal DailyRealizedPnl { get; set; }
    public DateTime? DailyBucketUtc { get; set; }
    public int ConcurrentPositionCount { get; set; }
    public List<ShadowOpenPosition> OpenPositions { get; } = [];

    public decimal CurrentEquity => CurrentBalance;
    public decimal? CurrentDrawdownPercent =>
        PeakEquity > 0 ? Math.Round((PeakEquity - CurrentEquity) / PeakEquity * 100m, 4) : null;

    public decimal? CurrentExposurePercent =>
        CurrentBalance > 0 ? Math.Round(CurrentExposureNotional / CurrentBalance * 100m, 4) : null;

    public decimal? ConcurrentRiskPercent =>
        CurrentBalance > 0 ? Math.Round(ConcurrentRiskAtStop / CurrentBalance * 100m, 4) : null;

    public decimal? DailyLossUsagePercent =>
        CurrentBalance > 0 && DailyRealizedPnl < 0
            ? Math.Round(Math.Abs(DailyRealizedPnl) / InitialBalance * 100m, 4)
            : 0m;

    public static ShadowPortfolioState Create(decimal initialBalance) => new()
    {
        InitialBalance = initialBalance,
        CurrentBalance = initialBalance,
        PeakEquity = initialBalance
    };
}

public sealed class ShadowOpenPosition
{
    public string Fingerprint { get; init; } = string.Empty;
    public decimal Notional { get; init; }
    public decimal RiskAmount { get; init; }
    public DateTime OpenedAtUtc { get; init; }
}

public sealed class StrategyLabRiskObservationResult
{
    public ResearchRiskDecision FinancialRiskDecision { get; init; }
    public string FinancialRiskReason { get; init; } = string.Empty;
    public ResearchRiskScoreDecision RiskScoreDecision { get; init; } = ResearchRiskScoreDecision.NotEvaluated;
    public ResearchHardRuleComplianceDecision HardRuleComplianceDecision { get; init; } =
        ResearchHardRuleComplianceDecision.NotEvaluated;
    public decimal CandidateRiskScore { get; init; }
    public decimal RiskThreshold { get; init; }
    public decimal RiskMargin { get; init; }
    public string RiskModelVersion { get; init; } = RiskObservationVersions.CandidateRiskModel;
    public string RiskAssessmentVersion { get; init; } = RiskObservationVersions.Current;
    public IReadOnlyList<RiskRuleResultDto> RuleResults { get; init; } = [];
    public IReadOnlyList<string> FailedRuleKeys { get; init; } = [];
    public IReadOnlyList<string> WarningRuleKeys { get; init; } = [];
    public CandidateRiskQualityResult ScoreBreakdown { get; init; } = new();
    public PositionSizingObservation Sizing { get; init; } = new();
    public ResearchRiskPolicyEligibilityDecision PolicyDecision { get; init; }
    public string? PolicyReason { get; init; }
    public IReadOnlyList<string> PolicyFailedRuleKeys { get; init; } = [];
    public decimal? PolicyMinimumConfidence { get; init; }
    public decimal? PortfolioRiskScore { get; init; }
    public PortfolioRiskAssessmentStatus PortfolioAssessmentStatus { get; init; }
    public decimal? CurrentNotionalExposurePercent { get; init; }
    public decimal? CurrentMarginUsagePercent { get; init; }
    public decimal? CurrentExposurePercent { get; init; }
    public decimal? ConcurrentRiskPercent { get; init; }
    public decimal? DailyLossUsagePercent { get; init; }
    public decimal? CurrentDrawdownPercent { get; init; }
    public int? ConcurrentPositionCount { get; init; }
    public DrawdownCalculationMode DrawdownCalculationMode { get; init; } = DrawdownCalculationMode.RealizedOnly;
    public IReadOnlyList<string> FinalPipelineRejectionSources { get; init; } = [];
    public string? RiskProfileName { get; init; }
    public string? RiskProfileSource { get; init; }
    public string? RiskProfileSnapshotId { get; init; }
}

public sealed class ShadowPortfolioSummaryDto
{
    public string PathName { get; init; } = string.Empty;
    public decimal StartingBalance { get; init; }
    public decimal EndingBalance { get; init; }
    public decimal GrossPnl { get; init; }
    public decimal GrossReturnPercent { get; init; }
    public decimal EntryFees { get; init; }
    public decimal ExitFees { get; init; }
    public decimal SlippageCost { get; init; }
    public decimal FundingCost { get; init; }
    public decimal TotalTransactionCosts { get; init; }
    public decimal RealizedNetPnl { get; init; }
    public decimal NetReturnPercent { get; init; }
    public decimal NetReturnAfterCostsPercent { get; init; }
    public int TradesAccepted { get; init; }
    public int TradesRejected { get; init; }
    public int TradesOpened { get; init; }
    public int ProfitableTrades { get; init; }
    public int LosingTrades { get; init; }
    public int BreakevenTrades { get; init; }
    public decimal MaxRealizedDrawdownPercent { get; init; }
    public decimal PeakMarginUsagePercent { get; init; }
    public decimal PeakNotionalExposurePercent { get; init; }
    public decimal PeakConcurrentRiskPercent { get; init; }
    public int PeakOpenPositionCount { get; init; }
    public DrawdownCalculationMode DrawdownCalculationMode { get; init; } = DrawdownCalculationMode.RealizedOnly;
    public string CostModelVersion { get; init; } = StrategyLabCostModelVersions.V1;
    public IReadOnlyList<ShadowTradeLedgerEntry> Ledger { get; init; } = [];
    public IReadOnlyList<ShadowPortfolioAuditEvent> AuditEvents { get; init; } = [];
}

public static class RiskObservationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
