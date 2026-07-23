using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public sealed class StrategyLabIndependentPathsTests
{
    private readonly StrategyLabRiskObserver _observer = new();

    [Fact]
    public void Path_states_are_distinct_object_instances()
    {
        var result = ChronologicalShadowProcessor.Process(
            [ApprovedWinner("a")],
            Snapshot(maxDrawdown: 20m),
            Rules(),
            _observer,
            10000m,
            StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m));

        Assert.False(ReferenceEquals(result.RiskOnly, result.FullPipeline));
        Assert.False(ReferenceEquals(result.RiskOnly.OpenPositions, result.FullPipeline.OpenPositions));
        Assert.DoesNotContain(result.Diagnostics, d => d.StartsWith("SharedPortfolioStateDetected", StringComparison.Ordinal));
    }

    [Fact]
    public void Opening_risk_only_position_does_not_add_full_pipeline_position()
    {
        var cost = StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m);
        var riskOnly = new ChronologicalShadowPortfolio("RiskOnly", 10000m, cost);
        var full = new ChronologicalShadowPortfolio("FullPipeline", 10000m, cost);
        var loser = ApprovedLoser("lose");
        loser.ProposedEntryTimeUtc = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        loser.RawExitTimeUtc = loser.ProposedEntryTimeUtc.AddHours(1);
        var sizing = FuturesSizingCalculator.Calculate(
            loser.ProposedEntryPrice, loser.StopLoss, loser.Target1, 10000m, 1m, 10m, 10m, 0.0004m);

        Assert.True(riskOnly.TryOpen(loser, sizing, loser.ProposedEntryTimeUtc, loser.RawExitTimeUtc!.Value, 10000m));
        Assert.Single(riskOnly.OpenPositions);
        Assert.Empty(full.OpenPositions);

        riskOnly.AdvanceTo(loser.RawExitTimeUtc.Value);
        riskOnly.CloseDuePositions(loser.RawExitTimeUtc.Value);
        Assert.True(riskOnly.CurrentBalance < 10000m);
        Assert.Equal(10000m, full.CurrentBalance);
        Assert.Empty(full.ClosedTrades);
    }

    [Fact]
    public void Risk_only_drawdown_failure_does_not_reject_full_pipeline()
    {
        var t0 = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var firstLoser = ApprovedLoser("lose-0");
        firstLoser.ProposedEntryTimeUtc = t0;
        firstLoser.RawExitTimeUtc = t0.AddMinutes(30);
        firstLoser.ConfidenceDecision = ResearchConfidenceDecision.Rejected;
        firstLoser.ConfidenceScore = 40m;

        var later = ApprovedWinner("later-fp");
        later.ProposedEntryTimeUtc = t0.AddHours(2);
        later.RawExitTimeUtc = later.ProposedEntryTimeUtc.AddHours(1);
        later.ConfidenceDecision = ResearchConfidenceDecision.Approved;
        later.ConfidenceScore = 90m;

        // Tight drawdown so one Risk-Only loser breaches the limit; Full-Pipeline never opened that loser.
        var result = ChronologicalShadowProcessor.Process(
            [firstLoser, later],
            Snapshot(maxDrawdown: 0.5m),
            Rules(),
            _observer,
            10000m,
            StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m));

        Assert.True(
            result.RiskOnly.MaxRealizedDrawdownPercent > 0.5m,
            $"RO max DD={result.RiskOnly.MaxRealizedDrawdownPercent}, accepted={result.RiskOnlySummary.TradesAccepted}");
        Assert.True(later.RiskOnlyCurrentDrawdownPercent is > 0.5m);
        Assert.Equal(ShadowEntryDecision.RejectedByPortfolioRisk, later.RiskOnlyEntryDecision);
        Assert.True((later.FullPipelineCurrentDrawdownPercent ?? 0m) <= 0.5m);
        Assert.Equal(ResearchRiskDecision.Approved, later.FullPipelineFinancialRiskDecision);
        Assert.Equal(ShadowEntryDecision.Opened, later.FullPipelineEntryDecision);
        Assert.Equal(ResearchFinalPipelineDecision.Approved, later.FinalPipelineDecision);
        Assert.DoesNotContain("\"MaxDrawdownPercent\"", later.FullPipelineRejectionSourcesJson ?? "");
        Assert.False(
            later.FullPipelineAssessmentJson?.Contains("\"status\":\"Failed\"") == true
            && later.FullPipelineAssessmentJson.Contains("MaxDrawdownPercent")
            && later.FullPipelineFinancialRiskDecision == ResearchRiskDecision.Rejected);
        Assert.True(result.FullPipelineSummary.TradesAccepted >= 1);
        Assert.True(result.Divergence.OpenedOnlyInRiskOnly >= 1);
        Assert.True(result.Divergence.OpenedOnlyInFullPipeline >= 1);
    }

    [Fact]
    public void Final_pipeline_decision_uses_full_pipeline_financial_risk_only()
    {
        var candidate = ApprovedWinner("fp-final");
        candidate.ConfidenceDecision = ResearchConfidenceDecision.Approved;
        candidate.RiskOnlyFinancialRiskDecision = ResearchRiskDecision.Rejected;
        candidate.RiskDecision = ResearchRiskDecision.Rejected;
        candidate.FullPipelineFinancialRiskDecision = ResearchRiskDecision.Approved;
        candidate.RiskPolicyEligibilityDecision = ResearchRiskPolicyEligibilityDecision.Eligible;

        Assert.Equal(
            ResearchFinalPipelineDecision.Approved,
            StrategyLabRiskObserver.ResolveFinalDecision(candidate));

        candidate.FullPipelineFinancialRiskDecision = ResearchRiskDecision.Rejected;
        Assert.Equal(
            ResearchFinalPipelineDecision.RejectedByRisk,
            StrategyLabRiskObserver.ResolveFinalDecision(candidate));
    }

    [Fact]
    public void Legacy_candidates_without_path_fields_remain_readable()
    {
        var legacy = ApprovedWinner("legacy");
        legacy.RiskDecision = ResearchRiskDecision.Approved;
        legacy.ConfidenceDecision = ResearchConfidenceDecision.Approved;
        legacy.RiskOnlyAssessmentJson = null;
        legacy.FullPipelineAssessmentJson = null;
        legacy.FullPipelineFinancialRiskDecision = null;
        Assert.Equal(
            ResearchFinalPipelineDecision.Approved,
            StrategyLabRiskObserver.ResolveFinalDecision(legacy));
    }

    private static StrategyResearchCandidate ApprovedWinner(string fingerprint)
    {
        var t = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new StrategyResearchCandidate
        {
            StrategyLabRunId = 1,
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            Direction = TradeDirection.Long,
            SetupFingerprint = fingerprint,
            SetupDetectedAtUtc = t,
            ProposedEntryTimeUtc = t,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            RewardRisk = 2m,
            RawRMultiple = 1m,
            ExitOutcome = ResearchExitOutcome.TargetHit,
            NetResult = ResearchNetResult.Profitable,
            RawOutcomeStatus = RawOutcomeStatus.Winner,
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawExitTimeUtc = t.AddHours(1),
            ConfidenceDecision = ResearchConfidenceDecision.Approved,
            ConfidenceScore = 90m,
            RawExitPrice = 102m,
            CreatedAtUtc = t
        };
    }

    private static StrategyResearchCandidate ApprovedLoser(string fingerprint)
    {
        var c = ApprovedWinner(fingerprint);
        c.RawRMultiple = -1m;
        c.ExitOutcome = ResearchExitOutcome.StopHit;
        c.NetResult = ResearchNetResult.Losing;
        c.RawOutcomeStatus = RawOutcomeStatus.Loser;
        c.RawExitPrice = c.StopLoss;
        return c;
    }

    private static RiskProfileSnapshotDto Snapshot(decimal maxDrawdown) =>
        new()
        {
            RiskProfileId = 1,
            RiskProfileName = "Test",
            RiskProfileSource = RiskProfileSources.Custom,
            RiskProfileSnapshotId = "ind-paths",
            ExposureSemanticsVersion = ExposureSemanticsVersion.ExplicitFuturesExposureV2,
            RiskPerTradePercent = 1m,
            MaxLeverage = 10m,
            PreferredLeverage = 10m,
            MaxMarginUsagePerSymbolPercent = 50m,
            MaxDailyLossPercent = 20m,
            MaxDrawdownPercent = maxDrawdown,
            MaxConcurrentPositions = 5,
            MinimumRewardRisk = 1m,
            ObservationalRiskScoreThreshold = 50m
        };

    private static RiskRuleSet Rules() =>
        new()
        {
            MaxRiskPerTradePercent = 1m,
            MaxDailyLossPercent = 20m,
            MaxWeeklyLossPercent = 50m,
            MaxOpenPositions = 5,
            MaxExposurePerSymbolPercent = 80m,
            MaxTotalExposurePercent = 100m,
            MaxCorrelatedExposurePercent = 100m,
            MaxConsecutiveLosses = 20,
            MinConfidenceScore = 0m,
            MaxSpreadPercent = 1m,
            MaxAtrPercent = 10m,
            EmergencyStopEnabled = false,
            RequireStopLoss = true,
            MinRewardRiskRatio = 1m
        };
}
