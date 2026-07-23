using System.Text.Json;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationLab224Tests
{
    private static ValidationExperiment TrainingExperiment() => new()
    {
        Id = 1,
        MaximumTrials = 25,
        ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
        AllowInfrastructureOnlyRejectedTrialFallback = false
    };

    private static ValidationParameterTrial RejectedTrial(
        long id, int num, string fp, decimal score = 1m) => new()
    {
        Id = id,
        TrialNumber = num,
        ParameterFingerprint = fp,
        ParameterSnapshotJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = fp }),
        Status = ValidationTrialStatus.GuardrailRejected,
        GuardrailDecision = "Failed",
        TrainingScore = score,
        StrategyLabRunId = 100 + num
    };

    private static ValidationParameterTrial EligibleTrial(
        long id, int num, string fp, decimal score,
        decimal netExp = 0.1m, decimal pf = 1.2m) => new()
    {
        Id = id,
        TrialNumber = num,
        ParameterFingerprint = fp,
        ParameterSnapshotJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = fp }),
        Status = ValidationTrialStatus.Completed,
        GuardrailDecision = "Passed",
        TrainingScore = score,
        NetExpectancyR = netExp,
        ProfitFactor = pf,
        ClosedTradeCount = 10,
        StrategyLabRunId = 200 + num
    };

    private static StrategyResearchCandidate RawClosed(
        string fp,
        decimal entry,
        decimal stop,
        decimal grossPnl,
        decimal netPnl,
        decimal? riskAmount = null,
        decimal? rawR = null) => new()
    {
        SetupFingerprint = fp,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = grossPnl >= 0 ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
        ProposedEntryPrice = entry,
        StopLoss = stop,
        ProposedEntryTimeUtc = DateTime.UtcNow,
        SetupDetectedAtUtc = DateTime.UtcNow,
        RawGrossPnl = grossPnl,
        RawNetPnl = netPnl,
        RiskAmount = riskAmount,
        RawRMultiple = rawR
    };

    [Fact]
    public void ZeroEligibleTrials_NoSelection_FailsTruthfully()
    {
        var svc = new ValidationTrainingSelectionService();
        var trials = Enumerable.Range(1, 5)
            .Select(i => RejectedTrial(i, i, $"FP{i:D3}", i))
            .ToList();

        var result = svc.FinalizeTrainingSelection(TrainingExperiment(), trials);

        Assert.False(result.Succeeded);
        Assert.True(result.ShouldFailExperiment);
        Assert.Null(result.SelectedTrial);
        Assert.Equal(0, result.Population.EligibleTrialCount);
        Assert.Equal(ValidationSelectionIntegrityStatus.FailedNoEligibleTrials, result.IntegrityStatus);
        Assert.Equal(StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails, result.FailureCode);
    }

    [Fact]
    public void ExactlyOneEligible_SelectsCorrectTrial()
    {
        var svc = new ValidationTrainingSelectionService();
        var trials = new List<ValidationParameterTrial>
        {
            RejectedTrial(1, 1, "REJ1"),
            RejectedTrial(2, 2, "REJ2"),
            EligibleTrial(3, 3, "WIN", 5m)
        };

        var result = svc.FinalizeTrainingSelection(TrainingExperiment(), trials);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.SelectedTrial);
        Assert.Equal(3, result.SelectedTrial!.Id);
        Assert.Equal(1, result.SelectedTrial.Rank);
        Assert.Equal(ValidationSelectionIntegrityStatus.Passed, result.IntegrityStatus);
    }

    [Fact]
    public void MultipleEligible_DeterministicRankingAndWinner()
    {
        var svc = new ValidationTrainingSelectionService();
        var trials = new List<ValidationParameterTrial>
        {
            EligibleTrial(1, 1, "LOW", 1m, 0.05m),
            EligibleTrial(2, 2, "HIGH", 10m, 0.20m),
            EligibleTrial(3, 3, "MID", 5m, 0.10m),
            RejectedTrial(4, 4, "REJ")
        };

        var first = svc.FinalizeTrainingSelection(TrainingExperiment(), trials);
        var second = svc.FinalizeTrainingSelection(TrainingExperiment(), trials);

        Assert.Equal(first.SelectedTrial!.Id, second.SelectedTrial!.Id);
        Assert.Equal(2, first.SelectedTrial.Id);
        Assert.Equal(1, first.SelectedTrial.Rank);
    }

    [Fact]
    public void Fingerprint_CanonicalAndReproducible()
    {
        var fp = new ValidationParameterFingerprintService();
        var a = new Dictionary<string, string> { ["z"] = "2", ["a"] = "1" };
        var b = new Dictionary<string, string> { ["a"] = "1", ["z"] = "2" };

        var fp1 = fp.ComputeFingerprint(a);
        var fp2 = fp.ComputeFingerprint(b);
        Assert.Equal(fp1, fp2);
        Assert.NotEqual(fp1, fp.ComputeFingerprint(new Dictionary<string, string> { ["a"] = "2" }));
    }

    [Fact]
    public void Fingerprint_RejectsEmptyAndEmptyHashArtifact()
    {
        var fp = new ValidationParameterFingerprintService();
        Assert.Equal(FrozenSnapshotValidationStatus.Missing, fp.ValidateParameterSnapshot(null));
        Assert.Equal(FrozenSnapshotValidationStatus.Missing, fp.ValidateParameterSnapshot(""));
        Assert.Equal(FrozenSnapshotValidationStatus.Empty, fp.ValidateParameterSnapshot("{}"));
        Assert.Equal(FrozenSnapshotValidationStatus.InvalidJson, fp.ValidateParameterSnapshot("{bad"));
        Assert.True(fp.IsEmptyContentFingerprint(ValidationParameterFingerprintService.EmptyContentFingerprint));
    }

    [Fact]
    public void FreezeAndValidation_BlockedWhenFingerprintsMismatch()
    {
        var integrity = new ValidationSelectionIntegrityService(
            new ValidationParameterFingerprintService(),
            new ValidationTrainingSelectionService());

        var experiment = TrainingExperiment();
        experiment.Status = ValidationExperimentStatus.ConfigurationFrozen;
        experiment.SelectedTrialId = 1;
        experiment.SelectedTrialParameterFingerprint = "AAAA1111";
        experiment.FrozenParameterFingerprint = "BBBB2222";
        experiment.FrozenStrategyParameterSnapshotJson = "{\"k\":\"v\"}";
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.FailedParameterFingerprintMismatch;

        var trials = new List<ValidationParameterTrial> { EligibleTrial(1, 1, "AAAA1111", 1m) };

        Assert.False(integrity.CanFreeze(experiment, trials, out _));
        Assert.False(integrity.CanStartValidation(experiment, trials, out var reason));
        Assert.Contains("ValidationStartedWithoutEligibleTrainingWinner", reason);
    }

    [Fact]
    public void V13_RiskBasis_NormalizedWinningTrade()
    {
        var risk = new ValidationRiskBasisService();
        var c = RawClosed("W1", 100m, 99m, 2m, 1.8m);

        var basis = risk.ComputeTradeBasis(c, ValidationLayerType.RawStrategy);
        Assert.Equal(ValidationRiskBasisValidationStatus.Valid, basis.Status);
        Assert.Equal(1m, basis.DerivedRiskAmount);
        Assert.Equal(2m, basis.GrossRMultiple);
        Assert.Equal(1.8m, basis.NetRMultiple);
    }

    [Fact]
    public void V13_RiskBasis_NormalizedLosingTrade()
    {
        var risk = new ValidationRiskBasisService();
        var c = RawClosed("L1", 100m, 99m, -1m, -1.2m);

        var basis = risk.ComputeTradeBasis(c, ValidationLayerType.RawStrategy);
        Assert.Equal(-1m, basis.GrossRMultiple);
        Assert.Equal(-1.2m, basis.NetRMultiple);
    }

    [Fact]
    public void V13_Expectancy_IsAverageRPerTrade()
    {
        var risk = new ValidationRiskBasisService();
        var candidates = Enumerable.Range(1, 100).Select(i =>
            RawClosed($"T{i:D3}", 100m, 99m, -0.10m, -0.15m)).ToList();

        var metrics = ValidationMetricsContract.FromCandidatesV13(
            candidates, 1000, 0, ValidationLayerType.RawStrategy, risk);

        Assert.Equal(-0.10m, metrics.GrossExpectancyR);
        Assert.Equal(-0.15m, metrics.NetExpectancyR);
        Assert.NotEqual(-10m, metrics.NetExpectancyR);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.NetExpectancyApplicability);
    }

    [Fact]
    public void V13_InvalidRiskBasis_ReturnsNotEvaluatedNetExpectancy()
    {
        var risk = new ValidationRiskBasisService();
        var candidates = Enumerable.Range(1, 5).Select(i =>
            RawClosed($"B{i}", 100m, 99m, -1m, -1.2m, riskAmount: 0.49m)).ToList();

        var metrics = ValidationMetricsContract.FromCandidatesV13(
            candidates, 500, 0, ValidationLayerType.RawStrategy, risk);

        Assert.Null(metrics.NetExpectancyR);
        Assert.Equal(ValidationMetricApplicability.InvalidRiskBasis, metrics.NetExpectancyApplicability);
    }

    [Fact]
    public void ValidationStartGate_BlocksZeroEligibleAndRejectedSelection()
    {
        var integrity = new ValidationSelectionIntegrityService(
            new ValidationParameterFingerprintService(),
            new ValidationTrainingSelectionService());

        var experiment = TrainingExperiment();
        experiment.Status = ValidationExperimentStatus.ConfigurationFrozen;
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.FailedNoEligibleTrials;

        var rejected = RejectedTrial(1, 1, "REJ");
        experiment.SelectedTrialId = 1;
        experiment.FrozenStrategyParameterSnapshotJson = rejected.ParameterSnapshotJson;
        experiment.FrozenParameterFingerprint = rejected.ParameterFingerprint;
        experiment.SelectedTrialParameterFingerprint = rejected.ParameterFingerprint;

        Assert.False(integrity.CanStartValidation(experiment, [rejected], out var reason));
        Assert.Contains("ValidationStartedWithoutEligibleTrainingWinner", reason);
    }

    [Fact]
    public void HistoricalV12_FromCandidates_PreservesVersion()
    {
        var candidates = new[]
        {
            RawClosed("A", 100m, 99m, 1m, 0.9m, riskAmount: 1m, rawR: 1m)
        };
        var m = ValidationMetricsContract.FromCandidates(candidates, 100, 0, ValidationLayerType.RawStrategy);
        Assert.Equal(ValidationMetricsContract.VersionV12, m.MetricsVersion);
    }

    [Fact]
    public void HistoricalExperiment23_Metadata_NotSilentlyRepaired()
    {
        var auditor = new ValidationTrialSelectionAuditor();
        var experiment = new ValidationExperiment
        {
            Id = 23,
            Status = ValidationExperimentStatus.Completed,
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12,
            SelectedTrialId = 54,
            FrozenParameterFingerprint = ValidationParameterFingerprintService.EmptyContentFingerprint,
            SelectedTrialParameterFingerprint = "66D69E6D7AF1123F",
            TrainingStrategyLabRunId = 100
        };
        var trials = Enumerable.Range(1, 25).Select(i => new ValidationParameterTrial
        {
            Id = i,
            TrialNumber = i,
            ParameterFingerprint = i == 1 ? "66D69E6D7AF1123F" : $"FP{i}",
            Status = ValidationTrialStatus.GuardrailRejected,
            GuardrailDecision = "Failed",
            StrategyLabRunId = i == 1 ? 100 : 200 + i
        }).ToList();

        var audit = auditor.AuditSelection(experiment, trials);
        Assert.Equal(ValidationSelectionIntegrityStatus.InvalidSelectedTrial, audit.IntegrityStatus);
        Assert.Equal(ValidationMetricsContract.VersionV12, experiment.ValidationMetricsVersion);
    }

    [Fact]
    public void Readiness_HistoricalExp23SelectionViolation_IsWarningNotBlocker()
    {
        var readiness = new ValidationLaboratoryReadinessService(null!);
        var exp = new ValidationExperiment
        {
            Id = 23,
            Status = ValidationExperimentStatus.Completed,
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12,
            SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.InvalidSelectedTrial,
            ExportVerificationStatus = ValidationExportVerificationStatus.Passed
        };

        var status = readiness.EvaluateExperiment(exp);
        Assert.Equal(ValidationLaboratoryReadiness.ReadyWithWarnings, status);
    }
}
