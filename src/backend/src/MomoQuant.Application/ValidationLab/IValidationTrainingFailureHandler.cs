using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Research;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationTrainingFailureHandleResult
{
    public string ErrorCode { get; init; } = ValidationTrainingFailureCodes.ValidationDataLeakage;
    public string UserSafeErrorMessage { get; init; } = string.Empty;
    public ValidationTrainingFailureAggregate Aggregate { get; init; } = new();
}

/// <summary>
/// Owns production status transitions for training boundary / leakage / audit-persistence failures.
/// </summary>
public interface IValidationTrainingFailureHandler
{
    /// <summary>
    /// Persists pending access evidence, marks trial/experiment failed for leakage,
    /// invalidates tentative selection, and writes safe operation-status diagnostics.
    /// Does not expose stack traces or candle contents in user-facing errors.
    /// </summary>
    Task<ValidationTrainingFailureHandleResult> HandleBoundaryFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IValidationTrainingCandleScope scope,
        Exception exception,
        string? optimizerInputFingerprint = null,
        string? leaseOwner = null,
        ValidationTrainingFailureAggregate? observedFailures = null,
        bool scopeFlushAlreadyAttempted = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fail-closed path when access audit evidence cannot be durably confirmed.
    /// Marks trial AuditPersistenceFailed, invalidates selection/freeze, and ensures leakage cannot report Passed.
    /// </summary>
    Task<ValidationTrainingFailureHandleResult> HandleAuditPersistenceFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        Exception exception,
        string? leaseOwner = null,
        ValidationTrainingFailureAggregate? observedFailures = null,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrainingFailureHandler : IValidationTrainingFailureHandler
{
    public const string UserSafeLeakageMessage =
        "Validation data leakage was detected during training. Training stopped and access evidence was recorded for audit.";

    public const string UserSafeAuditPersistenceMessage =
        "Validation candle access audit evidence could not be confirmed as durable. Training stopped without ranking, selection, or freeze.";

    public const string UserSafeCleanupMessage =
        "Validation training cleanup failed after the primary failure was recorded. Ranking, selection, and qualification remain blocked.";

    private readonly IValidationCandleAccessRecorder _recorder;
    private readonly IValidationCandleAccessAuditRepository _audits;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationExperimentRepository _experiments;
    private readonly IValidationLeakageAuditor _leakageAuditor;
    private readonly IResearchOperationStatusService _operationStatus;

    public ValidationTrainingFailureHandler(
        IValidationCandleAccessRecorder recorder,
        IValidationCandleAccessAuditRepository audits,
        IValidationParameterTrialRepository trials,
        IValidationExperimentRepository experiments,
        IValidationLeakageAuditor leakageAuditor,
        IResearchOperationStatusService operationStatus)
    {
        _recorder = recorder;
        _audits = audits;
        _trials = trials;
        _experiments = experiments;
        _leakageAuditor = leakageAuditor;
        _operationStatus = operationStatus;
    }

    public async Task<ValidationTrainingFailureHandleResult> HandleBoundaryFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IValidationTrainingCandleScope scope,
        Exception exception,
        string? optimizerInputFingerprint = null,
        string? leaseOwner = null,
        ValidationTrainingFailureAggregate? observedFailures = null,
        bool scopeFlushAlreadyAttempted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(exception);

        var aggregate = BuildAggregate(experiment, observedFailures);
        aggregate.Observe(exception, ValidationTrainingFailurePhase.TrialBody);

        if (!scopeFlushAlreadyAttempted)
        {
            try
            {
                await _recorder.FlushAsync(scope, cancellationToken).ConfigureAwait(false);
            }
            catch (ValidationAccessEvidencePersistenceException persistEx)
            {
                aggregate.Observe(persistEx, ValidationTrainingFailurePhase.TrialScopeFlush);
            }
            catch (Exception flushEx)
            {
                aggregate.Observe(flushEx, ValidationTrainingFailurePhase.TrialScopeFlush);
            }
        }

        if (aggregate.HasAuditDurabilityFailure && !aggregate.HasBoundaryFailure)
        {
            return await HandleAuditPersistenceFailureAsync(
                    experiment,
                    trial,
                    exception,
                    leaseOwner,
                    aggregate,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ApplyBoundaryTrialMutation(trial, aggregate);
        InvalidateTentativeSelection(experiment);
        ApplyBoundaryExperimentMutation(experiment, aggregate);

        if (experiment.ValidationStartUtc is not null
            && experiment.TrainingStartUtc is not null
            && experiment.TrainingEndUtc is not null)
        {
            try
            {
                var audits = await _audits.GetByExperimentIdAsync(experiment.Id, cancellationToken)
                    .ConfigureAwait(false);
                var leakage = _leakageAuditor.EvaluateFromAccessEvidence(
                    audits,
                    experiment.ValidationStartUtc.Value,
                    experiment.TrainingStartUtc.Value,
                    experiment.TrainingEndUtc.Value,
                    optimizerInputFingerprint ?? string.Empty);
                experiment.LeakageAuditJson = _leakageAuditor.Serialize(leakage);
                experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
            }
            catch (Exception leakageEx)
            {
                aggregate.Observe(leakageEx, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
                ApplyBoundaryExperimentMutation(experiment, aggregate);
            }
        }

        await PersistFailureStateSafelyAsync(experiment, trial, aggregate, cancellationToken)
            .ConfigureAwait(false);
        await SyncOperationStatusSafelyAsync(
                experiment,
                aggregate,
                stage: "LeakageDetected",
                leaseOwner,
                cancellationToken)
            .ConfigureAwait(false);

        return ToHandleResult(aggregate);
    }

    public async Task<ValidationTrainingFailureHandleResult> HandleAuditPersistenceFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        Exception exception,
        string? leaseOwner = null,
        ValidationTrainingFailureAggregate? observedFailures = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(exception);

        var aggregate = BuildAggregate(experiment, observedFailures);
        // Callers that already observed the failure at AuditFinalization / CompletenessVerification
        // must retain that phase. Only observe here when no prior observed failures were supplied.
        if (observedFailures is null || !observedFailures.HasAnyFailure)
        {
            aggregate.Observe(exception, ValidationTrainingFailurePhase.TrialScopeFlush);
        }

        ApplyAuditTrialMutation(trial, aggregate);
        InvalidateTentativeSelection(experiment);
        ApplyAuditExperimentMutation(experiment, aggregate);

        await PersistFailureStateSafelyAsync(experiment, trial, aggregate, cancellationToken)
            .ConfigureAwait(false);
        await SyncOperationStatusSafelyAsync(
                experiment,
                aggregate,
                stage: "AuditPersistenceFailed",
                leaseOwner,
                cancellationToken)
            .ConfigureAwait(false);

        return ToHandleResult(aggregate);
    }

    private async Task PersistFailureStateSafelyAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        ValidationTrainingFailureAggregate aggregate,
        CancellationToken cancellationToken)
    {
        try
        {
            await ValidationTrainingDbRetry.ExecuteAsync(
                    () => _trials.UpdateAsync(trial, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception trialPersistEx)
        {
            aggregate.Observe(trialPersistEx, ValidationTrainingFailurePhase.TrialStatusPersistence);
            RefreshSafeMessages(experiment, trial, aggregate);
            try
            {
                await ValidationTrainingDbRetry.ExecuteAsync(
                        () => _trials.UpdateAsync(trial, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception retryEx)
            {
                aggregate.Observe(retryEx, ValidationTrainingFailurePhase.TrialStatusPersistence);
                RefreshSafeMessages(experiment, trial, aggregate);
            }
        }

        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
        experiment.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await ValidationTrainingDbRetry.ExecuteAsync(
                    () => _experiments.UpdateAsync(experiment, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception experimentPersistEx)
        {
            aggregate.Observe(experimentPersistEx, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
            RefreshSafeMessages(experiment, trial, aggregate);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            try
            {
                await ValidationTrainingDbRetry.ExecuteAsync(
                        () => _experiments.UpdateAsync(experiment, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Bounded best-effort only — retain the in-memory aggregate and original primary.
            }
        }
    }

    private async Task SyncOperationStatusSafelyAsync(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate aggregate,
        string stage,
        string? leaseOwner,
        CancellationToken cancellationToken)
    {
        var primary = aggregate.PrimaryFailure!;
        IReadOnlyList<ValidationParameterTrial> trials;
        try
        {
            trials = await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            trials = Array.Empty<ValidationParameterTrial>();
        }

        var progress = ValidationTrainingProgressCalculator.Calculate(
            experiment,
            trials,
            generatedTrialCount: experiment.MaximumTrials);

        try
        {
            await _operationStatus.SyncFromValidationTrainingAsync(
                experiment.Id,
                status: ValidationExperimentStatus.Failed.ToString(),
                stage: stage,
                progress,
                leaseOwner: leaseOwner,
                errorCode: primary.Code,
                userSafeError: primary.UserSafeMessage,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception syncEx)
        {
            aggregate.Observe(syncEx, ValidationTrainingFailurePhase.OperationStatusSync);
            RefreshSafeMessages(experiment, trial: null, aggregate);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            try
            {
                await ValidationTrainingDbRetry.ExecuteAsync(
                        () => _experiments.UpdateAsync(experiment, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception secondaryPersistEx)
            {
                aggregate.Observe(secondaryPersistEx, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
                RefreshSafeMessages(experiment, trial: null, aggregate);
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                try
                {
                    await ValidationTrainingDbRetry.ExecuteAsync(
                            () => _experiments.UpdateAsync(experiment, cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Bounded best-effort only — retain the in-memory aggregate and original primary.
                    // Do not recurse into Handle*FailureAsync.
                }
            }
        }
    }

    private static void ApplyBoundaryTrialMutation(
        ValidationParameterTrial trial,
        ValidationTrainingFailureAggregate aggregate)
    {
        var primary = aggregate.PrimaryFailure!;
        trial.Status = ValidationTrialStatus.LeakageFailed;
        trial.ErrorMessage = primary.UserSafeMessage;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.AuditCompletionStatus = aggregate.HasAuditDurabilityFailure
            ? ValidationAuditCompletionStatus.RecoveryRequired
            : trial.AuditCompletionStatus;
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible;
        trial.Rank = null;
        ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
            trial,
            aggregate.AllFailures.Where(f => f.IsQualificationBlocking).Select(f => f.Code).ToArray());
    }

    private static void ApplyAuditTrialMutation(
        ValidationParameterTrial trial,
        ValidationTrainingFailureAggregate aggregate)
    {
        var primary = aggregate.PrimaryFailure!;
        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
        trial.ErrorMessage = primary.UserSafeMessage;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible;
        trial.Rank = null;
        ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
            trial,
            aggregate.AllFailures.Where(f => f.IsQualificationBlocking).Select(f => f.Code).ToArray());
    }

    private static void ApplyBoundaryExperimentMutation(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate aggregate)
    {
        var primary = aggregate.PrimaryFailure!;
        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        experiment.CurrentStage = "LeakageDetected";
        experiment.Status = ValidationExperimentStatus.Failed;
        experiment.ErrorMessage = primary.UserSafeMessage;
        experiment.DecidedAtUtc = DateTime.UtcNow;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        experiment.IsQualificationCapable = false;
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
    }

    private static void ApplyAuditExperimentMutation(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate aggregate)
    {
        var primary = aggregate.PrimaryFailure!;
        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        experiment.CurrentStage = "AuditPersistenceFailed";
        experiment.Status = ValidationExperimentStatus.Failed;
        experiment.ErrorMessage = primary.UserSafeMessage;
        experiment.DecidedAtUtc = DateTime.UtcNow;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        experiment.IsQualificationCapable = false;
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
    }

    private static void RefreshSafeMessages(
        ValidationExperiment experiment,
        ValidationParameterTrial? trial,
        ValidationTrainingFailureAggregate aggregate)
    {
        var primary = aggregate.PrimaryFailure;
        if (primary is null)
        {
            return;
        }

        experiment.ErrorMessage = primary.UserSafeMessage;
        experiment.PrimaryFailureReason = primary.Code;
        experiment.IsQualificationCapable = false;
        if (trial is not null)
        {
            trial.ErrorMessage = primary.UserSafeMessage;
        }
    }

    private static ValidationTrainingFailureHandleResult ToHandleResult(
        ValidationTrainingFailureAggregate aggregate)
    {
        var primary = aggregate.PrimaryFailure!;
        return new ValidationTrainingFailureHandleResult
        {
            ErrorCode = primary.Code,
            UserSafeErrorMessage = primary.UserSafeMessage,
            Aggregate = aggregate
        };
    }

    private static ValidationTrainingFailureAggregate BuildAggregate(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate? observedFailures)
    {
        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        aggregate.MergeFrom(observedFailures);
        return aggregate;
    }

    private static void InvalidateTentativeSelection(ValidationExperiment experiment)
    {
        experiment.SelectedTrialId = null;
        experiment.SelectedTrialNumber = null;
        experiment.SelectedTrialParameterSnapshotJson = null;
        experiment.SelectedTrialParameterFingerprint = null;
        experiment.TrainingStrategyLabRunId = null;
        experiment.ValidationStrategyLabRunId = null;
        experiment.FrozenStrategyParameterSnapshotJson = null;
        experiment.FrozenParameterFingerprint = null;
        experiment.FrozenAtUtc = null;
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NoEligibleTrial;
    }
}
