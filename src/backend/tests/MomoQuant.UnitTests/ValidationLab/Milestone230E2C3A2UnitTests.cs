using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C3A2 — CanStartValidation applicability split unit coverage.</summary>
public sealed class Milestone230E2C3A2UnitTests
{
    private readonly ValidationSelectionIntegrityService _integrity = new(
        new ValidationParameterFingerprintService(),
        new ValidationTrainingSelectionService());

    private readonly ValidationParameterFingerprintService _fingerprints = new();

    [Fact]
    public void CanStartValidation_ExistingFrozen_NotEvaluatedIntegrity_AllowsStart()
    {
        var experiment = ExistingFrozen();
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NotEvaluated;
        experiment.IsQualificationCapable = false;
        experiment.SelectedTrialId = null;

        Assert.True(_integrity.CanStartValidation(experiment, [], out var reason), reason);
    }

    [Fact]
    public void CanStartValidation_ExistingFrozen_InvalidSnapshot_Blocks()
    {
        var experiment = ExistingFrozen();
        experiment.FrozenStrategyParameterSnapshotJson = "{bad";

        Assert.False(_integrity.CanStartValidation(experiment, [], out var reason));
        Assert.Contains("frozen snapshot", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanStartValidation_ExistingFrozen_EmptyContentFingerprint_Blocks()
    {
        var experiment = ExistingFrozen();
        experiment.FrozenParameterFingerprint = ValidationParameterFingerprintService.EmptyContentFingerprint;

        Assert.False(_integrity.CanStartValidation(experiment, [], out var reason));
        Assert.Contains("empty-content", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanStartValidation_TrainingSearch_NotEvaluatedIntegrity_Blocks()
    {
        var experiment = TrainingFrozen();
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NotEvaluated;

        var trial = EligibleTrial(experiment);
        Assert.False(_integrity.CanStartValidation(experiment, [trial], out var reason));
        Assert.Contains("selection integrity", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private ValidationExperiment ExistingFrozen()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["swingLeftBars"] = "1",
            ["swingRightBars"] = "1"
        };
        var snapshot = """{"swingLeftBars":"1","swingRightBars":"1"}""";
        return new ValidationExperiment
        {
            Id = 42,
            ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration,
            Status = ValidationExperimentStatus.ConfigurationFrozen,
            FrozenStrategyParameterSnapshotJson = snapshot,
            FrozenParameterFingerprint = _fingerprints.ComputeFingerprint(parameters),
            SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NotEvaluated,
            IsQualificationCapable = false,
            SelectedTrialId = null,
            ValidationStartUtc = DateTime.UtcNow.AddDays(-3),
            ValidationEndUtc = DateTime.UtcNow.AddDays(-1)
        };
    }

    private ValidationExperiment TrainingFrozen()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["swingLeftBars"] = "1",
            ["swingRightBars"] = "1"
        };
        var snapshot = """{"swingLeftBars":"1","swingRightBars":"1"}""";
        var fp = _fingerprints.ComputeFingerprint(parameters);
        return new ValidationExperiment
        {
            Id = 43,
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            Status = ValidationExperimentStatus.ConfigurationFrozen,
            FrozenStrategyParameterSnapshotJson = snapshot,
            FrozenParameterFingerprint = fp,
            SelectedTrialId = 1,
            SelectedTrialNumber = 1,
            SelectedTrialParameterFingerprint = fp,
            SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.Passed,
            IsQualificationCapable = true,
            ValidationStartUtc = DateTime.UtcNow.AddDays(-3),
            ValidationEndUtc = DateTime.UtcNow.AddDays(-1)
        };
    }

    private static ValidationParameterTrial EligibleTrial(ValidationExperiment experiment) =>
        new()
        {
            Id = experiment.SelectedTrialId ?? 1,
            ValidationExperimentId = experiment.Id,
            TrialNumber = 1,
            ParameterFingerprint = experiment.SelectedTrialParameterFingerprint ?? "fp",
            ParameterSnapshotJson = experiment.FrozenStrategyParameterSnapshotJson ?? "{}",
            Status = ValidationTrialStatus.Completed,
            GuardrailDecision = "Passed",
            Rank = 1,
            AuthoritativeAuditExecutionId = Guid.NewGuid(),
            AuditCompletionStatus = ValidationAuditCompletionStatus.Complete,
            ClosedTradeCount = 10,
            TrainingScore = 1m
        };
}
