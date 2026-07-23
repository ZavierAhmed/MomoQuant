using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab.Risk;

/// <summary>
/// Independent chronological dual-shadow processing (IndependentPaths/v1).
/// Risk-Only and Full-Pipeline each evaluate financial risk against their own state.
/// FinalPipelineDecision uses Full-Pipeline financial risk only.
/// </summary>
public static class ChronologicalShadowProcessor
{
    public sealed class Result
    {
        public required ChronologicalShadowPortfolio RiskOnly { get; init; }
        public required ChronologicalShadowPortfolio FullPipeline { get; init; }
        public required ShadowPortfolioSummaryDto RiskOnlySummary { get; init; }
        public required ShadowPortfolioSummaryDto FullPipelineSummary { get; init; }
        public required StrategyLabCostSnapshot CostSnapshot { get; init; }
        public required PortfolioPathDivergenceDto Divergence { get; init; }
        public IReadOnlyList<decimal> PortfolioRiskScores { get; init; } = [];
        public IReadOnlyList<string> Diagnostics { get; init; } = [];
    }

    public static Result Process(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        RiskProfileSnapshotDto snapshot,
        RiskRuleSet rules,
        StrategyLabRiskObserver observer,
        decimal initialBalance,
        StrategyLabCostSnapshot costSnapshot,
        bool riskOnlyAppliesRiskPolicy = false)
    {
        var riskOnly = new ChronologicalShadowPortfolio("RiskOnly", initialBalance, costSnapshot);
        var fullPipeline = new ChronologicalShadowPortfolio("FullPipeline", initialBalance, costSnapshot);
        var diagnostics = new List<string>();
        var portfolioScores = new List<decimal>();

        // Shared-state detection (must never pass).
        if (ReferenceEquals(riskOnly, fullPipeline)
            || ReferenceEquals(riskOnly.OpenPositions, fullPipeline.OpenPositions))
        {
            diagnostics.Add("SharedPortfolioStateDetected: Risk-Only and Full-Pipeline share object identity.");
        }

        DateTime? firstDivergence = null;
        var differentRiskDecisions = 0;
        var openedOnlyRiskOnly = 0;
        var openedOnlyFull = 0;
        var openedBoth = 0;
        var openedNeither = 0;

        var ordered = candidates
            .OrderBy(c => c.ProposedEntryTimeUtc)
            .ThenBy(c => c.SetupFingerprint, StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in ordered)
        {
            var entryAt = DateTime.SpecifyKind(candidate.ProposedEntryTimeUtc, DateTimeKind.Utc);
            var exitAt = candidate.RawExitTimeUtc.HasValue
                ? DateTime.SpecifyKind(candidate.RawExitTimeUtc.Value, DateTimeKind.Utc)
                : entryAt;

            // Independent exit/day processing per path
            AdvanceAndClose(riskOnly, entryAt);
            AdvanceAndClose(fullPipeline, entryAt);

            if (candidate.CandidateStatus == StrategyResearchCandidateStatus.SimulationInvalid
                || candidate.RawOutcomeStatus == RawOutcomeStatus.Invalid)
            {
                ClearPathFields(candidate);
                candidate.RiskDecision = ResearchRiskDecision.NotEvaluated;
                candidate.FullPipelineFinancialRiskDecision = ResearchRiskDecision.NotEvaluated;
                candidate.RiskOnlyFinancialRiskDecision = ResearchRiskDecision.NotEvaluated;
                candidate.RiskOnlyEntryDecision = ShadowEntryDecision.Invalid;
                candidate.FullPipelineEntryDecision = ShadowEntryDecision.Invalid;
                candidate.FinalPipelineDecision = ResearchFinalPipelineDecision.RawOnly;
                candidate.GenericRiskFieldSource = GenericRiskFieldSource.RiskOnly;
                candidate.RiskPathAssessmentVersion = IndependentPathsVersions.Current;
                continue;
            }

            // -------- Risk-Only path evaluation (own state only) --------
            var riskOnlyBalance = riskOnly.CurrentBalance > 0 ? riskOnly.CurrentBalance : initialBalance;
            var riskOnlyObs = observer.Evaluate(
                candidate,
                snapshot,
                rules,
                riskOnly,
                riskOnlyAppliesRiskPolicy ? candidate.ConfidenceScore : null,
                riskOnlyBalance,
                costSnapshot,
                // Policy ignored by default for risk-only entry; still compute confidence for sources display.
                ResearchConfidenceDecision.NotEvaluated);

            // Strip policy from risk-only financial decision path when policy disabled
            if (!riskOnlyAppliesRiskPolicy)
            {
                // Financial result already ignores confidence; policy may still be in RuleResults — fine.
            }

            var (roEntry, roReason, roSources) = DecideRiskOnlyEntry(riskOnlyObs);
            var riskOnlyAssessment = PathAssessmentFactory.WithBalance(
                PathAssessmentFactory.FromObservation(
                    StrategyLabPortfolioPath.RiskOnly,
                    riskOnlyObs,
                    roEntry,
                    roReason,
                    roSources),
                riskOnlyBalance,
                ProjectConcurrent(riskOnly, riskOnlyObs));

            if (roEntry == ShadowEntryDecision.Opened && riskOnlyObs.Sizing.Quantity is > 0)
            {
                riskOnly.TryOpen(candidate, ToCalcResult(riskOnlyObs.Sizing), entryAt, exitAt, riskOnlyBalance);
            }
            else
            {
                riskOnly.RecordRejection(entryAt, candidate.Id, roReason);
            }

            riskOnly.RecordEvaluation(entryAt, candidate.Id,
                $"RiskOnly Financial={riskOnlyObs.FinancialRiskDecision}; Entry={roEntry}");

            // -------- Full-Pipeline path evaluation (own state only) --------
            var fpBalance = fullPipeline.CurrentBalance > 0 ? fullPipeline.CurrentBalance : initialBalance;
            var fpObs = observer.Evaluate(
                candidate,
                snapshot,
                rules,
                fullPipeline,
                candidate.ConfidenceScore,
                fpBalance,
                costSnapshot,
                candidate.ConfidenceDecision ?? ResearchConfidenceDecision.NotEvaluated);

            var (fpEntry, fpReason, fpSources) = DecideFullPipelineEntry(
                candidate,
                fpObs);

            var fullAssessment = PathAssessmentFactory.WithBalance(
                PathAssessmentFactory.FromObservation(
                    StrategyLabPortfolioPath.FullPipeline,
                    fpObs,
                    fpEntry,
                    fpReason,
                    fpSources),
                fpBalance,
                ProjectConcurrent(fullPipeline, fpObs));

            if (fpEntry == ShadowEntryDecision.Opened && fpObs.Sizing.Quantity is > 0)
            {
                fullPipeline.TryOpen(candidate, ToCalcResult(fpObs.Sizing), entryAt, exitAt, fpBalance);
            }
            else
            {
                fullPipeline.RecordRejection(entryAt, candidate.Id, fpReason);
            }

            fullPipeline.RecordEvaluation(entryAt, candidate.Id,
                $"FullPipeline Financial={fpObs.FinancialRiskDecision}; Entry={fpEntry}");

            // Persist both assessments on candidate; generic fields = Risk-Only (documented).
            ApplyPathResultsToCandidate(candidate, riskOnlyObs, fpObs, riskOnlyAssessment, fullAssessment, snapshot);

            foreach (var d in ValidateRuleConsistency(riskOnlyObs.RuleResults))
            {
                if (!diagnostics.Contains(d)) diagnostics.Add(d);
            }

            foreach (var d in ValidateRuleConsistency(fpObs.RuleResults))
            {
                if (!diagnostics.Contains(d)) diagnostics.Add(d);
            }

            // Contamination diagnostics
            if (riskOnlyObs.FinancialRiskDecision != fpObs.FinancialRiskDecision
                && candidate.RiskDecision == riskOnlyObs.FinancialRiskDecision
                && candidate.FinalPipelineDecision == ResearchFinalPipelineDecision.RejectedByRisk
                && fpObs.FinancialRiskDecision == ResearchRiskDecision.Approved)
            {
                diagnostics.Add("FinalDecisionUsedRiskOnlyState: FinalPipeline rejected by risk while Full-Pipeline financial approved.");
            }

            if (ContainsDrawdownFromOtherPath(riskOnlyAssessment, fullAssessment))
            {
                diagnostics.Add("PathRuleContamination: Drawdown failure appears mismatched across paths.");
            }

            if (fpObs.PortfolioRiskScore is { } prs)
            {
                portfolioScores.Add(prs);
            }

            // Divergence tracking
            if (riskOnlyObs.FinancialRiskDecision != fpObs.FinancialRiskDecision)
            {
                differentRiskDecisions++;
            }

            var roOpened = roEntry == ShadowEntryDecision.Opened;
            var fpOpened = fpEntry == ShadowEntryDecision.Opened;
            if (roOpened && fpOpened) openedBoth++;
            else if (roOpened) openedOnlyRiskOnly++;
            else if (fpOpened) openedOnlyFull++;
            else openedNeither++;

            if (firstDivergence is null
                && (!RiskLimitComparison.ApproximatelyEqual(riskOnly.CurrentBalance, fullPipeline.CurrentBalance)
                    || roOpened != fpOpened
                    || riskOnly.OpenPositions.Count != fullPipeline.OpenPositions.Count))
            {
                firstDivergence = entryAt;
            }
        }

        FlushRemaining(riskOnly);
        FlushRemaining(fullPipeline);

        // Suspicious identical state after material path difference
        if (openedOnlyRiskOnly + openedOnlyFull > 0
            && RiskLimitComparison.ApproximatelyEqual(riskOnly.CurrentBalance, fullPipeline.CurrentBalance)
            && RiskLimitComparison.ApproximatelyEqual(
                riskOnly.MaxRealizedDrawdownPercent,
                fullPipeline.MaxRealizedDrawdownPercent))
        {
            diagnostics.Add(
                "IdenticalPortfolioStateSuspicious: Paths opened different trades but ending balance/drawdown remain identical.");
        }

        if (candidates.Any(c =>
                c.RiskOnlyAssessmentJson is null || c.FullPipelineAssessmentJson is null)
            && candidates.Any(c => c.RiskDecision is ResearchRiskDecision.Approved or ResearchRiskDecision.Rejected))
        {
            diagnostics.Add("MissingPathAssessment: One or more candidates lack path assessments.");
        }

        var divergence = new PortfolioPathDivergenceDto
        {
            FirstDivergenceAtUtc = firstDivergence,
            FinalBalanceDifference = Math.Round(riskOnly.CurrentBalance - fullPipeline.CurrentBalance, 8),
            MaxDrawdownDifference = Math.Round(
                riskOnly.MaxRealizedDrawdownPercent - fullPipeline.MaxRealizedDrawdownPercent, 6),
            TradeCountDifference = riskOnly.AcceptedCount - fullPipeline.AcceptedCount,
            DifferentPortfolioRiskDecisions = differentRiskDecisions,
            OpenedOnlyInRiskOnly = openedOnlyRiskOnly,
            OpenedOnlyInFullPipeline = openedOnlyFull,
            OpenedInBoth = openedBoth,
            OpenedInNeither = openedNeither
        };

        return new Result
        {
            RiskOnly = riskOnly,
            FullPipeline = fullPipeline,
            RiskOnlySummary = ToSummary(riskOnly),
            FullPipelineSummary = ToSummary(fullPipeline),
            CostSnapshot = costSnapshot,
            Divergence = divergence,
            PortfolioRiskScores = portfolioScores,
            Diagnostics = diagnostics
        };
    }

    private static decimal? ProjectConcurrent(ChronologicalShadowPortfolio portfolio, StrategyLabRiskObservationResult obs)
    {
        if (obs.Sizing.RiskAmount <= 0 || portfolio.CurrentBalance <= 0) return obs.ConcurrentRiskPercent;
        var add = obs.Sizing.RiskAmount / portfolio.CurrentBalance * 100m;
        return Math.Round((obs.ConcurrentRiskPercent ?? 0m) + add, 6);
    }

    private static (ShadowEntryDecision Decision, string Reason, List<string> Sources)
        DecideRiskOnlyEntry(StrategyLabRiskObservationResult obs)
    {
        var sources = new List<string>();
        if (obs.FinancialRiskDecision != ResearchRiskDecision.Approved)
        {
            sources.Add(FinalPipelineRejectionSources.FinancialRisk);
            var portfolioRule = obs.FailedRuleKeys.Any(k =>
                k is "MaxDrawdownPercent" or "MaxDailyLossPercent" or "MaxOpenPositions"
                    or "MaxTotalMarginUsagePercent" or "MaxTotalNotionalExposurePercent"
                    or "MaxConcurrentRiskPercent");
            return (
                portfolioRule ? ShadowEntryDecision.RejectedByPortfolioRisk : ShadowEntryDecision.RejectedByCandidateRisk,
                obs.FinancialRiskReason,
                sources);
        }

        if (obs.Sizing.Quantity is null or <= 0)
        {
            sources.Add(FinalPipelineRejectionSources.FinancialRisk);
            return (ShadowEntryDecision.RejectedByCandidateRisk, obs.Sizing.UnavailableReason ?? "Sizing failed.", sources);
        }

        return (ShadowEntryDecision.Opened, "Risk-Only financial risk approved.", sources);
    }

    private static (ShadowEntryDecision Decision, string Reason, List<string> Sources)
        DecideFullPipelineEntry(
            StrategyResearchCandidate candidate,
            StrategyLabRiskObservationResult obs)
    {
        var sources = new List<string>();
        var confRejected = candidate.ConfidenceDecision == ResearchConfidenceDecision.Rejected;
        var policyRejected = obs.PolicyDecision == ResearchRiskPolicyEligibilityDecision.Ineligible;
        var financialRejected = obs.FinancialRiskDecision != ResearchRiskDecision.Approved
            || obs.Sizing.Quantity is null or <= 0;

        if (confRejected) sources.Add(FinalPipelineRejectionSources.Confidence);
        if (policyRejected) sources.Add(FinalPipelineRejectionSources.RiskPolicy);
        if (financialRejected) sources.Add(FinalPipelineRejectionSources.FinancialRisk);

        if (sources.Count > 1)
        {
            return (ShadowEntryDecision.RejectedByMultipleSources,
                string.Join("; ", BuildReasons(confRejected, policyRejected, financialRejected, obs)),
                sources);
        }

        if (confRejected)
        {
            return (ShadowEntryDecision.RejectedByConfidence,
                candidate.ConfidenceReason ?? "Confidence rejected.", sources);
        }

        if (policyRejected)
        {
            return (ShadowEntryDecision.RejectedByPolicy,
                obs.PolicyReason ?? candidate.RiskPolicyReason ?? "Policy ineligible.", sources);
        }

        if (financialRejected)
        {
            var portfolioRule = obs.FailedRuleKeys.Any(k =>
                k is "MaxDrawdownPercent" or "MaxDailyLossPercent" or "MaxOpenPositions"
                    or "MaxTotalMarginUsagePercent" or "MaxTotalNotionalExposurePercent"
                    or "MaxConcurrentRiskPercent");
            return (
                portfolioRule ? ShadowEntryDecision.RejectedByPortfolioRisk : ShadowEntryDecision.RejectedByCandidateRisk,
                obs.FinancialRiskReason,
                sources);
        }

        return (ShadowEntryDecision.Opened, "Full-Pipeline gates passed.", sources);
    }

    private static IEnumerable<string> BuildReasons(
        bool conf, bool policy, bool financial, StrategyLabRiskObservationResult obs)
    {
        if (conf) yield return "Confidence rejected";
        if (policy) yield return obs.PolicyReason ?? "Policy ineligible";
        if (financial) yield return obs.FinancialRiskReason;
    }

    private static void ApplyPathResultsToCandidate(
        StrategyResearchCandidate candidate,
        StrategyLabRiskObservationResult riskOnlyObs,
        StrategyLabRiskObservationResult fullPipelineObs,
        PathPortfolioAssessmentDto riskOnlyAssessment,
        PathPortfolioAssessmentDto fullAssessment,
        RiskProfileSnapshotDto snapshot)
    {
        // Generic fields = Risk-Only (documented source).
        StrategyLabRiskObserver.ApplyToCandidate(candidate, riskOnlyObs, snapshot);
        candidate.GenericRiskFieldSource = GenericRiskFieldSource.RiskOnly;
        candidate.RiskPathAssessmentVersion = IndependentPathsVersions.Current;

        // Policy eligibility is a pipeline concern — store Full-Pipeline evaluation.
        candidate.RiskPolicyEligibilityDecision = fullPipelineObs.PolicyDecision;
        candidate.RiskPolicyReason = fullPipelineObs.PolicyReason;
        candidate.RiskPolicyFailedRuleKeysJson = RiskObservationJson.Serialize(fullPipelineObs.PolicyFailedRuleKeys);
        candidate.RiskPolicyMinimumConfidence = fullPipelineObs.PolicyMinimumConfidence;

        candidate.RiskOnlyFinancialRiskDecision = riskOnlyAssessment.FinancialRiskDecision;
        candidate.RiskOnlyEntryDecision = riskOnlyAssessment.EntryDecision;
        candidate.RiskOnlyRejectionSourcesJson = RiskObservationJson.Serialize(riskOnlyAssessment.RejectionSources);
        candidate.RiskOnlyAssessmentJson = RiskObservationJson.Serialize(riskOnlyAssessment);
        candidate.RiskOnlyCurrentDrawdownPercent = riskOnlyAssessment.CurrentDrawdownPercent;
        candidate.RiskOnlyDailyLossUsagePercent = riskOnlyAssessment.CurrentDailyLossUsagePercent;
        candidate.RiskOnlyCurrentMarginUsagePercent = riskOnlyAssessment.CurrentMarginUsagePercent;
        candidate.RiskOnlyConcurrentRiskPercent = riskOnlyAssessment.CurrentConcurrentRiskPercent;
        candidate.RiskOnlyOpenPositionCount = riskOnlyAssessment.CurrentOpenPositionCount;

        candidate.FullPipelineFinancialRiskDecision = fullAssessment.FinancialRiskDecision;
        candidate.FullPipelineEntryDecision = fullAssessment.EntryDecision;
        candidate.FullPipelineRejectionSourcesJson = RiskObservationJson.Serialize(fullAssessment.RejectionSources);
        candidate.FullPipelineAssessmentJson = RiskObservationJson.Serialize(fullAssessment);
        candidate.FullPipelineCurrentDrawdownPercent = fullAssessment.CurrentDrawdownPercent;
        candidate.FullPipelineDailyLossUsagePercent = fullAssessment.CurrentDailyLossUsagePercent;
        candidate.FullPipelineCurrentMarginUsagePercent = fullAssessment.CurrentMarginUsagePercent;
        candidate.FullPipelineConcurrentRiskPercent = fullAssessment.CurrentConcurrentRiskPercent;
        candidate.FullPipelineOpenPositionCount = fullAssessment.CurrentOpenPositionCount;

        // Final pipeline uses Full-Pipeline financial risk ONLY.
        candidate.FinalPipelineRejectionSourcesJson = RiskObservationJson.Serialize(fullAssessment.RejectionSources);
        candidate.FinalPipelineDecision = StrategyLabRiskObserver.ResolveFinalDecisionFromFullPipeline(
            candidate.ConfidenceDecision,
            candidate.RiskPolicyEligibilityDecision,
            fullAssessment.FinancialRiskDecision);
    }

    private static void ClearPathFields(StrategyResearchCandidate candidate)
    {
        candidate.RiskOnlyAssessmentJson = null;
        candidate.FullPipelineAssessmentJson = null;
        candidate.RiskOnlyEntryDecision = ShadowEntryDecision.NotEvaluated;
        candidate.FullPipelineEntryDecision = ShadowEntryDecision.NotEvaluated;
        candidate.GenericRiskFieldSource = GenericRiskFieldSource.Legacy;
    }

    private static bool ContainsDrawdownFromOtherPath(
        PathPortfolioAssessmentDto riskOnly,
        PathPortfolioAssessmentDto full)
    {
        var roFailedDd = riskOnly.FailedRuleKeys.Contains("MaxDrawdownPercent");
        var fpFailedDd = full.FailedRuleKeys.Contains("MaxDrawdownPercent");
        if (roFailedDd && !fpFailedDd
            && full.CurrentDrawdownPercent is { } fdd
            && riskOnly.CurrentDrawdownPercent is { } rdd
            && fdd < rdd - 0.01m
            && full.FinancialRiskDecision == ResearchRiskDecision.Rejected
            && full.FailedRuleKeys.Contains("MaxDrawdownPercent"))
        {
            return true;
        }

        return false;
    }

    public static IReadOnlyList<string> ValidateRuleConsistency(IReadOnlyList<RiskRuleResultDto> rules)
    {
        var issues = new List<string>();
        foreach (var r in rules)
        {
            if (r.Status == nameof(RiskRuleResultStatus.Failed)
                && r.Reason.Contains("within limit", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"RiskRuleStatusValueMismatch: {r.RuleKey} Failed but reason says within limit.");
            }

            if (r.Status == nameof(RiskRuleResultStatus.Failed)
                && string.IsNullOrWhiteSpace(r.Reason)
                && r.ActualValue is null)
            {
                issues.Add($"RiskRuleStatusValueMismatch: {r.RuleKey} Failed with incomplete result.");
            }
        }

        return issues;
    }

    private static void AdvanceAndClose(ChronologicalShadowPortfolio portfolio, DateTime entryAt)
    {
        portfolio.CloseDuePositions(entryAt);
        portfolio.AdvanceTo(entryAt);
    }

    private static void FlushRemaining(ChronologicalShadowPortfolio portfolio)
    {
        var remaining = portfolio.OpenPositions.ToList();
        foreach (var pos in remaining.OrderBy(p => p.ExpectedExitAtUtc).ThenBy(p => p.SetupFingerprint, StringComparer.Ordinal))
        {
            portfolio.AdvanceTo(pos.ExpectedExitAtUtc);
            portfolio.ClosePosition(pos);
        }
    }

    private static FuturesSizingCalculator.Result ToCalcResult(PositionSizingObservation sizing) =>
        new()
        {
            EntryPrice = sizing.EntryPrice,
            StopLoss = sizing.StopLoss,
            StopDistanceAbsolute = sizing.StopDistanceAbsolute,
            StopDistancePercent = sizing.StopDistancePercent,
            RiskPerTradePercent = sizing.RiskPerTradePercent,
            RiskAmount = sizing.RiskAmount,
            RiskAtStopPercent = sizing.RiskAtStopPercent,
            Quantity = sizing.Quantity,
            PositionNotional = sizing.PositionNotional,
            NotionalExposurePercent = sizing.NotionalExposurePercent,
            MinimumRequiredLeverage = sizing.MinimumRequiredLeverage,
            AssessmentLeverage = sizing.AssessmentLeverage,
            PreferredLeverage = sizing.PreferredLeverage,
            MaxLeverage = sizing.MaxLeverage,
            InitialMarginRequired = sizing.InitialMarginRequired,
            MarginUsagePercent = sizing.MarginUsagePercent,
            EstimatedEntryFee = sizing.EstimatedEntryFee,
            EstimatedExitFee = sizing.EstimatedExitFee,
            EstimatedRoundTripFees = sizing.EstimatedRoundTripFees,
            TargetGrossProfit = sizing.TargetGrossProfit,
            TargetNetProfitEstimate = sizing.TargetNetProfitEstimate,
            FeeToTargetPercent = sizing.FeeToTargetPercent,
            UnavailableReason = sizing.UnavailableReason
        };

    private static ShadowPortfolioSummaryDto ToSummary(ChronologicalShadowPortfolio p)
    {
        var netReturn = p.InitialBalance > 0
            ? Math.Round((p.CurrentBalance - p.InitialBalance) / p.InitialBalance * 100m, 6)
            : 0m;
        var grossReturn = p.InitialBalance > 0
            ? Math.Round(p.TotalGrossPnl / p.InitialBalance * 100m, 6)
            : 0m;

        return new ShadowPortfolioSummaryDto
        {
            PathName = p.PathName,
            StartingBalance = p.InitialBalance,
            EndingBalance = p.CurrentBalance,
            GrossPnl = Math.Round(p.TotalGrossPnl, 8),
            GrossReturnPercent = grossReturn,
            EntryFees = Math.Round(p.TotalEntryFees, 8),
            ExitFees = Math.Round(p.TotalExitFees, 8),
            SlippageCost = Math.Round(p.TotalSlippageCost, 8),
            FundingCost = Math.Round(p.TotalFundingCost, 8),
            TotalTransactionCosts = Math.Round(p.TotalTransactionCosts, 8),
            RealizedNetPnl = Math.Round(p.TotalNetPnl, 8),
            NetReturnPercent = netReturn,
            NetReturnAfterCostsPercent = netReturn,
            TradesAccepted = p.AcceptedCount,
            TradesRejected = p.RejectedCount,
            TradesOpened = p.AcceptedCount,
            ProfitableTrades = p.ProfitableTrades,
            LosingTrades = p.LosingTrades,
            BreakevenTrades = p.BreakevenTrades,
            MaxRealizedDrawdownPercent = p.MaxRealizedDrawdownPercent,
            PeakMarginUsagePercent = p.PeakMarginUsagePercent,
            PeakNotionalExposurePercent = p.PeakNotionalExposurePercent,
            PeakConcurrentRiskPercent = p.PeakConcurrentRiskPercent,
            PeakOpenPositionCount = p.PeakOpenPositionCount,
            DrawdownCalculationMode = DrawdownCalculationMode.RealizedOnly,
            CostModelVersion = StrategyLabCostModelVersions.V1,
            Ledger = p.ClosedTrades,
            AuditEvents = p.AuditEvents
        };
    }
}
