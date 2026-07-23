using MomoQuant.Application.Risk;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab.Risk;

/// <summary>
/// Strategy Lab financial risk + policy observation (does not short-circuit).
/// Uses explicit futures quantities: risk-at-stop, notional exposure, margin usage, leverage variants.
/// FinancialRiskDecision = Approved only when RiskScoreDecision=Passed AND HardRuleCompliance=Compliant.
/// </summary>
public sealed class StrategyLabRiskObserver
{
    private readonly CandidateRiskQualityScorer _scorer = new();

    public StrategyLabRiskObservationResult Evaluate(
        StrategyResearchCandidate candidate,
        RiskProfileSnapshotDto snapshot,
        RiskRuleSet rules,
        ChronologicalShadowPortfolio shadow,
        decimal? observedConfidenceScore,
        decimal assessmentBalance,
        StrategyLabCostSnapshot costSnapshot,
        ResearchConfidenceDecision confidenceDecision)
    {
        var sizing = BuildSizing(candidate, snapshot, assessmentBalance, costSnapshot);
        var financialResults = EvaluateFinancialRules(candidate, snapshot, rules, sizing, shadow);
        var policy = EvaluatePolicy(observedConfidenceScore, snapshot);

        var hardFails = financialResults
            .Where(r => r.Status == nameof(RiskRuleResultStatus.Failed)
                        && r.Severity == nameof(RiskRuleSeverity.HardReject))
            .ToList();
        var warnings = financialResults
            .Where(r => r.Status == nameof(RiskRuleResultStatus.Warning)
                        || (r.Status == nameof(RiskRuleResultStatus.Failed)
                            && r.Severity == nameof(RiskRuleSeverity.Warning)))
            .Select(r => r.RuleKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hardCompliance = hardFails.Count == 0 && sizing.Quantity is > 0
            ? ResearchHardRuleComplianceDecision.Compliant
            : ResearchHardRuleComplianceDecision.NonCompliant;

        var score = _scorer.Score(candidate, sizing, snapshot, financialResults, shadow);
        var scoreDecision = RiskLimitComparison.MeetsInclusiveMinimum(score.Score, snapshot.ObservationalRiskScoreThreshold)
            ? ResearchRiskScoreDecision.Passed
            : ResearchRiskScoreDecision.Failed;

        var financialApproved = scoreDecision == ResearchRiskScoreDecision.Passed
            && hardCompliance == ResearchHardRuleComplianceDecision.Compliant;

        var financialReason = financialApproved
            ? "Financial risk score passed and hard rules are compliant."
            : hardCompliance == ResearchHardRuleComplianceDecision.NonCompliant
                ? (hardFails.Count > 0
                    ? string.Join("; ", hardFails.Select(f => f.Reason))
                    : sizing.UnavailableReason ?? "Hard financial rules failed.")
                : $"Candidate risk score {score.Score:0.##} is below threshold {snapshot.ObservationalRiskScoreThreshold:0.##}.";

        decimal? portfolioScore = null;
        var portfolioStatus = PortfolioRiskAssessmentStatus.Evaluated;
        if (assessmentBalance <= 0)
        {
            portfolioStatus = PortfolioRiskAssessmentStatus.Unavailable;
        }
        else
        {
            portfolioScore = shadow.PortfolioRiskScore(snapshot);
        }

        var sources = new List<string>();
        if (confidenceDecision == ResearchConfidenceDecision.Rejected)
        {
            sources.Add(FinalPipelineRejectionSources.Confidence);
        }

        if (!financialApproved)
        {
            sources.Add(FinalPipelineRejectionSources.FinancialRisk);
        }

        if (policy.Decision == ResearchRiskPolicyEligibilityDecision.Ineligible)
        {
            sources.Add(FinalPipelineRejectionSources.RiskPolicy);
        }

        if (candidate.CandidateStatus == StrategyResearchCandidateStatus.SimulationInvalid)
        {
            sources.Add(FinalPipelineRejectionSources.SimulationInvalid);
        }

        return new StrategyLabRiskObservationResult
        {
            FinancialRiskDecision = financialApproved ? ResearchRiskDecision.Approved : ResearchRiskDecision.Rejected,
            FinancialRiskReason = financialReason,
            RiskScoreDecision = scoreDecision,
            HardRuleComplianceDecision = hardCompliance,
            CandidateRiskScore = score.Score,
            RiskThreshold = snapshot.ObservationalRiskScoreThreshold,
            RiskMargin = score.Score - snapshot.ObservationalRiskScoreThreshold,
            RiskModelVersion = CandidateRiskQualityScorer.ModelVersion,
            RiskAssessmentVersion = RiskObservationVersions.Current,
            RuleResults = financialResults.Concat(policy.RuleResults).ToList(),
            FailedRuleKeys = hardFails.Select(f => f.RuleKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WarningRuleKeys = warnings,
            ScoreBreakdown = score,
            Sizing = sizing,
            PolicyDecision = policy.Decision,
            PolicyReason = policy.Reason,
            PolicyFailedRuleKeys = policy.FailedKeys,
            PolicyMinimumConfidence = snapshot.PolicyMinimumConfidence,
            PortfolioRiskScore = portfolioScore,
            PortfolioAssessmentStatus = portfolioStatus,
            CurrentNotionalExposurePercent = shadow.CurrentNotionalExposurePercent,
            CurrentMarginUsagePercent = shadow.CurrentMarginUsagePercent,
            CurrentExposurePercent = shadow.CurrentNotionalExposurePercent,
            ConcurrentRiskPercent = shadow.ConcurrentRiskPercent,
            DailyLossUsagePercent = shadow.DailyLossUsagePercent,
            CurrentDrawdownPercent = shadow.CurrentDrawdownPercent,
            ConcurrentPositionCount = shadow.OpenPositions.Count,
            DrawdownCalculationMode = DrawdownCalculationMode.RealizedOnly,
            FinalPipelineRejectionSources = sources,
            RiskProfileName = snapshot.RiskProfileName,
            RiskProfileSource = snapshot.RiskProfileSource,
            RiskProfileSnapshotId = snapshot.RiskProfileSnapshotId
        };
    }

    /// <summary>Backward-compatible overload using taker fee only (0 slippage).</summary>
    public StrategyLabRiskObservationResult Evaluate(
        StrategyResearchCandidate candidate,
        RiskProfileSnapshotDto snapshot,
        RiskRuleSet rules,
        ChronologicalShadowPortfolio shadow,
        decimal? observedConfidenceScore,
        decimal assessmentBalance,
        decimal takerFeeRate,
        ResearchConfidenceDecision confidenceDecision) =>
        Evaluate(
            candidate,
            snapshot,
            rules,
            shadow,
            observedConfidenceScore,
            assessmentBalance,
            StrategyLabCostSnapshot.CreateDefault(makerFeeRate: takerFeeRate * 0.5m, takerFeeRate: takerFeeRate),
            confidenceDecision);

    public static void ApplyToCandidate(
        StrategyResearchCandidate candidate,
        StrategyLabRiskObservationResult result,
        RiskProfileSnapshotDto snapshot)
    {
        candidate.RiskDecision = result.FinancialRiskDecision;
        candidate.RiskReason = result.FinancialRiskReason;
        candidate.RiskScore = result.CandidateRiskScore;
        candidate.CandidateRiskScore = result.CandidateRiskScore;
        candidate.PortfolioRiskScore = result.PortfolioRiskScore;
        candidate.PortfolioRiskAssessmentStatus = result.PortfolioAssessmentStatus;
        candidate.RiskThreshold = result.RiskThreshold;
        candidate.RiskMargin = result.RiskMargin;
        candidate.RiskModelVersion = result.RiskModelVersion;
        candidate.RiskAssessmentVersion = result.RiskAssessmentVersion;
        candidate.RiskComponentsJson = RiskObservationJson.Serialize(result.ScoreBreakdown.Components);
        candidate.RiskRuleResultsJson = RiskObservationJson.Serialize(result.RuleResults);
        candidate.RiskFailedRuleKeysJson = RiskObservationJson.Serialize(result.FailedRuleKeys);
        candidate.RiskWarningRuleKeysJson = RiskObservationJson.Serialize(result.WarningRuleKeys);
        candidate.RiskRejectedRuleKey = result.FailedRuleKeys.FirstOrDefault();
        candidate.RiskEvaluatedAtUtc = DateTime.UtcNow;
        candidate.RiskProfileId = snapshot.RiskProfileId;
        candidate.RiskProfileVersion = snapshot.RiskProfileVersion;
        candidate.RiskProfileName = snapshot.RiskProfileName;
        candidate.RiskProfileSource = snapshot.RiskProfileSource;
        candidate.RiskProfileSnapshotId = snapshot.RiskProfileSnapshotId;
        candidate.RiskScoreDecision = result.RiskScoreDecision;
        candidate.HardRuleComplianceDecision = result.HardRuleComplianceDecision;
        candidate.DrawdownCalculationMode = result.DrawdownCalculationMode;

        candidate.RiskPerTradePercent = result.Sizing.RiskPerTradePercent;
        candidate.RiskAmount = result.Sizing.RiskAmount;
        candidate.RiskAtStopPercent = result.Sizing.RiskAtStopPercent;
        candidate.ProposedPositionSize = result.Sizing.Quantity;
        candidate.PositionNotional = result.Sizing.PositionNotional;
        candidate.ProposedLeverage = result.Sizing.MinimumRequiredLeverage;
        candidate.MinimumRequiredLeverage = result.Sizing.MinimumRequiredLeverage;
        candidate.AssessmentLeverage = result.Sizing.AssessmentLeverage;
        candidate.PreferredLeverage = result.Sizing.PreferredLeverage;
        candidate.MaxLeverage = result.Sizing.MaxLeverage;
        candidate.InitialMarginRequired = result.Sizing.InitialMarginRequired;
        candidate.NotionalExposurePercent = result.Sizing.NotionalExposurePercent;
        candidate.MarginUsagePercent = result.Sizing.MarginUsagePercent;
        candidate.StopDistancePercent = result.Sizing.StopDistancePercent;
        candidate.PositionExposurePercent = result.Sizing.NotionalExposurePercent;
        candidate.EstimatedRoundTripFees = result.Sizing.EstimatedRoundTripFees;
        candidate.FeeToTargetPercent = result.Sizing.FeeToTargetPercent;
        candidate.PositionSizingUnavailableReason = result.Sizing.UnavailableReason;

        candidate.CurrentExposurePercent = result.CurrentNotionalExposurePercent;
        candidate.CurrentNotionalExposurePercent = result.CurrentNotionalExposurePercent;
        candidate.CurrentMarginUsagePercent = result.CurrentMarginUsagePercent;
        candidate.ConcurrentRiskPercent = result.ConcurrentRiskPercent;
        candidate.DailyLossUsagePercent = result.DailyLossUsagePercent;
        candidate.CurrentDrawdownPercent = result.CurrentDrawdownPercent;
        candidate.ConcurrentPositionCount = result.ConcurrentPositionCount;

        candidate.RiskPolicyEligibilityDecision = result.PolicyDecision;
        candidate.RiskPolicyReason = result.PolicyReason;
        candidate.RiskPolicyFailedRuleKeysJson = RiskObservationJson.Serialize(result.PolicyFailedRuleKeys);
        candidate.RiskPolicyMinimumConfidence = result.PolicyMinimumConfidence;
        candidate.FinalPipelineRejectionSourcesJson = RiskObservationJson.Serialize(result.FinalPipelineRejectionSources);
    }

    public static ResearchFinalPipelineDecision ResolveFinalDecision(StrategyResearchCandidate candidate)
    {
        // Prefer explicit Full-Pipeline financial decision when IndependentPaths/v1 is present.
        if (candidate.FullPipelineFinancialRiskDecision is ResearchRiskDecision.Approved
            or ResearchRiskDecision.Rejected
            or ResearchRiskDecision.NotEvaluated)
        {
            return ResolveFinalDecisionFromFullPipeline(
                candidate.ConfidenceDecision,
                candidate.RiskPolicyEligibilityDecision,
                candidate.FullPipelineFinancialRiskDecision);
        }

        // Legacy fallback: generic RiskDecision (historically Risk-Only contaminated).
        return ResolveFinalDecisionFromFullPipeline(
            candidate.ConfidenceDecision,
            candidate.RiskPolicyEligibilityDecision,
            candidate.RiskDecision);
    }

    /// <summary>
    /// Final pipeline decision uses Full-Pipeline financial risk only — never Risk-Only state.
    /// </summary>
    public static ResearchFinalPipelineDecision ResolveFinalDecisionFromFullPipeline(
        ResearchConfidenceDecision? confidenceDecision,
        ResearchRiskPolicyEligibilityDecision? policyDecision,
        ResearchRiskDecision? fullPipelineFinancialRisk)
    {
        if (confidenceDecision is ResearchConfidenceDecision.NotEvaluated or null
            && fullPipelineFinancialRisk is ResearchRiskDecision.NotEvaluated or null)
        {
            return ResearchFinalPipelineDecision.RawOnly;
        }

        var confRejected = confidenceDecision == ResearchConfidenceDecision.Rejected;
        var financialRejected = fullPipelineFinancialRisk == ResearchRiskDecision.Rejected;
        var policyRejected = policyDecision == ResearchRiskPolicyEligibilityDecision.Ineligible;

        if (confRejected && financialRejected)
        {
            return ResearchFinalPipelineDecision.RejectedByBoth;
        }

        if (confRejected)
        {
            return ResearchFinalPipelineDecision.RejectedByConfidence;
        }

        if (financialRejected)
        {
            return ResearchFinalPipelineDecision.RejectedByRisk;
        }

        if (policyRejected)
        {
            return ResearchFinalPipelineDecision.RejectedByPolicy;
        }

        return ResearchFinalPipelineDecision.Approved;
    }

    public PositionSizingObservation BuildSizing(
        StrategyResearchCandidate candidate,
        RiskProfileSnapshotDto snapshot,
        decimal assessmentBalance,
        StrategyLabCostSnapshot costSnapshot)
    {
        var entryFeeRate = costSnapshot.EntryFeeRateUsed > 0
            ? costSnapshot.EntryFeeRateUsed
            : costSnapshot.ResolveFeeRate(costSnapshot.EntryOrderType);

        var calc = FuturesSizingCalculator.Calculate(
            candidate.ProposedEntryPrice,
            candidate.StopLoss,
            candidate.Target1,
            assessmentBalance,
            snapshot.RiskPerTradePercent,
            snapshot.MaxLeverage,
            snapshot.PreferredLeverage,
            entryFeeRate);

        decimal? feeToTarget = calc.FeeToTargetPercent;
        decimal targetGross = calc.TargetGrossProfit;
        decimal entryFee = calc.EstimatedEntryFee;
        decimal exitFee = calc.EstimatedExitFee;
        decimal roundTrip = calc.EstimatedRoundTripFees;
        decimal targetNet = calc.TargetNetProfitEstimate;

        if (calc.Quantity is > 0)
        {
            var feeEst = ShadowPositionCostCalculator.EstimateFeeToTarget(
                candidate.Direction,
                candidate.ProposedEntryPrice,
                candidate.Target1,
                calc.Quantity.Value,
                costSnapshot);
            feeToTarget = feeEst.FeeToTargetPercent;
            targetGross = feeEst.TargetGross;
            roundTrip = feeEst.ExpectedCosts;
            // Split expected costs roughly for display when using cost snapshot.
            entryFee = Math.Round(feeEst.ExpectedCosts / 2m, 8);
            exitFee = Math.Round(feeEst.ExpectedCosts - entryFee, 8);
            targetNet = targetGross - feeEst.ExpectedCosts;
        }

        return new PositionSizingObservation
        {
            EntryPrice = calc.EntryPrice,
            StopLoss = calc.StopLoss,
            StopDistanceAbsolute = calc.StopDistanceAbsolute,
            StopDistancePercent = calc.StopDistancePercent,
            RiskPerTradePercent = calc.RiskPerTradePercent,
            RiskAmount = calc.RiskAmount,
            RiskAtStopPercent = calc.RiskAtStopPercent,
            Quantity = calc.Quantity,
            PositionNotional = calc.PositionNotional,
            NotionalExposurePercent = calc.NotionalExposurePercent,
            MinimumRequiredLeverage = calc.MinimumRequiredLeverage,
            AssessmentLeverage = calc.AssessmentLeverage,
            PreferredLeverage = calc.PreferredLeverage,
            MaxLeverage = calc.MaxLeverage,
            InitialMarginRequired = calc.InitialMarginRequired,
            MarginUsagePercent = calc.MarginUsagePercent,
            EstimatedEntryFee = entryFee,
            EstimatedExitFee = exitFee,
            EstimatedRoundTripFees = roundTrip,
            TargetGrossProfit = targetGross,
            TargetNetProfitEstimate = targetNet,
            FeeToTargetPercent = feeToTarget,
            UnavailableReason = calc.UnavailableReason
        };
    }

    /// <summary>Backward-compatible overload.</summary>
    public PositionSizingObservation BuildSizing(
        StrategyResearchCandidate candidate,
        RiskProfileSnapshotDto snapshot,
        decimal assessmentBalance,
        decimal takerFeeRate) =>
        BuildSizing(
            candidate,
            snapshot,
            assessmentBalance,
            StrategyLabCostSnapshot.CreateDefault(makerFeeRate: takerFeeRate * 0.5m, takerFeeRate: takerFeeRate));

    private static List<RiskRuleResultDto> EvaluateFinancialRules(
        StrategyResearchCandidate candidate,
        RiskProfileSnapshotDto snapshot,
        RiskRuleSet rules,
        PositionSizingObservation sizing,
        ChronologicalShadowPortfolio shadow)
    {
        var results = new List<RiskRuleResultDto>();

        if (snapshot.ExposureSemanticsVersion == ExposureSemanticsVersion.LegacyAmbiguous)
        {
            results.Add(Rule(
                "LegacyExposureSemantics",
                "Legacy Exposure Semantics",
                RiskRuleCategory.Financial,
                RiskRuleResultStatus.Warning,
                RiskRuleSeverity.Warning,
                snapshot.LegacyMaxExposurePerSymbolPercent,
                null,
                "%",
                "Profile uses LegacyAmbiguous exposure fields. Ambiguous MaxExposure limits are not enforced as notional or margin until explicitly migrated."));
        }

        var stopOk = sizing.StopDistanceAbsolute > 0
            && ((candidate.Direction == TradeDirection.Long && candidate.StopLoss < candidate.ProposedEntryPrice)
                || (candidate.Direction == TradeDirection.Short && candidate.StopLoss > candidate.ProposedEntryPrice));
        results.Add(Rule(
            "ValidStopGeometry",
            "Valid Stop Geometry",
            RiskRuleCategory.Financial,
            stopOk ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            sizing.StopDistancePercent,
            null,
            "%",
            stopOk ? "Stop geometry is valid." : "Invalid stop geometry."));

        var maxRisk = snapshot.RiskPerTradePercent > 0 ? snapshot.RiskPerTradePercent : rules.MaxRiskPerTradePercent;
        results.Add(Rule(
            RiskRuleKeys.MaxRiskPerTradePercent,
            "Max Risk Per Trade",
            RiskRuleCategory.Financial,
            sizing.Quantity is > 0 ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            sizing.RiskAtStopPercent,
            maxRisk,
            "%",
            sizing.UnavailableReason ?? $"Risk at stop {sizing.RiskAtStopPercent:0.####}% vs allowed {maxRisk:0.####}%."));

        results.Add(Rule(
            "PositionSizing",
            "Position Sizing",
            RiskRuleCategory.Sizing,
            sizing.Quantity is > 0 ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            sizing.Quantity,
            null,
            "qty",
            sizing.Quantity is > 0 ? "Position size calculated." : sizing.UnavailableReason ?? "Sizing failed."));

        var minLev = sizing.MinimumRequiredLeverage;
        var maxLev = snapshot.MaxLeverage;
        var levOk = minLev.HasValue && RiskLimitComparison.IsWithinInclusiveMaximum(minLev.Value, maxLev);
        results.Add(Rule(
            "MaxLeverage",
            "Max Leverage",
            RiskRuleCategory.Financial,
            !minLev.HasValue
                ? RiskRuleResultStatus.NotAvailable
                : levOk ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            minLev,
            maxLev,
            "x",
            !minLev.HasValue
                ? "Minimum required leverage not available."
                : levOk
                    ? $"Minimum required leverage {minLev:0.##}x within maximum {maxLev:0.##}x (inclusive)."
                    : $"Minimum required leverage {minLev:0.##}x exceeds maximum {maxLev:0.##}x."));

        AddNullablePercentRule(
            results,
            "MaxNotionalExposurePerSymbolPercent",
            "Max Notional Exposure Per Symbol",
            RiskRuleCategory.Financial,
            sizing.NotionalExposurePercent,
            snapshot.MaxNotionalExposurePerSymbolPercent,
            "Notional exposure");

        var projectedTotalNotional = (shadow.CurrentNotionalExposurePercent ?? 0m) + (sizing.NotionalExposurePercent ?? 0m);
        AddNullablePercentRule(
            results,
            "MaxTotalNotionalExposurePercent",
            "Max Total Notional Exposure",
            RiskRuleCategory.Portfolio,
            sizing.NotionalExposurePercent.HasValue ? projectedTotalNotional : null,
            snapshot.MaxTotalNotionalExposurePercent,
            "Projected total notional exposure");

        AddNullablePercentRule(
            results,
            "MaxMarginUsagePerSymbolPercent",
            "Max Margin Usage Per Symbol",
            RiskRuleCategory.Financial,
            sizing.MarginUsagePercent,
            snapshot.MaxMarginUsagePerSymbolPercent,
            "Margin usage");

        var projectedTotalMargin = (shadow.CurrentMarginUsagePercent ?? 0m) + (sizing.MarginUsagePercent ?? 0m);
        AddNullablePercentRule(
            results,
            "MaxTotalMarginUsagePercent",
            "Max Total Margin Usage",
            RiskRuleCategory.Portfolio,
            sizing.MarginUsagePercent.HasValue ? projectedTotalMargin : null,
            snapshot.MaxTotalMarginUsagePercent,
            "Projected total margin usage");

        var projectedConcurrentRisk = (shadow.ConcurrentRiskPercent ?? 0m)
            + (sizing.RiskAmount > 0 && shadow.CurrentBalance > 0
                ? sizing.RiskAmount / shadow.CurrentBalance * 100m
                : 0m);
        AddNullablePercentRule(
            results,
            "MaxConcurrentRiskPercent",
            "Max Concurrent Risk",
            RiskRuleCategory.Portfolio,
            sizing.RiskAmount > 0 ? Math.Round(projectedConcurrentRisk, 6) : null,
            snapshot.MaxConcurrentRiskPercent,
            "Projected concurrent risk at stop");

        var nextCount = shadow.OpenPositions.Count + 1;
        var openOk = RiskLimitComparison.IsWithinInclusiveMaximum(nextCount, snapshot.MaxConcurrentPositions);
        results.Add(Rule(
            RiskRuleKeys.MaxOpenPositions,
            "Max Open Positions",
            RiskRuleCategory.Portfolio,
            openOk ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            nextCount,
            snapshot.MaxConcurrentPositions,
            "count",
            openOk
                ? $"Open positions would become {nextCount} within max {snapshot.MaxConcurrentPositions} (inclusive)."
                : $"Open positions would become {nextCount} exceeding max {snapshot.MaxConcurrentPositions}."));

        var daily = shadow.DailyLossUsagePercent ?? 0m;
        var maxDaily = snapshot.MaxDailyLossPercent;
        var dailyOk = maxDaily <= 0 || RiskLimitComparison.IsBelowExclusiveMaximum(daily, maxDaily);
        results.Add(Rule(
            RiskRuleKeys.MaxDailyLossPercent,
            "Max Daily Loss",
            RiskRuleCategory.Portfolio,
            dailyOk ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            daily,
            maxDaily,
            "%",
            dailyOk
                ? $"Daily loss usage {daily:0.####}% is below the exclusive limit {maxDaily:0.####}%."
                : $"Daily loss usage has reached the configured limit of {maxDaily:0.####}%. No additional positions may be opened."));

        var dd = shadow.CurrentDrawdownPercent ?? 0m;
        var maxDd = snapshot.MaxDrawdownPercent > 0 ? snapshot.MaxDrawdownPercent : 100m;
        var ddOk = RiskLimitComparison.IsWithinInclusiveMaximum(dd, maxDd);
        results.Add(Rule(
            "MaxDrawdownPercent",
            "Max Drawdown",
            RiskRuleCategory.Portfolio,
            ddOk ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            dd,
            maxDd,
            "%",
            ddOk
                ? $"Current realized drawdown {dd:0.####}% within limit {maxDd:0.####}% (inclusive)."
                : $"Current realized drawdown {dd:0.####}% exceeds limit {maxDd:0.####}%."));

        var rrOk = RiskLimitComparison.MeetsInclusiveMinimum(candidate.RewardRisk, snapshot.MinimumRewardRisk);
        results.Add(Rule(
            RiskRuleKeys.MinRewardRiskRatio,
            "Minimum Reward/Risk",
            RiskRuleCategory.Financial,
            rrOk ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            candidate.RewardRisk,
            snapshot.MinimumRewardRisk,
            "R",
            rrOk
                ? $"R:R {candidate.RewardRisk:0.##} meets minimum {snapshot.MinimumRewardRisk:0.##} (inclusive)."
                : $"R:R {candidate.RewardRisk:0.##} below minimum {snapshot.MinimumRewardRisk:0.##}."));

        var feeLimit = snapshot.FeeEfficiencyHardLimitPercent ?? 80m;
        if (sizing.FeeToTargetPercent is { } feePct)
        {
            var feeOk = RiskLimitComparison.IsWithinInclusiveMaximum(feePct, feeLimit);
            var feeWarn = feeOk
                && feePct >= feeLimit * 0.5m
                && feePct < feeLimit - RiskLimitComparison.DefaultTolerance;
            var feeStatus = !feeOk
                ? RiskRuleResultStatus.Failed
                : feeWarn
                    ? RiskRuleResultStatus.Warning
                    : RiskRuleResultStatus.Passed;
            var severity = !feeOk ? RiskRuleSeverity.HardReject : RiskRuleSeverity.Warning;
            results.Add(Rule(
                "FeeEfficiency",
                "Fee Efficiency",
                RiskRuleCategory.Financial,
                feeStatus,
                severity,
                feePct,
                feeLimit,
                "%",
                !feeOk
                    ? $"Expected target costs are {feePct:0.##}% of target gross profit, exceeding limit {feeLimit:0.##}%."
                    : $"Expected target costs are {feePct:0.##}% of target gross profit (limit {feeLimit:0.##}%, inclusive)."));
        }
        else
        {
            results.Add(Rule(
                "FeeEfficiency",
                "Fee Efficiency",
                RiskRuleCategory.Financial,
                RiskRuleResultStatus.NotAvailable,
                RiskRuleSeverity.Info,
                null,
                feeLimit,
                "%",
                "Fee efficiency not available."));
        }

        if (rules.RequireStopLoss)
        {
            results.Add(Rule(
                RiskRuleKeys.RequireStopLoss,
                "Require Stop Loss",
                RiskRuleCategory.Financial,
                candidate.StopLoss > 0 ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
                RiskRuleSeverity.HardReject,
                candidate.StopLoss,
                null,
                "price",
                candidate.StopLoss > 0 ? "Stop loss present." : "Stop loss required."));
        }

        return results;
    }

    private static void AddNullablePercentRule(
        List<RiskRuleResultDto> results,
        string key,
        string name,
        RiskRuleCategory category,
        decimal? actual,
        decimal? limit,
        string label)
    {
        if (limit is null)
        {
            results.Add(Rule(
                key,
                name,
                category,
                RiskRuleResultStatus.NotApplicable,
                RiskRuleSeverity.Info,
                actual,
                null,
                "%",
                $"{label} rule disabled (null limit)."));
            return;
        }

        if (!actual.HasValue)
        {
            results.Add(Rule(
                key,
                name,
                category,
                RiskRuleResultStatus.NotAvailable,
                RiskRuleSeverity.HardReject,
                null,
                limit,
                "%",
                $"{label} unavailable."));
            return;
        }

        var ok = RiskLimitComparison.IsWithinInclusiveMaximum(actual.Value, limit.Value);
        results.Add(Rule(
            key,
            name,
            category,
            ok ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            actual,
            limit,
            "%",
            ok
                ? $"{label} {actual:0.##} within limit {limit:0.##} (inclusive)."
                : $"{label} {actual:0.##} exceeds limit {limit:0.##}."));
    }

    private static (ResearchRiskPolicyEligibilityDecision Decision, string? Reason, List<string> FailedKeys, List<RiskRuleResultDto> RuleResults)
        EvaluatePolicy(decimal? confidenceScore, RiskProfileSnapshotDto snapshot)
    {
        var results = new List<RiskRuleResultDto>();
        var failed = new List<string>();
        if (!snapshot.PolicyMinimumConfidence.HasValue || snapshot.PolicyMinimumConfidence.Value <= 0)
        {
            results.Add(Rule(
                RiskRuleKeys.MinConfidenceScore,
                "Minimum Confidence Policy",
                RiskRuleCategory.Policy,
                RiskRuleResultStatus.NotApplicable,
                RiskRuleSeverity.Info,
                confidenceScore,
                snapshot.PolicyMinimumConfidence,
                "score",
                "No minimum confidence policy configured on this risk profile."));
            return (ResearchRiskPolicyEligibilityDecision.Eligible, "No policy minimum confidence.", failed, results);
        }

        var min = snapshot.PolicyMinimumConfidence.Value;
        if (!confidenceScore.HasValue)
        {
            results.Add(Rule(
                RiskRuleKeys.MinConfidenceScore,
                "Minimum Confidence Policy",
                RiskRuleCategory.Policy,
                RiskRuleResultStatus.NotAvailable,
                RiskRuleSeverity.Info,
                null,
                min,
                "score",
                "Confidence not evaluated in this run; policy not applied as a hard eligibility block."));
            return (ResearchRiskPolicyEligibilityDecision.Eligible,
                "Confidence unavailable; policy minimum confidence not enforced as financial risk.",
                failed,
                results);
        }

        var ok = RiskLimitComparison.MeetsInclusiveMinimum(confidenceScore.Value, min);
        if (!ok)
        {
            failed.Add(RiskRuleKeys.MinConfidenceScore);
        }

        results.Add(Rule(
            RiskRuleKeys.MinConfidenceScore,
            "Minimum Confidence Policy",
            RiskRuleCategory.Policy,
            ok ? RiskRuleResultStatus.Passed : RiskRuleResultStatus.Failed,
            RiskRuleSeverity.HardReject,
            confidenceScore,
            min,
            "score",
            ok
                ? $"Confidence {confidenceScore:0.##} meets policy minimum {min:0.##} (inclusive)."
                : $"Confidence score {confidenceScore:0.##} is below risk profile policy minimum {min:0.##}."));

        return ok
            ? (ResearchRiskPolicyEligibilityDecision.Eligible, "Policy eligibility passed.", failed, results)
            : (ResearchRiskPolicyEligibilityDecision.Ineligible,
                $"Confidence score {confidenceScore:0.##} is below risk profile policy minimum {min:0.##}.",
                failed,
                results);
    }

    private static RiskRuleResultDto Rule(
        string key,
        string name,
        RiskRuleCategory category,
        RiskRuleResultStatus status,
        RiskRuleSeverity severity,
        decimal? actual,
        decimal? limit,
        string? unit,
        string reason) =>
        new()
        {
            RuleKey = key,
            RuleName = name,
            Category = category.ToString(),
            Status = status.ToString(),
            Severity = severity.ToString(),
            ActualValue = actual,
            LimitValue = limit,
            Unit = unit,
            Reason = reason
        };
}

