using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public sealed class StrategyLabGateAnalysisTests
{
    [Fact]
    public void Confidence_margin_is_score_minus_threshold()
    {
        var candidate = MakeCandidate();
        candidate.ConfidenceScore = 64m;
        candidate.ConfidenceThreshold = 70m;
        candidate.ConfidenceMargin = candidate.ConfidenceScore - candidate.ConfidenceThreshold;
        candidate.ConfidenceDecision = ResearchConfidenceDecision.Rejected;

        Assert.Equal(-6m, candidate.ConfidenceMargin);
        Assert.Equal(ResearchConfidenceDecision.Rejected, candidate.ConfidenceDecision);
    }

    [Fact]
    public void Gate_analysis_infers_threshold_from_legacy_reason_text()
    {
        var run = MakeRun(StrategyLabExecutionMode.StrategyPlusConfidenceObservation);
        var candidates = new List<StrategyResearchCandidate>
        {
            MakeCandidate(RawOutcomeStatus.Winner, confidenceScore: 70, decision: ResearchConfidenceDecision.Rejected, pnl: 5)
        };
        candidates[0].ConfidenceThreshold = null;
        candidates[0].ConfidenceMargin = null;
        candidates[0].ConfidenceReason = "Confidence 70.00 < 80.00";

        var analysis = StrategyLabGateAnalysisCalculator.Build(run, candidates);

        Assert.Equal(80m, analysis.ConfidenceSummary!.CurrentThreshold);
        Assert.Equal(1, analysis.ConfidenceRejectedWinners!.Count);
        Assert.Equal(-10m, analysis.ConfidenceRejectedWinners.AverageMarginBelowThreshold);
        Assert.Contains(analysis.ConfidenceThresholdSimulation, r => r.IsCurrentThreshold && r.Threshold == 80m);
    }

    [Fact]
    public void NotEvaluated_confidence_is_not_treated_as_zero_in_averages()
    {
        var run = MakeRun(StrategyLabExecutionMode.StrategyPlusConfidenceObservation);
        var candidates = new List<StrategyResearchCandidate>
        {
            MakeCandidate(RawOutcomeStatus.Winner, confidenceScore: 80, decision: ResearchConfidenceDecision.Approved),
            MakeCandidate(RawOutcomeStatus.Loser, confidenceScore: null, decision: ResearchConfidenceDecision.NotEvaluated),
            MakeCandidate(RawOutcomeStatus.Loser, confidenceScore: 40, decision: ResearchConfidenceDecision.Rejected)
        };

        var analysis = StrategyLabGateAnalysisCalculator.Build(run, candidates);

        // Evaluated scores only: 80 and 40 → 60. NotEvaluated null must not become 0.
        Assert.Equal(60m, analysis.ConfidenceSummary!.AverageScore);
        Assert.Equal(2, analysis.ConfidenceSummary.EvaluatedCount);
        Assert.Equal(40m, analysis.OverallWinnerLoserComparison.AverageLoserConfidence);
    }

    [Fact]
    public void Average_winner_and_loser_confidence_exclude_not_evaluated()
    {
        var run = MakeRun(StrategyLabExecutionMode.StrategyPlusConfidenceObservation);
        var candidates = new List<StrategyResearchCandidate>
        {
            MakeCandidate(RawOutcomeStatus.Winner, confidenceScore: 72, decision: ResearchConfidenceDecision.Rejected, pnl: 10),
            MakeCandidate(RawOutcomeStatus.Winner, confidenceScore: 68, decision: ResearchConfidenceDecision.Rejected, pnl: 5),
            MakeCandidate(RawOutcomeStatus.Loser, confidenceScore: 55, decision: ResearchConfidenceDecision.Rejected, pnl: -8),
            MakeCandidate(RawOutcomeStatus.Loser, confidenceScore: null, decision: ResearchConfidenceDecision.NotEvaluated, pnl: -3)
        };

        var analysis = StrategyLabGateAnalysisCalculator.Build(run, candidates);

        Assert.Equal(70m, analysis.OverallWinnerLoserComparison.AverageWinnerConfidence);
        Assert.Equal(55m, analysis.OverallWinnerLoserComparison.AverageLoserConfidence);
        Assert.Equal(2, analysis.ConfidenceRejectedWinners!.Count);
        Assert.Equal(70m, analysis.ConfidenceRejectedWinners.AverageScore);
        Assert.Equal(1, analysis.ConfidenceRejectedLosers!.Count);
        Assert.Equal(55m, analysis.ConfidenceRejectedLosers.AverageScore);
    }

    [Fact]
    public void Threshold_simulation_filters_and_calculates_pnl()
    {
        var run = MakeRun(StrategyLabExecutionMode.StrategyPlusConfidenceObservation);
        var candidates = new List<StrategyResearchCandidate>
        {
            MakeCandidate(RawOutcomeStatus.Winner, confidenceScore: 85, decision: ResearchConfidenceDecision.Approved, pnl: 20, threshold: 70),
            MakeCandidate(RawOutcomeStatus.Loser, confidenceScore: 75, decision: ResearchConfidenceDecision.Approved, pnl: -10, threshold: 70),
            MakeCandidate(RawOutcomeStatus.Winner, confidenceScore: 45, decision: ResearchConfidenceDecision.Rejected, pnl: 15, threshold: 70),
            MakeCandidate(RawOutcomeStatus.Loser, confidenceScore: 30, decision: ResearchConfidenceDecision.Rejected, pnl: -12, threshold: 70)
        };

        var analysis = StrategyLabGateAnalysisCalculator.Build(run, candidates);
        var at70 = analysis.ConfidenceThresholdSimulation.First(r => r.Threshold == 70m && r.IsCurrentThreshold);
        var at40 = analysis.ConfidenceThresholdSimulation.First(r => r.Threshold == 40m);

        Assert.Equal(2, at70.AcceptedCount);
        Assert.Equal(2, at70.RejectedCount);
        Assert.Equal(10m, at70.AcceptedNetPnl);
        Assert.Equal(3, at40.AcceptedCount);
        Assert.Equal(25m, at40.AcceptedNetPnl); // 20 - 10 + 15
    }

    [Fact]
    public void Risk_rejection_reason_aggregation_includes_rejected_winners()
    {
        var run = MakeRun(StrategyLabExecutionMode.StrategyPlusRiskObservation);
        var candidates = new List<StrategyResearchCandidate>
        {
            MakeRiskCandidate(RawOutcomeStatus.Winner, rejected: true, reasonKey: "StopTooWide", pnl: 12, riskScore: 40),
            MakeRiskCandidate(RawOutcomeStatus.Loser, rejected: true, reasonKey: "StopTooWide", pnl: -5, riskScore: 35),
            MakeRiskCandidate(RawOutcomeStatus.Winner, rejected: true, reasonKey: "MaxRisk", pnl: 8, riskScore: 30),
            MakeRiskCandidate(RawOutcomeStatus.Winner, rejected: false, reasonKey: null, pnl: 4, riskScore: 70)
        };

        var analysis = StrategyLabGateAnalysisCalculator.Build(run, candidates);
        var stopTooWide = analysis.RiskReasonAnalysis.First(r => r.Reason == "StopTooWide");

        Assert.Equal(2, stopTooWide.RejectedCount);
        Assert.Equal(1, stopTooWide.WinnerCount);
        Assert.Equal(1, stopTooWide.LoserCount);
        Assert.Equal(7m, stopTooWide.HypotheticalNetPnl);
        Assert.Equal(2, analysis.RiskRejectedWinners!.Count);
        Assert.True(analysis.OverallWinnerLoserComparison.AverageWinnerRiskScore.HasValue);
        Assert.NotEqual(0m, analysis.OverallWinnerLoserComparison.AverageWinnerRiskScore);
    }

    [Fact]
    public void RiskApprovalScoreCalculator_is_deterministic_and_observational()
    {
        var context = new MomoQuant.Domain.Risk.RiskContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            SuggestedStopLoss = 95m,
            SuggestedTakeProfit = 110m,
            ConfidenceScore = 70m,
            AccountBalance = 10000m,
            DailyPnl = 0m,
            WeeklyPnl = 0m,
            OpenPositionCount = 0,
            OpenSymbolExposure = 0m,
            TotalExposure = 0m,
            ConsecutiveLosses = 0,
            EmergencyStopEnabled = false,
            EvaluationTimeUtc = DateTime.UtcNow,
            Rules =
            [
                new MomoQuant.Domain.Risk.RiskRule { RuleKey = RiskRuleKeys.MaxRiskPerTradePercent, IsEnabled = true, RuleValue = "1" },
                new MomoQuant.Domain.Risk.RiskRule { RuleKey = RiskRuleKeys.MinRewardRiskRatio, IsEnabled = true, RuleValue = "1.5" }
            ]
        };
        var evaluation = new MomoQuant.Domain.Risk.RiskEvaluationResult
        {
            Decision = RiskDecisionType.Approved,
            Reason = "OK",
            ApprovedRiskPercent = 1m,
            PositionSize = 10m
        };

        var first = RiskApprovalScoreCalculator.Calculate(context, evaluation, rewardRisk: 2m);
        var second = RiskApprovalScoreCalculator.Calculate(context, evaluation, rewardRisk: 2m);

        Assert.Equal(first.Score, second.Score);
        Assert.InRange(first.Score, 0m, 100m);
        Assert.True(first.ComponentScores.ContainsKey("ConfidenceHeadroom"));
        Assert.True(first.ComponentScores.ContainsKey("RewardRiskHeadroom"));
    }

    private static StrategyLabRun MakeRun(StrategyLabExecutionMode mode) => new()
    {
        Id = 1,
        Name = "test",
        StrategyCode = "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM",
        StrategyVersion = "1.0.0",
        ExchangeId = 1,
        SymbolId = 1,
        Symbol = "BNBUSDT",
        Timeframe = "15m",
        FromUtc = DateTime.UtcNow.AddDays(-7),
        ToUtc = DateTime.UtcNow,
        ExecutionMode = mode,
        Status = StrategyLabRunStatus.Completed,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static StrategyResearchCandidate MakeCandidate(
        RawOutcomeStatus outcome = RawOutcomeStatus.Winner,
        decimal? confidenceScore = 70,
        ResearchConfidenceDecision decision = ResearchConfidenceDecision.Approved,
        decimal pnl = 1,
        decimal threshold = 70) =>
        new()
        {
            StrategyLabRunId = 1,
            StrategyCode = "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM",
            Direction = TradeDirection.Long,
            SetupDetectedAtUtc = DateTime.UtcNow,
            ProposedEntryTimeUtc = DateTime.UtcNow,
            ProposedEntryPrice = 100,
            StopLoss = 95,
            Target1 = 110,
            RewardRisk = 2,
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = outcome,
            RawNetPnl = pnl,
            RawRMultiple = pnl > 0 ? 1 : -1,
            ConfidenceScore = confidenceScore,
            ConfidenceThreshold = confidenceScore.HasValue ? threshold : null,
            ConfidenceMargin = confidenceScore.HasValue ? confidenceScore - threshold : null,
            ConfidenceDecision = decision,
            RiskDecision = ResearchRiskDecision.NotEvaluated,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static StrategyResearchCandidate MakeRiskCandidate(
        RawOutcomeStatus outcome,
        bool rejected,
        string? reasonKey,
        decimal pnl,
        decimal riskScore) =>
        new()
        {
            StrategyLabRunId = 1,
            StrategyCode = "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM",
            Direction = TradeDirection.Long,
            SetupDetectedAtUtc = DateTime.UtcNow,
            ProposedEntryTimeUtc = DateTime.UtcNow,
            ProposedEntryPrice = 100,
            StopLoss = 95,
            Target1 = 110,
            RewardRisk = 2,
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = outcome,
            RawNetPnl = pnl,
            RawRMultiple = pnl > 0 ? 1 : -1,
            ConfidenceDecision = ResearchConfidenceDecision.NotEvaluated,
            RiskDecision = rejected ? ResearchRiskDecision.Rejected : ResearchRiskDecision.Approved,
            RiskReason = reasonKey ?? "OK",
            RiskRejectedRuleKey = reasonKey,
            RiskScore = riskScore,
            RiskThreshold = 50,
            RiskMargin = riskScore - 50,
            CreatedAtUtc = DateTime.UtcNow
        };
}
