using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Synthetic;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationCandidateReconciliationTests
{
    private readonly ValidationCandidateReconciliationService _sut = new();

    private static StrategyResearchCandidate Cand(string fp, DateTime detected, long runId = 1) => new()
    {
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
        CreatedAtUtc = detected
    };

    private static ValidationExperiment Experiment(
        DateTime trainStart,
        DateTime valStart,
        DateTime valEnd) => new()
    {
        Id = 1,
        Name = "test",
        StrategyCode = "TEST",
        SegmentDetectorContinuityMode = SegmentDetectorContinuityMode.FreshSessionWithWarmup,
        TrainingStartUtc = trainStart,
        ValidationStartUtc = valStart,
        ValidationEndUtc = valEnd,
        SourceStrategyLabRunId = 28,
        TrainingStrategyLabRunId = 29,
        ValidationStrategyLabRunId = 30
    };

    [Fact]
    public void ExactMatch_WhenPartitionEqualsFullRange()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var end = t0.AddDays(10);
        var full = new[]
        {
            Cand("AAA", t0.AddDays(1)),
            Cand("BBB", t0.AddDays(2)),
            Cand("CCC", split.AddDays(1))
        };
        var train = new[] { Cand("AAA", t0.AddDays(1), 29), Cand("BBB", t0.AddDays(2), 29) };
        var val = new[] { Cand("CCC", split.AddDays(1), 30) };
        var report = _sut.Reconcile(Experiment(t0, split, end), full, train, val);
        Assert.Equal(CandidateReconciliationStatus.ExactMatch, report.ReconciliationStatus);
        Assert.Equal(0, report.OverlappingFingerprintCount);
        Assert.Equal(0, report.AddedFingerprintCount);
        Assert.Equal(0, report.MissingFingerprintCount);
        Assert.Equal(3, report.UniqueFullRangeFingerprintCount);
        Assert.Equal(3, report.UniqueSegmentFingerprintCount);
    }

    [Fact]
    public void ExplainedSessionBoundary_WhenKnownOverlapFingerprints()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var end = t0.AddDays(10);
        var overlaps = ValidationCandidateReconciliationService.KnownSessionBoundaryOverlapFingerprints.ToList();
        var full = overlaps.Select((fp, i) => Cand(fp, t0.AddDays(i + 1))).ToList();
        // train+val both contain same FPs (session reset re-confirms)
        var train = overlaps.Select((fp, i) => Cand(fp, t0.AddDays(i + 1), 29)).ToList();
        var val = overlaps.Select((fp, i) => Cand(fp, split.AddHours(i), 30)).ToList();
        // Full range has exactly the overlap set; union equals full but counts inflate.
        var report = _sut.Reconcile(Experiment(t0, split, end), full, train, val);
        Assert.Equal(CandidateReconciliationStatus.ExplainedSessionBoundaryDifference, report.ReconciliationStatus);
        Assert.Equal(5, report.OverlappingFingerprintCount);
        Assert.Equal(5, report.UniqueFullRangeFingerprintCount);
        Assert.Equal(5, report.UniqueSegmentFingerprintCount);
        Assert.All(report.DetailedDifferences.Where(d => d.DifferenceType == "DetectorSessionBoundaryEffect"),
            d => Assert.True(d.IsExpected));
    }

    [Fact]
    public void UnexplainedDifference_BlocksPassSemantics()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var end = t0.AddDays(10);
        var full = new[] { Cand("ONLYFULL", t0.AddDays(1)) };
        var train = new[] { Cand("ONLYTRAIN", t0.AddDays(1), 29) };
        var val = new[] { Cand("ONLYVAL", split.AddDays(1), 30) };
        var report = _sut.Reconcile(Experiment(t0, split, end), full, train, val);
        Assert.Equal(CandidateReconciliationStatus.UnexplainedDifference, report.ReconciliationStatus);

        var verdict = ValidationRobustnessEvaluator.Evaluate(
            new LayerSegmentMetrics
            {
                ClosedTradeCount = 40,
                NetExpectancyR = 0.3m,
                ProfitFactor = 1.3m,
                NetProfitFactor = 1.3m,
                OpportunityRatePer1000Candles = 5m,
                NetPnl = 100m,
                MaximumRealizedDrawdownPercent = 5m
            },
            new LayerSegmentMetrics
            {
                ClosedTradeCount = 20,
                NetExpectancyR = 0.2m,
                ProfitFactor = 1.2m,
                NetProfitFactor = 1.2m,
                OpportunityRatePer1000Candles = 4m,
                NetPnl = 50m,
                MaximumRealizedDrawdownPercent = 5m
            },
            ValidationQualificationProfile.StandardDefault(),
            parameterStabilityOk: true,
            reconciliationStatus: report.ReconciliationStatus);
        Assert.Contains(nameof(StrategyRobustnessDecision.FailedDataIntegrity), verdict.FailureReasons);
        Assert.NotEqual(StrategyRobustnessDecision.Passed, verdict.Decision);
    }

    [Fact]
    public void BoundaryCensored_CountedSeparately()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = t0.AddDays(7);
        var end = t0.AddDays(10);
        var boundary = Cand("BOUND", t0.AddDays(6), 29);
        boundary.RawExitTimeUtc = split.AddHours(2);
        boundary.RawOutcomeStatus = RawOutcomeStatus.Winner;
        var train = new[] { Cand("AAA", t0.AddDays(1), 29), boundary };
        var val = new[] { Cand("CCC", split.AddDays(1), 30) };
        var full = new[] { Cand("AAA", t0.AddDays(1)), Cand("BOUND", t0.AddDays(6)), Cand("CCC", split.AddDays(1)) };
        var report = _sut.Reconcile(Experiment(t0, split, end), full, train, val);
        Assert.True(report.BoundaryCensoredCount >= 1);
    }
}

public class ValidationMetricsContractTests
{
    [Fact]
    public void GrossVsNetExpectancy_SeparateWhenCostsPresent()
    {
        var candidates = new List<StrategyResearchCandidate>
        {
            Trade("t1", rawR: 1.0m, gross: 100m, net: 80m),
            Trade("t2", rawR: -0.5m, gross: -50m, net: -60m)
        };
        var m = ValidationMetricsContract.FromCandidates(candidates, 100, 0, ValidationLayerType.RawStrategy);
        Assert.Equal(0.25m, m.GrossExpectancyR);
        Assert.NotEqual(m.GrossExpectancyR, m.NetExpectancyR);
        Assert.True(m.NetExpectancyR < m.GrossExpectancyR);
        Assert.Equal(50m, m.GrossPnl);
        Assert.Equal(20m, m.NetPnl);
        Assert.Equal(30m, m.TransactionCosts);
    }

    [Fact]
    public void ProfitFactor_ZeroLoss_IsInfinityNotArbitraryNumber()
    {
        var pf = ValidationMetricsContract.ComputeProfitFactor(100m, 0m);
        Assert.Equal(ProfitFactorStatus.Infinity, pf.Status);
        Assert.Null(pf.NumericValue);
    }

    [Fact]
    public void MetricConsistency_DetectsNetPnlCostMismatch()
    {
        var svc = new ValidationMetricConsistencyService();
        var report = svc.Validate(new LayerSegmentMetrics
        {
            GrossPnl = 100m,
            TransactionCosts = 10m,
            NetPnl = 50m,
            ClosedTradeCount = 2,
            WinnerCount = 1,
            LoserCount = 1
        });
        Assert.False(report.IsConsistent);
        Assert.True(report.BlocksPassedVerdict);
        Assert.Contains(report.Diagnostics, d => d.Key == "NetPnlCostMismatch");
    }

    [Fact]
    public void Verdict_UsesNetExpectancyR_ByDefault()
    {
        var training = new LayerSegmentMetrics
        {
            ClosedTradeCount = 40,
            GrossExpectancyR = 0.5m,
            NetExpectancyR = 0.3m,
            ProfitFactor = 1.3m,
            NetProfitFactor = 1.3m,
            OpportunityRatePer1000Candles = 5m,
            NetPnl = 100m,
            MaximumRealizedDrawdownPercent = 5m
        };
        var validation = new LayerSegmentMetrics
        {
            ClosedTradeCount = 20,
            GrossExpectancyR = 0.2m,
            NetExpectancyR = -0.08m,
            ProfitFactor = 0.9m,
            NetProfitFactor = 0.9m,
            OpportunityRatePer1000Candles = 4m,
            NetPnl = -10m,
            MaximumRealizedDrawdownPercent = 5m
        };
        var verdict = ValidationRobustnessEvaluator.Evaluate(
            training, validation, ValidationQualificationProfile.StandardDefault());
        Assert.Contains(nameof(StrategyRobustnessDecision.FailedNegativeValidationExpectancy), verdict.FailureReasons);
        Assert.Contains(verdict.StructuredRuleResults,
            r => r.RuleKey == "ValidationExpectancy" && r.MetricKey == "NetExpectancyR");
        Assert.Contains(verdict.StructuredRuleResults,
            r => r.Reason.Contains("NetExpectancyR", StringComparison.Ordinal));
    }

    private static StrategyResearchCandidate Trade(string fp, decimal rawR, decimal gross, decimal net) => new()
    {
        SetupFingerprint = fp,
        SetupDetectedAtUtc = DateTime.UtcNow,
        ProposedEntryTimeUtc = DateTime.UtcNow,
        ProposedEntryPrice = 100m,
        StopLoss = 99m,
        Target1 = 102m,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = net >= 0 ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
        RawRMultiple = rawR,
        RawGrossPnl = gross,
        RawNetPnl = net,
        StrategyCode = "T",
        Symbol = "BTCUSDT",
        Timeframe = "15m",
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class ValidationModeAwareStabilityTests
{
    [Fact]
    public void ValidateExisting_StabilityNotApplicable_NeverFailsInstability()
    {
        var result = ValidationParameterStabilityAnalyzer.AnalyzeForExperimentType(
            ValidationExperimentType.ValidateExistingFrozenConfiguration,
            trials: []);
        Assert.Equal(ParameterStabilityApplicability.NotApplicable, result.Applicability);
        Assert.True(result.IsStable);

        var verdict = ValidationRobustnessEvaluator.Evaluate(
            new LayerSegmentMetrics
            {
                ClosedTradeCount = 40,
                NetExpectancyR = 0.3m,
                ProfitFactor = 1.3m,
                NetProfitFactor = 1.3m,
                OpportunityRatePer1000Candles = 5m,
                NetPnl = 100m,
                MaximumRealizedDrawdownPercent = 5m
            },
            new LayerSegmentMetrics
            {
                ClosedTradeCount = 20,
                NetExpectancyR = 0.2m,
                ProfitFactor = 1.2m,
                NetProfitFactor = 1.2m,
                OpportunityRatePer1000Candles = 4m,
                NetPnl = 40m,
                MaximumRealizedDrawdownPercent = 5m
            },
            ValidationQualificationProfile.StandardDefault(),
            parameterStabilityOk: false,
            experimentType: ValidationExperimentType.ValidateExistingFrozenConfiguration,
            stabilityApplicability: ParameterStabilityApplicability.NotApplicable);
        Assert.Equal(StrategyRobustnessDecision.Passed, verdict.Decision);
        Assert.DoesNotContain(nameof(StrategyRobustnessDecision.FailedParameterInstability), verdict.FailureReasons);
        Assert.Contains(verdict.StructuredRuleResults,
            r => r.RuleKey == "ParameterStability" && r.Status == QualificationRuleStatus.NotApplicable);
    }

    [Fact]
    public void TrainingSearch_StabilityApplicable_CanFail()
    {
        var trials = Enumerable.Range(1, 5).Select(i => new ValidationParameterTrial
        {
            TrialNumber = i,
            ParameterFingerprint = $"fp{i}",
            ParameterSnapshotJson = "{}",
            Status = ValidationTrialStatus.Completed,
            TrainingScore = 100m - i * 15m,
            GuardrailDecision = "Passed",
            ClosedTradeCount = 40
        }).ToList();
        var result = ValidationParameterStabilityAnalyzer.AnalyzeForExperimentType(
            ValidationExperimentType.TrainingSearchHoldoutValidation, trials);
        Assert.Equal(ParameterStabilityApplicability.Evaluated, result.Applicability);
        Assert.False(result.IsStable);
        Assert.Contains("ParameterCliff", result.Warnings);
    }
}

public class ValidationLeakageAuditorTests
{
    [Fact]
    public void Passed_WhenMaxTsBeforeValidationStart()
    {
        var auditor = new ValidationLeakageAuditor();
        var report = auditor.Evaluate(
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            "abc");
        Assert.Equal(ValidationLeakageAuditStatus.Passed, report.Status);
        Assert.False(report.BlocksFreezeOrPassed);
    }

    [Fact]
    public void Failed_WhenMaxTsReachesValidationStart()
    {
        var auditor = new ValidationLeakageAuditor();
        var valStart = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);
        var report = auditor.Evaluate(
            valStart,
            valStart,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            valStart,
            "abc");
        Assert.Equal(ValidationLeakageAuditStatus.Failed, report.Status);
        Assert.True(report.BlocksFreezeOrPassed);
    }
}

public class ValidationExportRevealGatingTests
{
    [Fact]
    public void PopulateEnvelope_BlocksResultsWhenNotRevealed()
    {
        var detail = new Application.ValidationLab.Dtos.ValidationExperimentDetailDto
        {
            Id = 9,
            Name = "hidden",
            ValidationRevealStatus = ValidationRevealStatus.Frozen,
            ValidationMetricsVersion = ValidationMetricsContract.VersionV11,
            Status = ValidationExperimentStatus.ConfigurationFrozen,
            StrategyCode = "X",
            Symbol = "BTCUSDT",
            Timeframe = "15m"
        };
        var envelope = new Application.Exports.Dtos.ExportEnvelopeDto
        {
            SourceMetadata = new Dictionary<string, object?>(),
            ExportMetadata = new Application.Exports.Dtos.ExportMetadataDto
            {
                ExportId = "t",
                ExportedAtUtc = DateTime.UtcNow,
                AppName = "MOMO",
                AppVersion = "1",
                Environment = "Test",
                Scope = "ValidationExperiment",
                SourceId = "9",
                Format = "json",
                DetailLevel = "full"
            }
        };
        ValidationLabExportBuilder.PopulateExportEnvelope(envelope, detail, revealed: false);
        Assert.True(envelope.Results.ContainsKey("blocked"));
        Assert.Contains(envelope.Warnings, w => w.Contains("hidden", StringComparison.OrdinalIgnoreCase));
    }
}

public class SyntheticLeakageOrchestrationTests
{
    [Fact]
    public void VerificationB_TrainSelectsA_ValBBetter_RerunUnchanged_LeakagePassed()
    {
        var first = SyntheticParameterSelectionFixture.RunLeakageProofOrchestration(amplifyValidationB: false);
        Assert.Equal(SyntheticParameterSelectionFixture.ModeA, first.SelectedMode);
        Assert.True(first.ValidationModeBOutperformsA);
        Assert.Equal(ValidationLeakageAuditStatus.Passed, first.LeakageStatus);

        var amplified = SyntheticParameterSelectionFixture.RunLeakageProofOrchestration(amplifyValidationB: true);
        Assert.Equal(SyntheticParameterSelectionFixture.ModeA, amplified.SelectedMode);
        Assert.True(amplified.ValidationModeBOutperformsA);
        Assert.Equal(first.SelectedFingerprint, amplified.SelectedFingerprint);
        Assert.True(amplified.SelectionUnchangedAfterValidationAmplify);
        Assert.Equal(first.TrainingTrialFingerprintBefore, amplified.TrainingTrialFingerprintAfterValidationAmplify);
        Assert.Equal(ValidationLeakageAuditStatus.Passed, amplified.LeakageStatus);
    }
}
