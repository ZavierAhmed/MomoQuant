using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class SelectionIntegrityReport
{
    public long ExperimentId { get; init; }
    public ValidationSelectionIntegrityStatus Status { get; init; }
    public long? SelectedTrialId { get; init; }
    public int? SelectedTrialNumber { get; init; }
    public string? SelectedParameterFingerprint { get; init; }
    public string? FrozenParameterFingerprint { get; init; }
    public FrozenSnapshotValidationStatus SnapshotValidationStatus { get; init; }
    public bool FingerprintsMatch { get; init; }
    public bool IsEligibleForSelection { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
    public TrainingSelectionPopulationSummary? Population { get; init; }
}

public interface IValidationSelectionIntegrityService
{
    SelectionIntegrityReport Evaluate(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials);

    bool CanFreeze(ValidationExperiment experiment, IReadOnlyList<ValidationParameterTrial> trials, out string? reason);

    bool CanStartValidation(ValidationExperiment experiment, IReadOnlyList<ValidationParameterTrial> trials, out string? reason);
}

public sealed class ValidationSelectionIntegrityService : IValidationSelectionIntegrityService
{
    private readonly IValidationParameterFingerprintService _fingerprints;
    private readonly IValidationTrainingSelectionService _selection;

    public ValidationSelectionIntegrityService(
        IValidationParameterFingerprintService fingerprints,
        IValidationTrainingSelectionService selection)
    {
        _fingerprints = fingerprints;
        _selection = selection;
    }

    public SelectionIntegrityReport Evaluate(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials)
    {
        var violations = new List<string>();
        var population = _selection.SummarizePopulation(experiment, trials);
        var status = experiment.SelectionIntegrityStatus;

        if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            return new SelectionIntegrityReport
            {
                ExperimentId = experiment.Id,
                Status = ValidationSelectionIntegrityStatus.NotEvaluated,
                Population = population
            };
        }

        ValidationParameterTrial? selected = null;
        if (experiment.SelectedTrialId is long id)
        {
            selected = trials.FirstOrDefault(t => t.Id == id);
        }

        selected ??= trials.FirstOrDefault(t =>
            experiment.SelectedTrialParameterFingerprint is not null
            && string.Equals(t.ParameterFingerprint, experiment.SelectedTrialParameterFingerprint, StringComparison.Ordinal));

        if (population.EligibleTrialCount == 0 && population.TerminalTrialCount > 0)
        {
            violations.Add(
                population.GuardrailPassedTrialCount > 0
                    ? ValidationTrainingSelectionService.AuditIncompleteMessage
                    : ValidationTrainingSelectionService.ZeroEligibleMessage);
            status = ValidationSelectionIntegrityStatus.FailedNoEligibleTrials;
        }

        if (experiment.SelectedTrialId is not null && selected is null)
        {
            violations.Add("Selected trial ID does not match any persisted trial.");
            status = ValidationSelectionIntegrityStatus.FailedSelectedTrialMissing;
        }

        if (selected is not null)
        {
            var eligible = ValidationAuthoritativeAuditQualificationEvaluator.MeetsCachedAuditEligibilityFields(selected);
            if (!eligible && experiment.SelectionIntegrityStatus != ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback)
            {
                var auditReason =
                    selected.AuthoritativeAuditExecutionId is null
                    || selected.AuditCompletionStatus != ValidationAuditCompletionStatus.Complete
                        ? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage
                        : "Selected trial did not pass training guardrails.";
                // Prefer selected-trial audit ineligibility as the leading freeze/validation reason.
                violations.Insert(0, auditReason);
                status = ValidationSelectionIntegrityStatus.FailedSelectedTrialIneligible;
            }

            if (!selected.Rank.HasValue && eligible)
            {
                violations.Add("Selected trial has no training rank.");
                status = ValidationSelectionIntegrityStatus.FailedSelectedTrialNotRanked;
            }
        }
        else if (population.EligibleTrialCount > 0 && experiment.Status >= ValidationExperimentStatus.TrainingCompleted)
        {
            violations.Add("No selected trial persisted despite eligible trials.");
            status = ValidationSelectionIntegrityStatus.FailedSelectedTrialMissing;
        }

        var snapshotStatus = _fingerprints.ValidateParameterSnapshot(
            experiment.FrozenStrategyParameterSnapshotJson,
            requireNonEmptyParameters: experiment.ExperimentType == ValidationExperimentType.TrainingSearchHoldoutValidation);

        if (experiment.Status >= ValidationExperimentStatus.ConfigurationFrozen)
        {
            if (snapshotStatus == FrozenSnapshotValidationStatus.Missing)
            {
                violations.Add("Frozen parameter snapshot is missing.");
                status = ValidationSelectionIntegrityStatus.FailedFrozenSnapshotMissing;
            }
            else if (snapshotStatus is FrozenSnapshotValidationStatus.Empty or FrozenSnapshotValidationStatus.InvalidJson)
            {
                violations.Add("Frozen parameter snapshot is empty or invalid.");
                status = ValidationSelectionIntegrityStatus.FailedFrozenSnapshotEmpty;
            }

            if (_fingerprints.IsEmptyContentFingerprint(experiment.FrozenParameterFingerprint))
            {
                violations.Add($"Frozen fingerprint is empty-content artifact ({ValidationParameterFingerprintService.EmptyContentFingerprint}).");
                status = ValidationSelectionIntegrityStatus.FailedFrozenFingerprintInvalid;
            }

            if (experiment.SelectedTrialParameterFingerprint is not null
                && experiment.FrozenParameterFingerprint is not null
                && !string.Equals(experiment.SelectedTrialParameterFingerprint, experiment.FrozenParameterFingerprint, StringComparison.Ordinal))
            {
                violations.Add("Selected and frozen parameter fingerprints do not match.");
                status = ValidationSelectionIntegrityStatus.FailedParameterFingerprintMismatch;
            }
        }

        var fingerprintsMatch = experiment.SelectedTrialParameterFingerprint is not null
            && experiment.FrozenParameterFingerprint is not null
            && string.Equals(experiment.SelectedTrialParameterFingerprint, experiment.FrozenParameterFingerprint, StringComparison.Ordinal);

        if (violations.Count == 0 && status is ValidationSelectionIntegrityStatus.NotEvaluated or ValidationSelectionIntegrityStatus.Valid)
        {
            status = ValidationSelectionIntegrityStatus.Passed;
        }

        return new SelectionIntegrityReport
        {
            ExperimentId = experiment.Id,
            Status = status,
            SelectedTrialId = selected?.Id ?? experiment.SelectedTrialId,
            SelectedTrialNumber = selected?.TrialNumber ?? experiment.SelectedTrialNumber,
            SelectedParameterFingerprint = experiment.SelectedTrialParameterFingerprint ?? selected?.ParameterFingerprint,
            FrozenParameterFingerprint = experiment.FrozenParameterFingerprint,
            SnapshotValidationStatus = snapshotStatus,
            FingerprintsMatch = fingerprintsMatch,
            IsEligibleForSelection = selected is not null
                && ValidationAuthoritativeAuditQualificationEvaluator.MeetsCachedAuditEligibilityFields(selected),
            Violations = violations,
            Population = population
        };
    }

    public bool CanFreeze(ValidationExperiment experiment, IReadOnlyList<ValidationParameterTrial> trials, out string? reason)
    {
        reason = null;
        if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            return true;
        }

        var report = Evaluate(experiment, trials);
        if (report.Status is not ValidationSelectionIntegrityStatus.Passed
            and not ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback)
        {
            reason = report.Violations.FirstOrDefault() ?? $"Selection integrity status: {report.Status}";
            return false;
        }

        if (experiment.SelectedTrialId is null)
        {
            reason = "No eligible training trial was selected.";
            return false;
        }

        return true;
    }

    public bool CanStartValidation(ValidationExperiment experiment, IReadOnlyList<ValidationParameterTrial> trials, out string? reason)
    {
        reason = null;

        // 1. Common frozen snapshot / fingerprint integrity (all experiment types).
        var snapshotStatus = _fingerprints.ValidateParameterSnapshot(
            experiment.FrozenStrategyParameterSnapshotJson,
            requireNonEmptyParameters: true);
        if (snapshotStatus != FrozenSnapshotValidationStatus.Valid)
        {
            reason = $"ValidationStartedWithoutEligibleTrainingWinner: frozen snapshot status {snapshotStatus}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint)
            || _fingerprints.IsEmptyContentFingerprint(experiment.FrozenParameterFingerprint))
        {
            reason = "ValidationStartedWithoutEligibleTrainingWinner: frozen fingerprint is invalid empty-content hash.";
            return false;
        }

        // 2. Existing-frozen: generic configuration integrity only — no training selection artifacts.
        if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            return true;
        }

        // 3. Training-search: selection-integrity and selected-trial requirements.
        if (!CanFreeze(experiment, trials, out reason))
        {
            reason = $"ValidationStartedWithoutEligibleTrainingWinner: {reason}";
            return false;
        }

        if (experiment.SelectedTrialParameterFingerprint is not null
            && experiment.FrozenParameterFingerprint is not null
            && !string.Equals(experiment.SelectedTrialParameterFingerprint, experiment.FrozenParameterFingerprint, StringComparison.Ordinal))
        {
            reason = "ValidationStartedWithoutEligibleTrainingWinner: selected and frozen fingerprints differ.";
            return false;
        }

        if (experiment.SelectionIntegrityStatus is not ValidationSelectionIntegrityStatus.Passed
            and not ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback
            and not ValidationSelectionIntegrityStatus.Valid)
        {
            reason = $"ValidationStartedWithoutEligibleTrainingWinner: selection integrity {experiment.SelectionIntegrityStatus}.";
            return false;
        }

        return true;
    }
}
