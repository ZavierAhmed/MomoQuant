using System.Text.Json;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0D WP16–25 — trial metric snapshots, null-honest guardrails, v2 score,
/// snapshot-based ranking, explicit version routing, dual statuses, and reconciliation.
/// </summary>
public class ValidationLab230DTrialMetricsTests
{
    private static readonly DateTime ValidationStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ValidationTrialMetricsCalculator RealCalculator() => new(
        new ValidationPathMetricInputBuilder(),
        new ValidationRiskBasisService(),
        new ValidationRiskBasisStatusReducer());

    private static ValidationExperiment Experiment(string version = ValidationMetricsContract.VersionV132) => new()
    {
        Id = 42,
        ValidationMetricsVersion = version,
        TrainingCandleCount = 1000,
        ValidationStartUtc = ValidationStart,
        DraftConfigurationJson = """{"takerFeeRate":0.0004,"makerFeeRate":0.0002,"slippagePercent":0}"""
    };

    private static ValidationQualificationProfile Profile(int minClosed = 5) => new()
    {
        MinimumTrainingClosedTrades = minClosed,
        MinimumTrainingProfitFactor = 1.10m,
        MinimumTrainingNetExpectancyR = 0m,
        MaximumTrainingDrawdownPercent = 25m
    };

    private static ValidationParameterTrial Trial(int number, string fingerprint) => new()
    {
        ValidationExperimentId = 42,
        TrialNumber = number,
        ParameterFingerprint = fingerprint,
        ParameterSnapshotJson = "{}",
        Status = ValidationTrialStatus.Running,
        GuardrailDecision = "NotEvaluated"
    };

    private static StrategyResearchCandidate Candidate(
        string fingerprint,
        decimal entry,
        decimal stop,
        decimal exit,
        RawOutcomeStatus outcome,
        decimal? rawGross,
        decimal? rawNet,
        decimal? rawR,
        decimal? riskAmount) => new()
    {
        SetupFingerprint = fingerprint,
        Direction = TradeDirection.Long,
        SetupDetectedAtUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        ProposedEntryTimeUtc = new DateTime(2025, 6, 1, 0, 5, 0, DateTimeKind.Utc),
        ProposedEntryPrice = entry,
        StopLoss = stop,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = outcome,
        RawExitTimeUtc = new DateTime(2025, 6, 1, 4, 0, 0, DateTimeKind.Utc),
        RawExitPrice = exit,
        RawGrossPnl = rawGross,
        RawNetPnl = rawNet,
        RawRMultiple = rawR,
        RiskAmount = riskAmount,
        ProposedPositionSize = 1m
    };

    /// <summary>
    /// Trial A (adversarial): persisted candidate fields claim four +2.9R winners and one small loser,
    /// but the actual prices (entry 100, stop 99, exit 99) are five one-unit losses. Legacy trusts the
    /// persisted fields; ValidationMetrics/v1.3.2 recomputes from prices with frozen taker fees.
    /// </summary>
    private static IReadOnlyList<StrategyResearchCandidate> BuildTrialACandidates() =>
    [
        Candidate("A1", 100m, 99m, 99m, RawOutcomeStatus.Winner, 3m, 2.9m, 3m, 1m),
        Candidate("A2", 100m, 99m, 99m, RawOutcomeStatus.Winner, 3m, 2.9m, 3m, 1m),
        Candidate("A3", 100m, 99m, 99m, RawOutcomeStatus.Winner, 3m, 2.9m, 3m, 1m),
        Candidate("A4", 100m, 99m, 99m, RawOutcomeStatus.Winner, 3m, 2.9m, 3m, 1m),
        Candidate("A5", 100m, 99m, 99m, RawOutcomeStatus.Loser, -1m, -1.05m, -1m, 1m)
    ];

    /// <summary>
    /// Trial B (honest): four genuine winners (exit 102) and one genuine loser (exit 99); persisted
    /// candidate fields are exactly consistent with the one-unit price economics at frozen taker fees.
    /// </summary>
    private static IReadOnlyList<StrategyResearchCandidate> BuildTrialBCandidates() =>
    [
        Candidate("B1", 100m, 99m, 102m, RawOutcomeStatus.Winner, 2m, 1.9192m, 2m, 1m),
        Candidate("B2", 100m, 99m, 102m, RawOutcomeStatus.Winner, 2m, 1.9192m, 2m, 1m),
        Candidate("B3", 100m, 99m, 102m, RawOutcomeStatus.Winner, 2m, 1.9192m, 2m, 1m),
        Candidate("B4", 100m, 99m, 102m, RawOutcomeStatus.Winner, 2m, 1.9192m, 2m, 1m),
        Candidate("B5", 100m, 99m, 99m, RawOutcomeStatus.Loser, -1m, -1.0796m, -1m, 1m)
    ];

    // ------------------------------------------------------------------
    // WP16–17 — calculator, snapshot, fingerprint
    // ------------------------------------------------------------------

    [Fact]
    public void Calculator_V132_ComputesExactFixtureMetrics_FromPathInputs()
    {
        var result = RealCalculator().Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile(), "fp-b");

        var m = result.Metrics;
        Assert.Equal(ValidationMetricsContract.VersionV132, m.MetricsVersion);
        Assert.Equal(5, m.CandidatePopulationCount);
        Assert.Equal(5, m.BoundaryEligibleCandidateCount);
        Assert.Equal(5, m.IncludedPathInputCount);
        Assert.Equal(0, m.ExcludedPathInputCount);
        Assert.Equal(5, m.ClosedOutcomePopulationCount);
        Assert.Equal(5, m.MonetaryPnlPopulationCount);
        Assert.Equal(5, m.GrossRPopulationCount);
        Assert.Equal(5, m.NetRPopulationCount);
        Assert.Equal(4, m.WinnerPopulationCount);
        Assert.Equal(1, m.LoserPopulationCount);

        // Normalized one-unit economics at frozen taker fees (0.0004 per leg, no slippage):
        // winner: gross +2, fees 0.04+0.0408, net +1.9192; loser: gross -1, fees 0.04+0.0396, net -1.0796
        Assert.Equal(7m, m.GrossPnl);
        Assert.Equal(0.4028m, m.TransactionCosts);
        Assert.Equal(6.5972m, m.NetPnl);
        Assert.Equal(8m, m.GrossProfit);
        Assert.Equal(1m, m.GrossLoss);
        Assert.Equal(7.6768m, m.NetProfit);
        Assert.Equal(1.0796m, m.NetLoss);
        Assert.Equal(1.4m, m.GrossExpectancyR);
        Assert.Equal(1.31944m, m.NetExpectancyR);
        Assert.Equal(8m, m.GrossProfitFactor);
        Assert.Equal(Math.Round(7.6768m / 1.0796m, 8), m.NetProfitFactor);
        Assert.Equal(ValidationRiskBasisValidationStatus.Valid, m.IncludedPopulationRiskStatus);
        Assert.Equal(ValidationRiskBasisValidationStatus.Valid, m.CompletePathInputIntegrityStatus);

        // ValidationTrainingScore/v2: 19.7916 + 20 + 0 (no drawdown) + 15 + 8.99 + 10 = 73.78
        Assert.Equal(ValidationTrainingScoreVersions.V2, result.Score.Version);
        Assert.Equal(19.79m, result.Score.ExpectancyQuality);
        Assert.Equal(20m, result.Score.ProfitFactorQuality);
        Assert.Equal(0m, result.Score.DrawdownQuality);
        Assert.Equal(15m, result.Score.SampleSufficiency);
        Assert.Equal(8.99m, result.Score.CostEfficiency);
        Assert.Equal(10m, result.Score.OpportunityStability);
        Assert.Equal(73.78m, result.Score.Total);

        Assert.True(result.Guardrails.Passed);
        Assert.True(result.Guardrails.IsRankEligible);
        Assert.Empty(result.Guardrails.FailureCodes);
        Assert.Equal(5.035m, result.FeeImpactPercent);

        Assert.Equal(ValidationTrialMetricSnapshot.Version, result.Snapshot.SnapshotVersion);
        Assert.Equal("fp-b", result.Snapshot.ParameterFingerprint);
        Assert.Equal(0.0004m, result.Snapshot.CostModel.EntryFeeRate);
        Assert.Equal(0.0004m, result.Snapshot.CostModel.ExitFeeRate);
        Assert.NotEqual("{}", result.SnapshotJson);
        Assert.Equal(ValidationTrialMetricsCalculator.ComputeSha256(result.SnapshotJson), result.MetricFingerprint);
    }

    [Fact]
    public void Calculator_BoundaryCensoredCandidates_ExcludedFromMetrics_ButCounted()
    {
        var candidates = BuildTrialBCandidates().ToList();
        var boundary = Candidate("BX", 100m, 99m, 102m, RawOutcomeStatus.Winner, 2m, 1.9192m, 2m, 1m);
        // Straddles the training/validation boundary: detected before, exits after ValidationStart.
        boundary.SetupDetectedAtUtc = ValidationStart.AddHours(-2);
        boundary.RawExitTimeUtc = ValidationStart.AddHours(2);
        candidates.Add(boundary);

        var result = RealCalculator().Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, candidates, Profile());

        Assert.Equal(1, result.BoundaryCensoredCount);
        Assert.Equal(5, result.BoundaryEligibleCandidateCount);
        Assert.Equal(6, result.Metrics.CandidatePopulationCount);
        Assert.Equal(5, result.Metrics.BoundaryEligibleCandidateCount);
        Assert.Equal(1, result.Metrics.BoundaryCensoredCount);
        // Metrics identical to the 5-candidate fixture — the boundary trade never leaks in.
        Assert.Equal(6.5972m, result.Metrics.NetPnl);
        Assert.Equal(1.31944m, result.Metrics.NetExpectancyR);
    }

    [Fact]
    public void Calculator_Fingerprint_IsDeterministic_AndSensitiveToInputs()
    {
        var calculator = RealCalculator();
        var first = calculator.Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile(), "fp-b");
        var second = calculator.Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile(), "fp-b");

        Assert.Equal(first.SnapshotJson, second.SnapshotJson);
        Assert.Equal(first.MetricFingerprint, second.MetricFingerprint);
        Assert.Equal(64, first.MetricFingerprint.Length);
        Assert.Equal(first.MetricFingerprint, first.MetricFingerprint.ToLowerInvariant());

        var altered = BuildTrialBCandidates().ToList();
        altered[0].RawExitPrice = 103m;
        var third = calculator.Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, altered, Profile(), "fp-b");
        Assert.NotEqual(first.MetricFingerprint, third.MetricFingerprint);
    }

    [Fact]
    public void ApplyToTrial_PersistsFullSnapshotFields()
    {
        var result = RealCalculator().Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile(), "fp-b");
        var trial = Trial(1, "fp-b");

        ValidationTrialMetricsCalculator.ApplyToTrial(trial, result);

        Assert.Equal(ValidationTrialStatus.Completed, trial.Status);
        Assert.Equal("Passed", trial.GuardrailDecision);
        Assert.Equal(result.SnapshotJson, trial.TrialMetricSnapshotJson);
        Assert.Equal(result.MetricFingerprint, trial.TrialMetricFingerprint);
        Assert.Equal(ValidationMetricsContract.VersionV132, trial.TrialMetricsVersion);
        Assert.Equal(ValidationTrainingScoreVersions.V2, trial.TrainingScoreVersion);
        Assert.NotNull(trial.GuardrailEvaluationJson);
        Assert.Equal(5, trial.CandidatePopulationCount);
        Assert.Equal(5, trial.BoundaryEligibleCandidateCount);
        Assert.Equal(5, trial.IncludedPathInputCount);
        Assert.Equal(0, trial.ExcludedPathInputCount);
        Assert.Equal(5, trial.ClosedOutcomePopulationCount);
        Assert.Equal(5, trial.MonetaryPnlPopulationCount);
        Assert.Equal(5, trial.GrossRPopulationCount);
        Assert.Equal(5, trial.NetRPopulationCount);
        Assert.Equal(ValidationRiskBasisValidationStatus.Valid, trial.IncludedPopulationRiskStatus);
        Assert.Equal(ValidationRiskBasisValidationStatus.Valid, trial.CompletePathInputIntegrityStatus);
        Assert.Equal(ValidationTrialRankEligibility.Eligible, trial.TrialRankEligibility);
        Assert.Null(trial.RankIneligibleReasonsJson);
        Assert.Equal(73.78m, trial.TrainingScore);
        Assert.Equal(1.31944m, trial.NetExpectancyR);
        Assert.Equal(4, trial.WinnerCount);
        Assert.Equal(1, trial.LoserCount);
    }

    // ------------------------------------------------------------------
    // WP18 — guardrails without "?? 0m"
    // ------------------------------------------------------------------

    [Fact]
    public void Guardrails_NullNetExpectancy_NotEvaluated_NeverCoalescedToPassingZero()
    {
        // Legacy defect: (null ?? 0m) >= MinimumTrainingNetExpectancyR (0) silently passed.
        var metrics = new LayerSegmentMetrics
        {
            MetricsVersion = ValidationMetricsContract.VersionV132,
            ClosedOutcomePopulationCount = 40,
            NetProfitFactor = 1.5m,
            NetExpectancyR = null
        };

        var evaluation = ValidationGuardrailEvaluator.Evaluate(metrics, Profile(minClosed: 30));

        var ner = evaluation.Results.Single(r => r.GuardrailKey == "MinimumTrainingNetExpectancyR");
        Assert.Equal(ValidationGuardrailOutcome.NotEvaluated, ner.Outcome);
        Assert.True(ner.IsMandatory);
        Assert.False(evaluation.Passed);
        Assert.False(evaluation.IsRankEligible);
        Assert.Contains(ValidationGuardrailFailureCodes.NetExpectancyNotEvaluated, evaluation.FailureCodes);
    }

    [Fact]
    public void Guardrails_NullProfitFactor_NotEvaluated_UnlessInfinite()
    {
        var notEvaluated = ValidationGuardrailEvaluator.Evaluate(new LayerSegmentMetrics
        {
            MetricsVersion = ValidationMetricsContract.VersionV132,
            ClosedOutcomePopulationCount = 40,
            NetExpectancyR = 0.5m,
            NetProfitFactor = null
        }, Profile(minClosed: 30));
        Assert.False(notEvaluated.Passed);
        Assert.Contains(ValidationGuardrailFailureCodes.ProfitFactorNotEvaluated, notEvaluated.FailureCodes);

        var infinite = ValidationGuardrailEvaluator.Evaluate(new LayerSegmentMetrics
        {
            MetricsVersion = ValidationMetricsContract.VersionV132,
            ClosedOutcomePopulationCount = 40,
            NetExpectancyR = 0.5m,
            NetProfitFactor = null,
            NetProfitFactorStatus = ProfitFactorStatus.Infinity
        }, Profile(minClosed: 30));
        Assert.True(infinite.Passed);
        Assert.True(infinite.IsRankEligible);
        var pf = infinite.Results.Single(r => r.GuardrailKey == "MinimumTrainingProfitFactor");
        Assert.Equal(ValidationGuardrailOutcome.Passed, pf.Outcome);
        Assert.Equal("Infinity", pf.ActualValue);
    }

    [Fact]
    public void Guardrails_V132_NullDrawdown_IsNotApplicable_NotAFailure()
    {
        var evaluation = ValidationGuardrailEvaluator.Evaluate(new LayerSegmentMetrics
        {
            MetricsVersion = ValidationMetricsContract.VersionV132,
            ClosedOutcomePopulationCount = 40,
            NetExpectancyR = 0.5m,
            NetProfitFactor = 1.5m,
            MaximumRealizedDrawdownPercent = null
        }, Profile(minClosed: 30));

        var dd = evaluation.Results.Single(r => r.GuardrailKey == "MaximumTrainingDrawdownPercent");
        Assert.Equal(ValidationGuardrailOutcome.NotApplicable, dd.Outcome);
        Assert.False(dd.IsMandatory);
        Assert.Null(dd.FailureCode);
        Assert.True(evaluation.Passed);
    }

    [Fact]
    public void Guardrails_ExplicitFailureCodes_ForEachBreach()
    {
        var evaluation = ValidationGuardrailEvaluator.Evaluate(new LayerSegmentMetrics
        {
            MetricsVersion = ValidationMetricsContract.VersionV132,
            ClosedOutcomePopulationCount = 3,
            NetProfitFactor = 0.5m,
            NetExpectancyR = -0.5m,
            MaximumRealizedDrawdownPercent = 30m
        }, Profile(minClosed: 30));

        Assert.False(evaluation.Passed);
        Assert.Equal("Failed", evaluation.Decision);
        Assert.Contains(ValidationGuardrailFailureCodes.ClosedTradesBelowMinimum, evaluation.FailureCodes);
        Assert.Contains(ValidationGuardrailFailureCodes.ProfitFactorBelowMinimum, evaluation.FailureCodes);
        Assert.Contains(ValidationGuardrailFailureCodes.NetExpectancyBelowMinimum, evaluation.FailureCodes);
        Assert.Contains(ValidationGuardrailFailureCodes.MaxDrawdownExceeded, evaluation.FailureCodes);
    }

    // ------------------------------------------------------------------
    // WP24–25 — dual statuses
    // ------------------------------------------------------------------

    [Fact]
    public void DualStatuses_IncludedRiskValid_CompleteIntegrityInvalid_WhenAnyPathInputExcluded()
    {
        var risk = new ValidationRiskBasisService();
        var reducer = new ValidationRiskBasisStatusReducer();
        var trades = BuildDualStatusFixture();

        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 3, 3, 0, ValidationLayerType.RawStrategy, risk, reducer);

        Assert.Equal(2, metrics.IncludedPathInputCount);
        Assert.Equal(1, metrics.ExcludedPathInputCount);
        Assert.Equal(ValidationRiskBasisValidationStatus.Valid, metrics.IncludedPopulationRiskStatus);
        Assert.Equal(
            ValidationRiskBasisValidationStatus.InvalidRiskBasis,
            metrics.CompletePathInputIntegrityStatus);
        // Legacy aggregate keeps included-only semantics.
        Assert.Equal(metrics.IncludedPopulationRiskStatus, metrics.RiskBasisValidationStatus);
    }

    [Fact]
    public void DualStatuses_AreOrderIndependent()
    {
        var risk = new ValidationRiskBasisService();
        var reducer = new ValidationRiskBasisStatusReducer();
        var trades = BuildDualStatusFixture();

        var orderings = new[]
        {
            new[] { trades[0], trades[1], trades[2] },
            new[] { trades[2], trades[1], trades[0] },
            new[] { trades[1], trades[2], trades[0] },
            new[] { trades[2], trades[0], trades[1] },
            new[] { trades[0], trades[2], trades[1] },
            new[] { trades[1], trades[0], trades[2] }
        };

        foreach (var ordering in orderings)
        {
            var metrics = ValidationMetricsContract.FromPathTradesV132(
                ordering, 1000, 3, 3, 0, ValidationLayerType.RawStrategy, risk, reducer);
            Assert.Equal(ValidationRiskBasisValidationStatus.Valid, metrics.IncludedPopulationRiskStatus);
            Assert.Equal(
                ValidationRiskBasisValidationStatus.InvalidRiskBasis,
                metrics.CompletePathInputIntegrityStatus);
        }
    }

    [Fact]
    public void DualStatuses_AreFingerprinted()
    {
        var risk = new ValidationRiskBasisService();
        var reducer = new ValidationRiskBasisStatusReducer();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            BuildDualStatusFixture(), 1000, 3, 3, 0, ValidationLayerType.RawStrategy, risk, reducer);

        var fields = ValidationMetricsContract.BuildPathResultFingerprintFields(
            ValidationSegmentType.Training, ValidationLayerType.RawStrategy, metrics);
        Assert.Equal("Valid", fields["includedRiskStatus"]);
        Assert.Equal("InvalidRiskBasis", fields["completeIntegrityStatus"]);

        var baseFp = ValidationLabService.ParameterFingerprint(fields);
        var altered = new Dictionary<string, string>(fields) { ["completeIntegrityStatus"] = "Valid" };
        Assert.NotEqual(baseFp, ValidationLabService.ParameterFingerprint(altered));
    }

    private static IReadOnlyList<ValidationPathTradeMetricInput> BuildDualStatusFixture() =>
    [
        PathTrade("P1", included: true, net: 1.5m),
        PathTrade("P2", included: true, net: -0.5m),
        PathTrade("P3", included: false, net: 0.7m)
    ];

    private static ValidationPathTradeMetricInput PathTrade(string fingerprint, bool included, decimal net) => new()
    {
        CandidateFingerprint = fingerprint,
        ValidationLayer = ValidationLayerType.RawStrategy,
        EntryPrice = 100m,
        StopPriceAtEntry = 99m,
        Quantity = included ? 1m : 0m,
        ContractMultiplier = 1m,
        RiskAmountAtEntry = included ? 1m : null,
        GrossPnl = net + 0.1m,
        NetPnl = net,
        TotalTransactionCosts = 0.1m,
        Outcome = net >= 0m ? "Winner" : "Loser",
        ExitPrice = 101m,
        MetricInclusionStatus = included
            ? ValidationPathMetricInclusionStatus.Included
            : ValidationPathMetricInclusionStatus.Excluded,
        MetricExclusionReason = included ? null : "MissingPathQuantity",
        PnlCurrency = "USDT",
        RiskCurrency = "USDT"
    };

    // ------------------------------------------------------------------
    // WP23 — explicit version routing (no silent upgrade)
    // ------------------------------------------------------------------

    private sealed class SpyLegacyMapper : IValidationLegacyTrialMetricsMapper
    {
        public int CallCount { get; private set; }

        public void Apply(
            ValidationExperiment experiment,
            ValidationParameterTrial trial,
            StrategyLabRun run,
            IReadOnlyList<StrategyResearchCandidate> candidates,
            ValidationQualificationProfile profile) => CallCount++;
    }

    private sealed class SpyCalculator : IValidationTrialMetricsCalculator
    {
        public int CallCount { get; private set; }

        public ValidationTrialMetricsResult Calculate(
            ValidationExperiment experiment,
            StrategyLabRun run,
            IReadOnlyList<StrategyResearchCandidate> candidates,
            ValidationQualificationProfile profile,
            string? parameterFingerprint = null)
        {
            CallCount++;
            return new ValidationTrialMetricsResult();
        }
    }

    [Fact]
    public void Router_V132_UsesCalculator_LegacyMapperNeverCalled()
    {
        var spyLegacy = new SpyLegacyMapper();
        var router = new ValidationTrialMetricsRouter(RealCalculator(), spyLegacy);
        var trial = Trial(1, "fp-b");

        router.ApplyTrialMetrics(
            Experiment(), trial, new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile());

        Assert.Equal(0, spyLegacy.CallCount);
        Assert.NotNull(trial.TrialMetricSnapshotJson);
        Assert.NotNull(trial.TrialMetricFingerprint);
        Assert.Equal(ValidationMetricsContract.VersionV132, trial.TrialMetricsVersion);
        Assert.Equal(7L, trial.StrategyLabRunId);
    }

    [Fact]
    public void Router_LegacyVersion_UsesLegacyMapper_CalculatorNeverCalled()
    {
        var spyCalculator = new SpyCalculator();
        var router = new ValidationTrialMetricsRouter(spyCalculator, new ValidationLegacyTrialMetricsMapper());
        var trial = Trial(1, "fp-b");

        router.ApplyTrialMetrics(
            Experiment(ValidationMetricsContract.VersionV12),
            trial, new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile());

        Assert.Equal(0, spyCalculator.CallCount);
        Assert.NotNull(trial.TrainingScore);
        Assert.Null(trial.TrialMetricSnapshotJson);
        Assert.Null(trial.TrialMetricFingerprint);
    }

    [Fact]
    public void Router_UnknownVersion_Throws_NoSilentUpgrade()
    {
        var router = new ValidationTrialMetricsRouter(new SpyCalculator(), new SpyLegacyMapper());

        var ex = Assert.Throws<InvalidOperationException>(() => router.ApplyTrialMetrics(
            Experiment("ValidationMetrics/v9.9"),
            Trial(1, "fp"), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile()));
        Assert.Contains("silent upgrade", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // WP19–20 — ranking from persisted snapshot, deterministic tie-break
    // ------------------------------------------------------------------

    [Fact]
    public void Ranking_TieBreak_FingerprintOrdinal_ThenTrialNumber()
    {
        ValidationParameterTrial Ranked(int number, string fp) 
        {
            var t = Trial(number, fp);
            t.Status = ValidationTrialStatus.Completed;
            t.GuardrailDecision = "Passed";
            t.TrainingScore = 50m;
            t.NetExpectancyR = 1m;
            t.ProfitFactor = 2m;
            t.MaximumDrawdownPercent = null;
            t.ClosedTradeCount = 10;
            t.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
            t.TrialMetricFingerprint = "mf-" + fp;
            t.AuthoritativeAuditExecutionId = Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
            t.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
            return t;
        }

        var t1 = Ranked(1, "bbb");
        var t2 = Ranked(2, "aaa");
        var t3 = Ranked(3, "aaa");

        var orderedA = ValidationTrialRanker.OrderForRanking([t1, t2, t3], requireSnapshotEligibility: true);
        var orderedB = ValidationTrialRanker.OrderForRanking([t3, t1, t2], requireSnapshotEligibility: true);

        Assert.Equal(new[] { 2, 3, 1 }, orderedA.Select(t => t.TrialNumber).ToArray());
        Assert.Equal(new[] { 2, 3, 1 }, orderedB.Select(t => t.TrialNumber).ToArray());
    }

    [Fact]
    public void Ranking_NullMetrics_OrderAfterEvaluatedMetrics()
    {
        var evaluated = Trial(1, "aaa");
        evaluated.Status = ValidationTrialStatus.Completed;
        evaluated.GuardrailDecision = "Passed";
        evaluated.TrainingScore = 50m;
        evaluated.NetExpectancyR = 0.1m;
        evaluated.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        evaluated.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var nullMetric = Trial(2, "bbb");
        nullMetric.Status = ValidationTrialStatus.Completed;
        nullMetric.GuardrailDecision = "Passed";
        nullMetric.TrainingScore = 50m;
        nullMetric.NetExpectancyR = null;
        nullMetric.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        nullMetric.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var ordered = ValidationTrialRanker.OrderForRanking([nullMetric, evaluated]);
        Assert.Equal(new[] { 1, 2 }, ordered.Select(t => t.TrialNumber).ToArray());
    }

    [Fact]
    public void Ranking_SnapshotEligibilityRequired_ExcludesTrialsWithoutSnapshot()
    {
        var withSnapshot = Trial(1, "aaa");
        withSnapshot.Status = ValidationTrialStatus.Completed;
        withSnapshot.GuardrailDecision = "Passed";
        withSnapshot.TrainingScore = 10m;
        withSnapshot.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
        withSnapshot.TrialMetricFingerprint = "abc123";
        withSnapshot.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        withSnapshot.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var withoutSnapshot = Trial(2, "bbb");
        withoutSnapshot.Status = ValidationTrialStatus.Completed;
        withoutSnapshot.GuardrailDecision = "Passed";
        withoutSnapshot.TrainingScore = 99m; // better score, but no persisted snapshot
        withoutSnapshot.TrialRankEligibility = ValidationTrialRankEligibility.NotEvaluated;
        withoutSnapshot.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-000000000012");
        withoutSnapshot.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var trials = new List<ValidationParameterTrial> { withSnapshot, withoutSnapshot };
        ValidationTrialRanker.AssignRanks(trials, requireSnapshotEligibility: true);

        Assert.Equal(1, withSnapshot.Rank);
        Assert.Null(withoutSnapshot.Rank);
        Assert.Same(withSnapshot, ValidationTrialRanker.SelectWinner(trials, requireSnapshotEligibility: true));
    }

    // ------------------------------------------------------------------
    // WP21 — trial vs training segment reconciliation
    // ------------------------------------------------------------------

    [Fact]
    public void Reconciliation_Matched_WhenTrialSnapshotReproducesSegmentFingerprint()
    {
        var result = RealCalculator().Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile(), "fp-b");
        var trial = Trial(1, "fp-b");
        ValidationTrialMetricsCalculator.ApplyToTrial(trial, result);

        var segment = BuildMatchingTrainingSegment(result.Metrics);
        var report = ValidationTrialSegmentReconciliationService.Reconcile(trial, segment);

        Assert.Equal(ValidationTrialSegmentReconciliationStatus.Matched, report.Status);
        Assert.Empty(report.MismatchReasons);
        Assert.Equal(segment.ResultFingerprint, report.TrialDerivedResultFingerprint);
    }

    [Fact]
    public void Reconciliation_Mismatch_ProducesBlockingCode()
    {
        var result = RealCalculator().Calculate(
            Experiment(), new StrategyLabRun { Id = 7 }, BuildTrialBCandidates(), Profile(), "fp-b");
        var trial = Trial(1, "fp-b");
        ValidationTrialMetricsCalculator.ApplyToTrial(trial, result);

        var segment = BuildMatchingTrainingSegment(result.Metrics);
        segment.ResultFingerprint = "tampered";
        segment.NetPnl = 999m;

        var report = ValidationTrialSegmentReconciliationService.Reconcile(trial, segment);

        Assert.Equal(ValidationTrialSegmentReconciliationStatus.Mismatched, report.Status);
        Assert.Contains(
            ValidationTrialSegmentReconciliationReport.MismatchCode,
            report.MismatchReasons);
        Assert.Contains(report.MismatchReasons, r => r.StartsWith("NetPnl", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconciliation_MissingSnapshotOrSegment_Mismatches()
    {
        var noSnapshot = Trial(1, "fp");
        var report = ValidationTrialSegmentReconciliationService.Reconcile(noSnapshot, trainingRawSegment: null);
        Assert.Equal(ValidationTrialSegmentReconciliationStatus.Mismatched, report.Status);
        Assert.Contains("MISSING_TRIAL_METRIC_SNAPSHOT", report.MismatchReasons);
        Assert.Contains("MISSING_TRAINING_RAWSTRATEGY_SEGMENT", report.MismatchReasons);
    }

    private static ValidationSegmentResult BuildMatchingTrainingSegment(LayerSegmentMetrics metrics)
    {
        var fields = ValidationMetricsContract.BuildPathResultFingerprintFields(
            ValidationSegmentType.Training, ValidationLayerType.RawStrategy, metrics);
        return new ValidationSegmentResult
        {
            SegmentType = ValidationSegmentType.Training,
            LayerType = ValidationLayerType.RawStrategy,
            ResultFingerprint = ValidationLabService.ParameterFingerprint(fields),
            ClosedTradeCount = metrics.ClosedOutcomePopulationCount ?? metrics.ClosedTradeCount,
            NetExpectancyR = metrics.NetExpectancyR,
            NetPnl = metrics.NetPnl,
            MetricIncludedCandidateCount = metrics.IncludedPathInputCount ?? 0,
            MetricExcludedCandidateCount = metrics.ExcludedPathInputCount ?? 0
        };
    }

    // ------------------------------------------------------------------
    // WP22 — adversarial legacy vs v1.3.2 ranking fixture
    // ------------------------------------------------------------------

    [Fact]
    public void Adversarial_LegacyRoutingPicksA_V132RoutingMustPickB()
    {
        var profile = Profile(minClosed: 5);
        var runA = new StrategyLabRun { Id = 101 };
        var runB = new StrategyLabRun { Id = 102 };
        var candidatesA = BuildTrialACandidates();
        var candidatesB = BuildTrialBCandidates();

        // --- Legacy routing (ValidationMetrics/v1.2): trusts persisted candidate fields. ---
        var legacyRouter = new ValidationTrialMetricsRouter(
            new SpyCalculator(), new ValidationLegacyTrialMetricsMapper());
        var legacyExperiment = Experiment(ValidationMetricsContract.VersionV12);
        var legacyTrialA = Trial(1, "fp-a");
        var legacyTrialB = Trial(2, "fp-b");
        legacyRouter.ApplyTrialMetrics(legacyExperiment, legacyTrialA, runA, candidatesA, profile);
        legacyRouter.ApplyTrialMetrics(legacyExperiment, legacyTrialB, runB, candidatesB, profile);

        // Trial A's inflated persisted fields pass legacy guardrails and outscore honest trial B.
        Assert.Equal("Passed", legacyTrialA.GuardrailDecision);
        Assert.Equal("Passed", legacyTrialB.GuardrailDecision);
        Assert.Equal(2.11m, legacyTrialA.NetExpectancyR);
        Assert.Equal(Math.Round(11.6m / 1.05m, 8), legacyTrialA.ProfitFactor);
        Assert.Equal(85m, legacyTrialA.TrainingScore);
        Assert.Equal(1.31944m, legacyTrialB.NetExpectancyR);
        Assert.Equal(74.79m, legacyTrialB.TrainingScore);

        legacyTrialA.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        legacyTrialA.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
        legacyTrialB.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
        legacyTrialB.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var legacyWinner = ValidationTrialRanker.SelectWinner([legacyTrialA, legacyTrialB]);
        Assert.Same(legacyTrialA, legacyWinner);

        // --- v1.3.2 routing: recomputes normalized one-unit economics from prices + frozen fees. ---
        var router = new ValidationTrialMetricsRouter(RealCalculator(), new SpyLegacyMapper());
        var experiment = Experiment(ValidationMetricsContract.VersionV132);
        var trialA = Trial(1, "fp-a");
        var trialB = Trial(2, "fp-b");
        router.ApplyTrialMetrics(experiment, trialA, runA, candidatesA, profile);
        router.ApplyTrialMetrics(experiment, trialB, runB, candidatesB, profile);

        // Trial A's true one-unit economics are five losses: PF 0 and negative expectancy.
        Assert.Equal(ValidationTrialStatus.GuardrailRejected, trialA.Status);
        Assert.Equal(ValidationTrialRankEligibility.Ineligible, trialA.TrialRankEligibility);
        Assert.Equal(-1.0796m, trialA.NetExpectancyR);
        Assert.Equal(-5.398m, trialA.NetPnl);
        Assert.Equal(0m, trialA.ProfitFactor);
        Assert.Equal(25m, trialA.TrainingScore);
        var reasonsA = JsonSerializer.Deserialize<string[]>(trialA.RankIneligibleReasonsJson!)!;
        Assert.Contains(ValidationGuardrailFailureCodes.ProfitFactorBelowMinimum, reasonsA);
        Assert.Contains(ValidationGuardrailFailureCodes.NetExpectancyBelowMinimum, reasonsA);

        // Trial B's honest economics pass and produce the persisted snapshot.
        Assert.Equal(ValidationTrialStatus.Completed, trialB.Status);
        Assert.Equal(ValidationTrialRankEligibility.Eligible, trialB.TrialRankEligibility);
        Assert.Equal(1.31944m, trialB.NetExpectancyR);
        Assert.Equal(6.5972m, trialB.NetPnl);
        Assert.Equal(Math.Round(7.6768m / 1.0796m, 8), trialB.ProfitFactor);
        Assert.Equal(73.78m, trialB.TrainingScore);
        Assert.NotNull(trialB.TrialMetricFingerprint);

        trialB.AuthoritativeAuditExecutionId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
        trialB.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var trials = new List<ValidationParameterTrial> { trialA, trialB };
        ValidationTrialRanker.AssignRanks(trials, requireSnapshotEligibility: true);
        var winner = ValidationTrialRanker.SelectWinner(trials, requireSnapshotEligibility: true);

        Assert.Same(trialB, winner);
        Assert.Equal(1, trialB.Rank);
        Assert.Null(trialA.Rank);
    }
}
