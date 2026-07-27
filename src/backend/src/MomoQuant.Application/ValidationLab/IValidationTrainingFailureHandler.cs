using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        var primary = aggregate.PrimaryFailure!;
        var errorCode = primary.Code;
        var userSafe = primary.UserSafeMessage;

        trial.Status = ValidationTrialStatus.LeakageFailed;
        trial.ErrorMessage = userSafe;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.AuditCompletionStatus = aggregate.HasAuditDurabilityFailure
            ? ValidationAuditCompletionStatus.RecoveryRequired
            : trial.AuditCompletionStatus;
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible;
        ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
            trial,
            aggregate.AllFailures.Where(f => f.IsQualificationBlocking).Select(f => f.Code).ToArray());
        await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);

        InvalidateTentativeSelection(experiment);
        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        experiment.CurrentStage = "LeakageDetected";
        experiment.Status = ValidationExperimentStatus.Failed;
        experiment.ErrorMessage = userSafe;
        experiment.DecidedAtUtc = DateTime.UtcNow;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);

        if (experiment.ValidationStartUtc is not null
            && experiment.TrainingStartUtc is not null
            && experiment.TrainingEndUtc is not null)
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

        await _experiments.UpdateAsync(experiment, cancellationToken).ConfigureAwait(false);

        var progress = ValidationTrainingProgressCalculator.Calculate(
            experiment,
            await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken).ConfigureAwait(false),
            generatedTrialCount: experiment.MaximumTrials);
        try
        {
            await _operationStatus.SyncFromValidationTrainingAsync(
                experiment.Id,
                status: ValidationExperimentStatus.Failed.ToString(),
                stage: "LeakageDetected",
                progress,
                leaseOwner: leaseOwner,
                errorCode: errorCode,
                userSafeError: userSafe,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception syncEx)
        {
            aggregate.Observe(syncEx, ValidationTrainingFailurePhase.OperationStatusSync);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            await _experiments.UpdateAsync(experiment, cancellationToken).ConfigureAwait(false);
        }

        return new ValidationTrainingFailureHandleResult
        {
            ErrorCode = errorCode,
            UserSafeErrorMessage = userSafe,
            Aggregate = aggregate
        };
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
        aggregate.Observe(exception, ValidationTrainingFailurePhase.TrialScopeFlush);

        var primary = aggregate.PrimaryFailure!;
        var errorCode = primary.Code;
        var userSafe = primary.UserSafeMessage;

        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
        trial.ErrorMessage = userSafe;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible;
        ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
            trial,
            aggregate.AllFailures.Where(f => f.IsQualificationBlocking).Select(f => f.Code).ToArray());
        await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);

        InvalidateTentativeSelection(experiment);
        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        experiment.CurrentStage = "AuditPersistenceFailed";
        experiment.Status = ValidationExperimentStatus.Failed;
        experiment.ErrorMessage = userSafe;
        experiment.DecidedAtUtc = DateTime.UtcNow;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);

        await _experiments.UpdateAsync(experiment, cancellationToken).ConfigureAwait(false);

        var progress = ValidationTrainingProgressCalculator.Calculate(
            experiment,
            await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken).ConfigureAwait(false),
            generatedTrialCount: experiment.MaximumTrials);
        try
        {
            await _operationStatus.SyncFromValidationTrainingAsync(
                experiment.Id,
                status: ValidationExperimentStatus.Failed.ToString(),
                stage: "AuditPersistenceFailed",
                progress,
                leaseOwner: leaseOwner,
                errorCode: errorCode,
                userSafeError: userSafe,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception syncEx)
        {
            aggregate.Observe(syncEx, ValidationTrainingFailurePhase.OperationStatusSync);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            await _experiments.UpdateAsync(experiment, cancellationToken).ConfigureAwait(false);
        }

        return new ValidationTrainingFailureHandleResult
        {
            ErrorCode = errorCode,
            UserSafeErrorMessage = userSafe,
            Aggregate = aggregate
        };
    }

    private static ValidationTrainingFailureAggregate BuildAggregate(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate? observedFailures)
    {
        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(
            experiment.FailureReasonsJson,
            experiment.DiagnosticsJson);
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
