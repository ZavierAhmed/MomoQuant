using MomoQuant.Application.Risk;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public sealed class StrategyLabRiskObservationTests
{
    private readonly StrategyLabRiskObserver _observer = new();

    [Fact]
    public void Futures_sizing_formulas_match_spec_example()
    {
        // Equity 10,000; risk 1%; stop 1% â†’ notional ~100%; at 5x assessment â†’ margin ~20%
        var result = FuturesSizingCalculator.Calculate(
            entryPrice: 100m,
            stopLoss: 99m,
            targetPrice: 102m,
            assessmentEquity: 10000m,
            riskPerTradePercent: 1m,
            maxLeverage: 10m,
            preferredLeverage: 5m,
            takerFeeRate: 0.0004m);

        Assert.Equal(100m, result.RiskAmount);
        Assert.Equal(1m, result.RiskAtStopPercent);
        Assert.NotNull(result.PositionNotional);
        Assert.InRange(result.NotionalExposurePercent!.Value, 99.9m, 100.1m);
        Assert.Equal(5m, result.AssessmentLeverage);
        Assert.NotNull(result.MarginUsagePercent);
        Assert.InRange(result.MarginUsagePercent!.Value, 19.9m, 20.1m);
        Assert.Equal(result.MinimumRequiredLeverage, result.NotionalExposurePercent / 100m);
    }

    [Fact]
    public void Notional_above_100_percent_is_not_auto_rejected_when_only_margin_limit_enabled()
    {
        var candidate = ValidCandidate();
        candidate.StopLoss = 99.866m; // ~0.134% stop â†’ high notional at 0.5% risk
        var snapshot = ExplicitFuturesSnapshot(
            riskPerTrade: 0.5m,
            maxLeverage: 10m,
            maxMarginPerSymbol: 40m,
            maxNotionalPerSymbol: null);

        var result = _observer.Evaluate(
            candidate,
            snapshot,
            Rules(0.5m, 80m),
            new ChronologicalShadowPortfolio("RiskOnly", 10000m, StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m)),
            90m,
            10000m,
            0.0004m,
            ResearchConfidenceDecision.Approved);

        Assert.True(result.Sizing.NotionalExposurePercent > 100m);
        Assert.DoesNotContain(result.FailedRuleKeys, k => k.Contains("Notional", StringComparison.OrdinalIgnoreCase));
        var marginRule = result.RuleResults.Single(r => r.RuleKey == "MaxMarginUsagePerSymbolPercent");
        Assert.Equal(nameof(RiskRuleResultStatus.Passed), marginRule.Status);
        Assert.Equal(ResearchHardRuleComplianceDecision.Compliant, result.HardRuleComplianceDecision);
    }

    [Fact]
    public void Disabled_notional_rule_is_not_applicable()
    {
        var result = _observer.Evaluate(
            ValidCandidate(),
            ExplicitFuturesSnapshot(1m, 20m, maxMarginPerSymbol: 50m, maxNotionalPerSymbol: null),
            Rules(1m, 80m),
            new ChronologicalShadowPortfolio("RiskOnly", 10000m, StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m)),
            90m,
            10000m,
            0.0004m,
            ResearchConfidenceDecision.Approved);

        Assert.Contains(result.RuleResults, r =>
            r.RuleKey == "MaxNotionalExposurePerSymbolPercent"
            && r.Status == nameof(RiskRuleResultStatus.NotApplicable));
    }

    [Fact]
    public void Legacy_ambiguous_does_not_enforce_old_exposure_limits()
    {
        var snapshot = new RiskProfileSnapshotDto
        {
            RiskProfileId = 1,
            RiskProfileName = "Legacy",
            RiskProfileSource = RiskProfileSources.Saved,
            RiskProfileSnapshotId = "snap-legacy",
            ExposureSemanticsVersion = ExposureSemanticsVersion.LegacyAmbiguous,
            RiskPerTradePercent = 0.5m,
            MaxLeverage = 10m,
            LegacyMaxExposurePerSymbolPercent = 25m,
            LegacyMaxTotalExposurePercent = 25m,
            MaxNotionalExposurePerSymbolPercent = null,
            MaxMarginUsagePerSymbolPercent = null,
            MaxDailyLossPercent = 5m,
            MaxDrawdownPercent = 20m,
            MaxConcurrentPositions = 5,
            MinimumRewardRisk = 1m,
            ObservationalRiskScoreThreshold = 50m
        };

        var candidate = ValidCandidate();
        candidate.StopLoss = 99.866m;
        var result = _observer.Evaluate(
            candidate,
            snapshot,
            Rules(0.5m, 80m),
            new ChronologicalShadowPortfolio("RiskOnly", 10000m, StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m)),
            90m,
            10000m,
            0.0004m,
            ResearchConfidenceDecision.Approved);

        Assert.DoesNotContain(result.FailedRuleKeys, k =>
            k is "MaxExposurePerSymbolPercent" or "MaxTotalExposurePercent"
                or "MaxNotionalExposurePerSymbolPercent" or "MaxMarginUsagePerSymbolPercent");
        Assert.Contains(result.RuleResults, r => r.RuleKey == "LegacyExposureSemantics");
    }

    [Fact]
    public void Score_can_pass_while_hard_rules_fail()
    {
        var snapshot = ExplicitFuturesSnapshot(0.5m, 10m, maxMarginPerSymbol: 1m, maxNotionalPerSymbol: null);
        snapshot = WithThreshold(snapshot, 30m);

        var candidate = ValidCandidate();
        candidate.StopLoss = 99.866m;
        var result = _observer.Evaluate(
            candidate,
            snapshot,
            Rules(0.5m, 80m),
            new ChronologicalShadowPortfolio("RiskOnly", 10000m, StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m)),
            90m,
            10000m,
            0.0004m,
            ResearchConfidenceDecision.Approved);

        Assert.Equal(ResearchRiskScoreDecision.Passed, result.RiskScoreDecision);
        Assert.Equal(ResearchHardRuleComplianceDecision.NonCompliant, result.HardRuleComplianceDecision);
        Assert.Equal(ResearchRiskDecision.Rejected, result.FinancialRiskDecision);
        Assert.Contains("MaxMarginUsagePerSymbolPercent", result.FailedRuleKeys);
    }

    [Fact]
    public void Confidence_rejection_does_not_force_financial_risk_rejection()
    {
        var candidate = ValidCandidate();
        var snapshot = ExplicitFuturesSnapshot(1m, 20m, maxMarginPerSymbol: 50m, maxNotionalPerSymbol: null);
        snapshot = WithPolicy(snapshot, 80m);

        var result = _observer.Evaluate(
            candidate,
            snapshot,
            Rules(1m, 80m),
            new ChronologicalShadowPortfolio("RiskOnly", 10000m, StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m)),
            observedConfidenceScore: 62m,
            assessmentBalance: 10000m,
            takerFeeRate: 0.0004m,
            confidenceDecision: ResearchConfidenceDecision.Rejected);

        Assert.Equal(ResearchRiskDecision.Approved, result.FinancialRiskDecision);
        Assert.Equal(ResearchRiskPolicyEligibilityDecision.Ineligible, result.PolicyDecision);
        Assert.Contains(FinalPipelineRejectionSources.Confidence, result.FinalPipelineRejectionSources);
        Assert.Contains(FinalPipelineRejectionSources.RiskPolicy, result.FinalPipelineRejectionSources);
        Assert.DoesNotContain(FinalPipelineRejectionSources.FinancialRisk, result.FinalPipelineRejectionSources);
    }

    [Fact]
    public void Chronology_keeps_position_open_until_exit()
    {
        var entry = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var exit = entry.AddHours(5);
        var mid = entry.AddHours(2);

        var first = ValidCandidate();
        first.SetupFingerprint = "a";
        first.ProposedEntryTimeUtc = entry;
        first.RawExitTimeUtc = exit;
        first.RawRMultiple = -1m;
        first.ExitOutcome = ResearchExitOutcome.StopHit;
        first.NetResult = ResearchNetResult.Losing;
        first.RawOutcomeStatus = RawOutcomeStatus.Loser;
        first.CandidateStatus = StrategyResearchCandidateStatus.Closed;
        first.ConfidenceDecision = ResearchConfidenceDecision.Approved;

        var second = ValidCandidate();
        second.SetupFingerprint = "b";
        second.ProposedEntryTimeUtc = mid;
        second.RawExitTimeUtc = mid.AddHours(1);
        second.RawRMultiple = 1m;
        second.ExitOutcome = ResearchExitOutcome.TargetHit;
        second.NetResult = ResearchNetResult.Profitable;
        second.RawOutcomeStatus = RawOutcomeStatus.Winner;
        second.CandidateStatus = StrategyResearchCandidateStatus.Closed;
        second.ConfidenceDecision = ResearchConfidenceDecision.Approved;

        var snapshot = ExplicitFuturesSnapshot(1m, 20m, maxMarginPerSymbol: 50m, maxNotionalPerSymbol: null);
        var processed = ChronologicalShadowProcessor.Process(
            [first, second],
            snapshot,
            Rules(1m, 80m),
            _observer,
            10000m,
            StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m));

        Assert.Equal(ResearchRiskDecision.Approved, first.RiskDecision);
        Assert.True(second.ConcurrentPositionCount > 0);
        Assert.True(second.CurrentMarginUsagePercent > 0);
        Assert.True(second.ConcurrentRiskPercent > 0);
        Assert.True((second.DailyLossUsagePercent ?? 0m) < 0.01m); // loss not realized yet at mid entry
        Assert.Equal(2, processed.RiskOnlySummary.TradesAccepted);
    }

    [Fact]
    public void Daily_loss_resets_at_utc_midnight()
    {
        var day1 = new DateTime(2024, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2024, 1, 2, 1, 0, 0, DateTimeKind.Utc);

        var loser = ValidCandidate();
        loser.SetupFingerprint = "lose";
        loser.ProposedEntryTimeUtc = day1;
        loser.RawExitTimeUtc = day1.AddMinutes(30);
        loser.RawRMultiple = -1m;
        loser.CandidateStatus = StrategyResearchCandidateStatus.Closed;
        loser.RawOutcomeStatus = RawOutcomeStatus.Loser;
        loser.ConfidenceDecision = ResearchConfidenceDecision.Approved;

        var nextDay = ValidCandidate();
        nextDay.SetupFingerprint = "next";
        nextDay.ProposedEntryTimeUtc = day2;
        nextDay.RawExitTimeUtc = day2.AddHours(1);
        nextDay.RawRMultiple = 1m;
        nextDay.CandidateStatus = StrategyResearchCandidateStatus.Closed;
        nextDay.RawOutcomeStatus = RawOutcomeStatus.Winner;
        nextDay.ConfidenceDecision = ResearchConfidenceDecision.Approved;

        ChronologicalShadowProcessor.Process(
            [loser, nextDay],
            ExplicitFuturesSnapshot(1m, 20m, 50m, null),
            Rules(1m, 80m),
            _observer,
            10000m,
            StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m));

        Assert.Equal(0m, nextDay.DailyLossUsagePercent);
    }

    [Fact]
    public void Dual_shadow_paths_are_independent()
    {
        var entry = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var c = ValidCandidate();
        c.SetupFingerprint = "conf-reject";
        c.ProposedEntryTimeUtc = entry;
        c.RawExitTimeUtc = entry.AddHours(1);
        c.RawRMultiple = 1m;
        c.CandidateStatus = StrategyResearchCandidateStatus.Closed;
        c.RawOutcomeStatus = RawOutcomeStatus.Winner;
        c.ConfidenceScore = 50m;
        c.ConfidenceDecision = ResearchConfidenceDecision.Rejected;

        var snapshot = ExplicitFuturesSnapshot(1m, 20m, 50m, null);
        var result = ChronologicalShadowProcessor.Process(
            [c],
            snapshot,
            Rules(1m, 80m),
            _observer,
            10000m,
            StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m));

        Assert.Equal(ResearchRiskDecision.Approved, c.RiskDecision);
        Assert.Equal(ResearchRiskDecision.Approved, c.RiskOnlyFinancialRiskDecision);
        Assert.Equal(ShadowEntryDecision.Opened, c.RiskOnlyEntryDecision);
        Assert.Equal(ResearchRiskDecision.Approved, c.FullPipelineFinancialRiskDecision);
        Assert.Equal(ShadowEntryDecision.RejectedByConfidence, c.FullPipelineEntryDecision);
        Assert.Equal(ResearchFinalPipelineDecision.RejectedByConfidence, c.FinalPipelineDecision);
        Assert.Equal(1, result.RiskOnlySummary.TradesAccepted);
        Assert.Equal(0, result.FullPipelineSummary.TradesAccepted);
        Assert.NotNull(c.RiskOnlyAssessmentJson);
        Assert.NotNull(c.FullPipelineAssessmentJson);
        Assert.Equal(IndependentPathsVersions.Current, c.RiskPathAssessmentVersion);
    }

    [Fact]
    public void TargetHit_with_negative_net_pnl_is_Losing_net_result()
    {
        var candidate = ValidCandidate();
        var candles = new List<Candle>
        {
            CandleAt(0, 100m),
            CandleAt(1, high: 103m, low: 99.5m, close: 102m) // hits target 102
        };
        candidate.ProposedEntryPrice = 100m;
        candidate.StopLoss = 99m;
        candidate.Target1 = 102m;

        RawOutcomeSimulator.Simulate(new RawOutcomeSimulationRequest
        {
            Candidate = candidate,
            Candles = candles,
            EntryCandleIndex = 0,
            TakerFeeRate = 0.05m, // extreme fees â†’ target hit but net loss
            Quantity = 1m
        });

        Assert.Equal(ResearchExitOutcome.TargetHit, candidate.ExitOutcome);
        Assert.Equal(ResearchNetResult.Losing, candidate.NetResult);
        Assert.True(candidate.RawNetPnl < 0);
        Assert.Equal(RawOutcomeStatus.Winner, candidate.RawOutcomeStatus); // legacy label
    }

    [Fact]
    public void Profile_traceability_persists_on_candidate()
    {
        var snapshot = new RiskProfileSnapshotDto
        {
            RiskProfileId = 42,
            RiskProfileName = "Conservative",
            RiskProfileSource = RiskProfileSources.Saved,
            RiskProfileSnapshotId = "saved-42-abc",
            RiskProfileVersion = RiskObservationVersions.Current,
            ExposureSemanticsVersion = ExposureSemanticsVersion.ExplicitFuturesExposureV2,
            RiskPerTradePercent = 1m,
            MaxLeverage = 20m,
            MaxMarginUsagePerSymbolPercent = 50m,
            MaxDailyLossPercent = 5m,
            MaxDrawdownPercent = 20m,
            MaxConcurrentPositions = 5,
            MinimumRewardRisk = 1m,
            ObservationalRiskScoreThreshold = 50m
        };

        var candidate = ValidCandidate();
        var result = _observer.Evaluate(
            candidate,
            snapshot,
            Rules(1m, 80m),
            new ChronologicalShadowPortfolio("RiskOnly", 10000m, StrategyLabCostSnapshot.CreateDefault(0.0002m, 0.0004m)),
            90m,
            10000m,
            0.0004m,
            ResearchConfidenceDecision.Approved);
        StrategyLabRiskObserver.ApplyToCandidate(candidate, result, snapshot);

        Assert.Equal(42, candidate.RiskProfileId);
        Assert.Equal("Conservative", candidate.RiskProfileName);
        Assert.Equal(RiskProfileSources.Saved, candidate.RiskProfileSource);
        Assert.Equal("saved-42-abc", candidate.RiskProfileSnapshotId);
    }

    [Fact]
    public void RejectedByBoth_requires_independent_financial_failure()
    {
        var candidate = ValidCandidate();
        candidate.ConfidenceDecision = ResearchConfidenceDecision.Rejected;
        candidate.RiskDecision = ResearchRiskDecision.Approved;
        candidate.RiskPolicyEligibilityDecision = ResearchRiskPolicyEligibilityDecision.Ineligible;
        Assert.Equal(ResearchFinalPipelineDecision.RejectedByConfidence, StrategyLabRiskObserver.ResolveFinalDecision(candidate));

        candidate.RiskDecision = ResearchRiskDecision.Rejected;
        Assert.Equal(ResearchFinalPipelineDecision.RejectedByBoth, StrategyLabRiskObserver.ResolveFinalDecision(candidate));
    }

    [Fact]
    public void Half_percent_and_one_percent_produce_different_position_sizes()
    {
        var candidate = ValidCandidate();
        var half = _observer.BuildSizing(candidate, ExplicitFuturesSnapshot(0.5m, 10m, 50m, null), 10000m, 0.0004m);
        var one = _observer.BuildSizing(candidate, ExplicitFuturesSnapshot(1.0m, 10m, 50m, null), 10000m, 0.0004m);

        Assert.NotNull(half.Quantity);
        Assert.NotNull(one.Quantity);
        Assert.True(one.Quantity > half.Quantity);
        Assert.Equal(50m, half.RiskAmount);
        Assert.Equal(100m, one.RiskAmount);
    }

    private static Candle CandleAt(int minutes, decimal close, decimal? high = null, decimal? low = null)
    {
        var open = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes * 15);
        return new Candle
        {
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.M15,
            OpenTimeUtc = open,
            CloseTimeUtc = open.AddMinutes(15),
            Open = close,
            High = high ?? close,
            Low = low ?? close,
            Close = close,
            Volume = 1m
        };
    }

    private static StrategyResearchCandidate ValidCandidate() => new()
    {
        StrategyLabRunId = 1,
        StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
        Direction = TradeDirection.Long,
        SetupDetectedAtUtc = DateTime.UtcNow,
        ProposedEntryTimeUtc = DateTime.UtcNow,
        ProposedEntryPrice = 100m,
        StopLoss = 99m,
        Target1 = 102m,
        RewardRisk = 2m,
        CandidateStatus = StrategyResearchCandidateStatus.StrategyQualified,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static RiskProfileSnapshotDto ExplicitFuturesSnapshot(
        decimal riskPerTrade,
        decimal maxLeverage,
        decimal? maxMarginPerSymbol,
        decimal? maxNotionalPerSymbol) =>
        new()
        {
            RiskProfileId = 10,
            RiskProfileName = "Test",
            RiskProfileSource = RiskProfileSources.Custom,
            RiskProfileSnapshotId = "custom-test",
            ExposureSemanticsVersion = ExposureSemanticsVersion.ExplicitFuturesExposureV2,
            RiskPerTradePercent = riskPerTrade,
            MaxLeverage = maxLeverage,
            PreferredLeverage = maxLeverage,
            MaxMarginUsagePerSymbolPercent = maxMarginPerSymbol,
            MaxNotionalExposurePerSymbolPercent = maxNotionalPerSymbol,
            MaxDailyLossPercent = 5m,
            MaxDrawdownPercent = 20m,
            MaxConcurrentPositions = 5,
            MinimumRewardRisk = 1.0m,
            ObservationalRiskScoreThreshold = 50m
        };

    private static RiskProfileSnapshotDto WithPolicy(RiskProfileSnapshotDto s, decimal policyMin) =>
        new()
        {
            RiskProfileId = s.RiskProfileId,
            RiskProfileName = s.RiskProfileName,
            RiskProfileSource = s.RiskProfileSource,
            RiskProfileSnapshotId = s.RiskProfileSnapshotId,
            RiskProfileVersion = s.RiskProfileVersion,
            ExposureSemanticsVersion = s.ExposureSemanticsVersion,
            RiskPerTradePercent = s.RiskPerTradePercent,
            PreferredLeverage = s.PreferredLeverage,
            MaxLeverage = s.MaxLeverage,
            MaxNotionalExposurePerSymbolPercent = s.MaxNotionalExposurePerSymbolPercent,
            MaxTotalNotionalExposurePercent = s.MaxTotalNotionalExposurePercent,
            MaxMarginUsagePerSymbolPercent = s.MaxMarginUsagePerSymbolPercent,
            MaxTotalMarginUsagePercent = s.MaxTotalMarginUsagePercent,
            MaxConcurrentRiskPercent = s.MaxConcurrentRiskPercent,
            MaxDailyLossPercent = s.MaxDailyLossPercent,
            MaxDrawdownPercent = s.MaxDrawdownPercent,
            MaxConcurrentPositions = s.MaxConcurrentPositions,
            MinimumRewardRisk = s.MinimumRewardRisk,
            FeeEfficiencyHardLimitPercent = s.FeeEfficiencyHardLimitPercent,
            PolicyMinimumConfidence = policyMin,
            ObservationalRiskScoreThreshold = s.ObservationalRiskScoreThreshold,
            ActiveRules = s.ActiveRules
        };

    private static RiskProfileSnapshotDto WithThreshold(RiskProfileSnapshotDto s, decimal threshold) =>
        new()
        {
            RiskProfileId = s.RiskProfileId,
            RiskProfileName = s.RiskProfileName,
            RiskProfileSource = s.RiskProfileSource,
            RiskProfileSnapshotId = s.RiskProfileSnapshotId,
            RiskProfileVersion = s.RiskProfileVersion,
            ExposureSemanticsVersion = s.ExposureSemanticsVersion,
            RiskPerTradePercent = s.RiskPerTradePercent,
            PreferredLeverage = s.PreferredLeverage,
            MaxLeverage = s.MaxLeverage,
            MaxNotionalExposurePerSymbolPercent = s.MaxNotionalExposurePerSymbolPercent,
            MaxTotalNotionalExposurePercent = s.MaxTotalNotionalExposurePercent,
            MaxMarginUsagePerSymbolPercent = s.MaxMarginUsagePerSymbolPercent,
            MaxTotalMarginUsagePercent = s.MaxTotalMarginUsagePercent,
            MaxConcurrentRiskPercent = s.MaxConcurrentRiskPercent,
            MaxDailyLossPercent = s.MaxDailyLossPercent,
            MaxDrawdownPercent = s.MaxDrawdownPercent,
            MaxConcurrentPositions = s.MaxConcurrentPositions,
            MinimumRewardRisk = s.MinimumRewardRisk,
            FeeEfficiencyHardLimitPercent = s.FeeEfficiencyHardLimitPercent,
            PolicyMinimumConfidence = s.PolicyMinimumConfidence,
            ObservationalRiskScoreThreshold = threshold,
            ActiveRules = s.ActiveRules
        };

    private static RiskRuleSet Rules(decimal riskPerTrade, decimal minConfidence) =>
        new()
        {
            MaxRiskPerTradePercent = riskPerTrade,
            MaxDailyLossPercent = 5m,
            MaxWeeklyLossPercent = 20m,
            MaxOpenPositions = 5,
            MaxExposurePerSymbolPercent = 40m,
            MaxTotalExposurePercent = 80m,
            MaxCorrelatedExposurePercent = 80m,
            MaxConsecutiveLosses = 5,
            MinConfidenceScore = minConfidence,
            MaxSpreadPercent = 1m,
            MaxAtrPercent = 10m,
            EmergencyStopEnabled = false,
            RequireStopLoss = true,
            MinRewardRiskRatio = 1m
        };
}

