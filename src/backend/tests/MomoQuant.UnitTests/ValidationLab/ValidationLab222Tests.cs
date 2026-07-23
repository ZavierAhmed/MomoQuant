using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationHoldoutExclusivityTests
{
    private readonly ValidationHoldoutExclusivityService _sut = new();

    private static StrategyResearchCandidate Cand(
        long id,
        string fp,
        DateTime detected,
        long runId = 1) => new()
    {
        Id = id,
        StrategyLabRunId = runId,
        SetupFingerprint = fp,
        SetupDetectedAtUtc = detected,
        ProposedEntryTimeUtc = detected,
        ProposedEntryPrice = 100m,
        StopLoss = 99m,
        Target1 = 102m,
        Direction = TradeDirection.Long,
        Symbol = "BTCUSDT",
        Timeframe = "15m",
        StrategyCode = "TEST",
        CreatedAtUtc = detected,
        RawOutcomeStatus = RawOutcomeStatus.Winner,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawRMultiple = 1.5m,
        RawGrossPnl = 15m,
        RawNetPnl = 14m
    };

    [Fact]
    public void SameFingerprint_MetricIncludedOnce_TrainingOwns()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var train = new[]
        {
            Cand(1, "AAA", t0.AddDays(1), 29),
            Cand(2, "OVERLAP", t0.AddDays(2), 29)
        };
        var val = new[]
        {
            Cand(3, "OVERLAP", split.AddHours(1), 30),
            Cand(4, "VALONLY", split.AddDays(1), 30)
        };

        var report = _sut.Apply(train, val, split);
        var partition = _sut.ApplyExclusivityToValidationCandidates(val, report);

        Assert.Equal(1, report.CrossSegmentOverlapCount);
        Assert.True(report.MetricIntersectionEmpty);
        Assert.Contains("OVERLAP", report.TrainingMetricFingerprints);
        Assert.DoesNotContain("OVERLAP", report.ValidationMetricFingerprints);
        Assert.Contains("VALONLY", report.ValidationMetricFingerprints);
        Assert.Single(partition.AuditOnly);
        Assert.Equal(3, partition.AuditOnly[0].Id);
        Assert.Single(partition.MetricIncluded);
        Assert.Equal(4, partition.MetricIncluded[0].Id);
        Assert.Equal("Training", report.Overlaps[0].MetricOwner);
        Assert.All(partition.AuditOnly, _ =>
            Assert.Contains(
                report.Classifications,
                c => c.CandidateId == _.Id
                     && c.MetricClassification ==
                     ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation
                     && !c.PortfolioMutationAllowed));
    }

    [Fact]
    public void AuditOnly_CannotAppearInMetricFingerprintSets()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var known = ValidationCandidateReconciliationService.KnownSessionBoundaryOverlapFingerprints.ToList();
        var train = known.Select((fp, i) => Cand(i + 1, fp, t0.AddDays(i + 1), 29)).ToList();
        var val = known.Select((fp, i) => Cand(100 + i, fp, split.AddHours(i), 30)).ToList();
        val.Add(Cand(200, "UNIQUE_VAL", split.AddDays(1), 30));

        var report = _sut.Apply(train, val, split);
        Assert.Equal(5, report.CrossSegmentOverlapCount);
        Assert.True(report.MetricIntersectionEmpty);
        Assert.DoesNotContain(
            report.ValidationMetricFingerprints,
            fp => known.Contains(fp, StringComparer.OrdinalIgnoreCase));
        Assert.All(known, fp => Assert.Contains(fp, report.TrainingMetricFingerprints));
    }

    [Fact]
    public void UnionReconciles_AfterExclusivity()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var train = new[] { Cand(1, "T1", t0.AddDays(1)), Cand(2, "SHARED", t0.AddDays(2)) };
        var val = new[] { Cand(3, "SHARED", split), Cand(4, "V1", split.AddDays(1)) };
        var report = _sut.Apply(train, val, split);
        Assert.True(report.MetricIntersectionEmpty);
        Assert.True(report.UnionReconcilesWithProvidedRange);
        var union = report.TrainingMetricFingerprints
            .Union(report.ValidationMetricFingerprints, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, union.Count);
        Assert.Contains("T1", union);
        Assert.Contains("SHARED", union);
        Assert.Contains("V1", union);
    }

    [Fact]
    public void Exclusivity_PreventsQualificationCountingOverlap()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var train = new[] { Cand(1, "SHARED", t0.AddDays(1)) };
        var valOverlap = Cand(2, "SHARED", split);
        valOverlap.RawNetPnl = 999m;
        valOverlap.RawGrossPnl = 1000m;
        valOverlap.RawRMultiple = 10m;
        var valUnique = Cand(3, "V1", split.AddDays(1));
        valUnique.RawNetPnl = 5m;
        valUnique.RawGrossPnl = 6m;
        valUnique.RawRMultiple = 0.5m;
        var val = new[] { valOverlap, valUnique };

        var report = _sut.Apply(train, val, split);
        var partition = _sut.ApplyExclusivityToValidationCandidates(val, report);
        var metrics = ValidationMetricsContract.FromCandidates(
            partition.MetricIncluded, 1000, 0, ValidationLayerType.RawStrategy);

        Assert.Equal(1, metrics.CandidateCount);
        Assert.Equal(1, metrics.ClosedTradeCount);
        Assert.Equal(6m, metrics.GrossPnl);
        Assert.Equal(5m, metrics.NetPnl);
        Assert.DoesNotContain(999m, new[] { metrics.NetPnl ?? 0m });
    }
}

public class ValidationGridPsbrTests
{
    [Fact]
    public void GenerateGrid_5x5_Produces25UniquePsbrFingerprints()
    {
        var provider = new StrategyParameterDefinitionProvider();
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["swingLeftBarsMin"] = "1",
            ["swingLeftBarsMax"] = "5",
            ["swingLeftBarsStep"] = "1",
            ["retestTolerancePercentMin"] = "0.05",
            ["retestTolerancePercentMax"] = "0.25",
            ["retestTolerancePercentStep"] = "0.05",
            // Lock other optimizable axes to defaults (min=max)
            ["swingRightBarsMin"] = "2",
            ["swingRightBarsMax"] = "2",
            ["swingRightBarsStep"] = "1",
            ["maxRetestBarsMin"] = "20",
            ["maxRetestBarsMax"] = "20",
            ["maxRetestBarsStep"] = "1",
            ["fixedRewardRiskMin"] = "2",
            ["fixedRewardRiskMax"] = "2",
            ["fixedRewardRiskStep"] = "0.5",
            ["stopBufferPercentMin"] = "0.05",
            ["stopBufferPercentMax"] = "0.05",
            ["stopBufferPercentStep"] = "0.05"
        };

        var grid = provider.GenerateGridCombinations(
            StrategyCodes.PriceStructureBreakoutRetest, 100, overrides);

        Assert.Equal(25, grid.Count);
        var fingerprints = grid
            .Select(p => string.Join('|',
                p.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}={kv.Value}")))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(25, fingerprints.Count);

        // PSBR keys must survive merge (not destroyed by VG From)
        Assert.All(grid, combo =>
        {
            Assert.True(combo.ContainsKey("swingLeftBars"));
            Assert.True(combo.ContainsKey("retestTolerancePercent"));
            Assert.True(combo.ContainsKey("useWicksForSwing"));
            Assert.True(combo.ContainsKey("breakoutMustCloseBeyondLevel"));
            Assert.False(combo.ContainsKey("atrPeriod")); // VG-only key must not appear
        });

        var leftBars = grid.Select(c => c["swingLeftBars"]).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(["1", "2", "3", "4", "5"], leftBars);
    }
}

public class ValidationExactMetricsTests
{
    [Fact]
    public void ExactMetrics_GrossVsNet_UnroundedMonetary()
    {
        var c1 = new StrategyResearchCandidate
        {
            Id = 1,
            SetupFingerprint = "A",
            SetupDetectedAtUtc = DateTime.UtcNow,
            ProposedEntryTimeUtc = DateTime.UtcNow,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            Direction = TradeDirection.Long,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            StrategyCode = "TEST",
            CreatedAtUtc = DateTime.UtcNow,
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = RawOutcomeStatus.Winner,
            RawRMultiple = 1.23456789m,
            RawGrossPnl = 12.345678901m,
            RawNetPnl = 11.111111111m,
            RiskAmount = 10m
        };
        var c2 = new StrategyResearchCandidate
        {
            Id = 2,
            SetupFingerprint = "B",
            SetupDetectedAtUtc = DateTime.UtcNow,
            ProposedEntryTimeUtc = DateTime.UtcNow,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            Direction = TradeDirection.Long,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            StrategyCode = "TEST",
            CreatedAtUtc = DateTime.UtcNow,
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = RawOutcomeStatus.Loser,
            RawRMultiple = -0.5m,
            RawGrossPnl = -5.5m,
            RawNetPnl = -5.75m,
            RiskAmount = 10m
        };

        var m = ValidationMetricsContract.FromCandidates(
            [c1, c2], 500, 0, ValidationLayerType.RawStrategy);

        Assert.Equal(ValidationMetricsContract.VersionV12, m.MetricsVersion);
        Assert.Equal(12.345678901m + -5.5m, m.GrossPnl);
        Assert.Equal(11.111111111m + -5.75m, m.NetPnl);
        Assert.Equal(12.345678901m, m.GrossProfit);
        Assert.Equal(5.5m, m.GrossLoss);
        Assert.Equal(11.111111111m, m.NetProfit);
        Assert.Equal(5.75m, m.NetLoss);
        Assert.Equal(2, m.ClosedTradeCount);
        Assert.True(m.GrossExpectancyR.HasValue);
        Assert.True(m.NetExpectancyR.HasValue);
        Assert.NotEqual(m.GrossExpectancyR, m.NetExpectancyR);
    }
}

public class ValidationExportContentVerifierTests
{
    [Fact]
    public void Verifier_Passes_WhenAllSectionsPresent()
    {
        var detail = new Application.ValidationLab.Dtos.ValidationExperimentDetailDto
        {
            Id = 99,
            Name = "test",
            CrossSegmentOverlapCount = 2,
            HoldoutExclusivityJson = """{"crossSegmentOverlapCount":2,"overlaps":[]}""",
            QualificationRuleResultsJson = "[]",
            SegmentResults =
            [
                new Application.ValidationLab.Dtos.ValidationSegmentResultDto
                {
                    PersistedCandidateRowCount = 10,
                    MetricIncludedCandidateCount = 8,
                    MetricExcludedCandidateCount = 2,
                    CrossSegmentOverlapCount = 2
                }
            ]
        };

        var complete = """
            {
              "experiment": {},
              "split": {},
              "candidateReconciliation": {},
              "frozenConfiguration": {},
              "trainingResults": [],
              "validationResults": [],
              "qualification": {},
              "holdoutExclusivity": {},
              "exportManifest": {}
            }
            """;
        var csv = """
            # training-trials.csv
            TrialNumber
            # validation-experiment-segment-results.csv
            SegmentType
            # validation-experiment-candidate-reconciliation.csv
            Fingerprint
            # training-candidates.csv
            CandidateId
            # validation-candidates.csv
            CandidateId
            # validation-experiment-qualification-rules.csv
            RuleKey
            # diagnostics.csv
            DiagnosticKey
            # overlap-candidates.csv
            OverlapFingerprint
            """;
        var pdf = """
            Experiment Overview
            Data Quality
            Chronological Split
            Candidate Reconciliation
            Training Search
            Top Trials
            Frozen Configuration
            Training Metrics
            Holdout Metrics
            Gross versus Net Results
            Layer Comparison
            Parameter Stability
            Leakage Audit
            Qualification Rules
            Final Verdict
            Limitations
            """;

        var result = new ValidationExportContentVerifier().Verify(detail, complete, csv, pdf);
        Assert.Equal(ValidationExportVerificationStatus.Passed, result.Status);
        Assert.Empty(result.Issues);
        Assert.False(string.IsNullOrWhiteSpace(result.Manifest.ContentSha256));
        Assert.True(result.Manifest.HasOverlapCandidatesCsv);
        Assert.True(result.Manifest.HasExclusivityReport);
        Assert.True(result.Manifest.HasPopulationCounts);
    }

    [Fact]
    public void Verifier_Fails_WhenOverlapCsvMissing()
    {
        var detail = new Application.ValidationLab.Dtos.ValidationExperimentDetailDto
        {
            Id = 1,
            Name = "x",
            CrossSegmentOverlapCount = 3,
            HoldoutExclusivityJson = """{"crossSegmentOverlapCount":3}"""
        };
        var result = new ValidationExportContentVerifier().Verify(
            detail,
            """{"experiment":{}}""",
            "# validation-experiment-segment-results.csv\n",
            "MOMO Quant — Validation Laboratory Report");
        Assert.Equal(ValidationExportVerificationStatus.Failed, result.Status);
        Assert.Contains(result.Issues, i => i.Contains("overlap-candidates", StringComparison.OrdinalIgnoreCase));
    }
}

public class ValidationLaboratoryReadinessTests
{
    [Fact]
    public void EvaluateExperiment_Blocks_WhenMetricConsistencyFailed()
    {
        var readiness = new ValidationLaboratoryReadinessService(
            experiments: null!);
        var experiment = new ValidationExperiment
        {
            Id = 1,
            Name = "ready-check",
            StrategyCode = "TEST",
            Status = ValidationExperimentStatus.Completed,
            MetricConsistencyStatus = "Failed",
            ExportVerificationStatus = ValidationExportVerificationStatus.Passed,
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12
        };

        var status = readiness.EvaluateExperiment(experiment);
        Assert.Equal(ValidationLaboratoryReadiness.Blocked, status);
    }

    [Fact]
    public void EvaluateExperiment_DoesNotBlock_InterruptedFailedWithoutIntegrity()
    {
        var readiness = new ValidationLaboratoryReadinessService(experiments: null!);
        var experiment = new ValidationExperiment
        {
            Id = 99,
            Name = "interrupted-c2",
            StrategyCode = "TEST",
            Status = ValidationExperimentStatus.Failed,
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12
        };

        var status = readiness.EvaluateExperiment(experiment);
        Assert.Equal(ValidationLaboratoryReadiness.ReadyWithWarnings, status);
    }

    [Fact]
    public void EvaluateExperiment_DoesNotBlock_NegativeStrategyVerdictOnCompleted()
    {
        var readiness = new ValidationLaboratoryReadinessService(experiments: null!);
        var experiment = new ValidationExperiment
        {
            Id = 10,
            Name = "a3",
            StrategyCode = "TEST",
            Status = ValidationExperimentStatus.Completed,
            MetricConsistencyStatus = "Passed",
            LeakageAuditStatus = ValidationLeakageAuditStatus.Passed,
            ExportVerificationStatus = ValidationExportVerificationStatus.Passed,
            CandidateReconciliationStatus = CandidateReconciliationStatus.ExplainedSessionBoundaryDifference,
            CrossSegmentOverlapCount = 5,
            PrimaryFailureReason = "FailedNegativeTrainingExpectancy",
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12
        };

        var status = readiness.EvaluateExperiment(experiment);
        Assert.Equal(ValidationLaboratoryReadiness.ReadyWithWarnings, status);
    }
}

public class ValidationReconciliationExclusivityTests
{
    [Fact]
    public void Reconciliation_MarksOverlapsAsNotAffectingMetrics_WhenExclusivityApplied()
    {
        var sut = new ValidationCandidateReconciliationService();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var end = t0.AddDays(10);
        var experiment = new ValidationExperiment
        {
            Id = 1,
            Name = "test",
            StrategyCode = "TEST",
            SegmentDetectorContinuityMode = SegmentDetectorContinuityMode.FreshSessionWithWarmup,
            TrainingStartUtc = t0,
            ValidationStartUtc = split,
            ValidationEndUtc = end,
            TrainingStrategyLabRunId = 29,
            ValidationStrategyLabRunId = 30,
            HoldoutExclusivityPolicyVersion = ValidationHoldoutExclusivityVersions.Current
        };

        StrategyResearchCandidate Cand(string fp, DateTime d, long run) => new()
        {
            StrategyLabRunId = run,
            SetupFingerprint = fp,
            SetupDetectedAtUtc = d,
            ProposedEntryTimeUtc = d,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            Direction = TradeDirection.Long,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            StrategyCode = "TEST",
            CreatedAtUtc = d
        };

        var train = new[] { Cand("SHARED", t0.AddDays(1), 29), Cand("T1", t0.AddDays(2), 29) };
        var val = new[] { Cand("SHARED", split.AddHours(1), 30), Cand("V1", split.AddDays(1), 30) };
        var full = new[] { Cand("SHARED", t0.AddDays(1), 28), Cand("T1", t0.AddDays(2), 28), Cand("V1", split.AddDays(1), 28) };

        var report = sut.Reconcile(experiment, full, train, val);
        Assert.True(report.OverlappingFingerprintCount >= 1);
        Assert.Contains(
            report.DetailedDifferences.Where(d => d.Fingerprint == "SHARED"),
            d => !d.AffectsMetrics
                 || d.DifferenceType.Contains("Overlap", StringComparison.OrdinalIgnoreCase)
                 || d.Explanation.Contains("exclusivity", StringComparison.OrdinalIgnoreCase));
    }
}
