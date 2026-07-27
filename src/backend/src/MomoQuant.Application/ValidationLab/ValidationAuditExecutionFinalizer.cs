using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationAuditExecutionCompletionResult
{
    public Guid AuditExecutionId { get; init; }
    public bool IsComplete { get; init; }
    public ValidationAuditCompletenessCode CompletionCode { get; init; }
    public long FinalExpectedSequence { get; init; }
    public string? FinalPayloadSetHash { get; init; }
    public string? FailureCode { get; init; }
}

public interface IValidationAuditExecutionFinalizer
{
    Task<ValidationAuditExecutionCompletionResult> CompleteAsync(
        Guid auditExecutionId,
        long finalExpectedSequence,
        CancellationToken cancellationToken = default);
}

/// <summary>Durable final-expected-sequence declaration and completion (WP7).</summary>
public sealed class ValidationAuditExecutionFinalizer : IValidationAuditExecutionFinalizer
{
    private readonly IValidationAuditExecutionRepository _executions;
    private readonly IValidationAuditBatchRepository _batches;
    private readonly IValidationCandleAccessAuditRepository _accessAudits;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationAuditCompletenessVerifier _verifier;
    private readonly IValidationAuditPayloadSetHasher _hasher;
    private readonly IValidationAuditUnitOfWork _uow;

    public ValidationAuditExecutionFinalizer(
        IValidationAuditExecutionRepository executions,
        IValidationAuditBatchRepository batches,
        IValidationCandleAccessAuditRepository accessAudits,
        IValidationParameterTrialRepository trials,
        IValidationAuditCompletenessVerifier verifier,
        IValidationAuditPayloadSetHasher hasher,
        IValidationAuditUnitOfWork uow)
    {
        _executions = executions;
        _batches = batches;
        _accessAudits = accessAudits;
        _trials = trials;
        _verifier = verifier;
        _hasher = hasher;
        _uow = uow;
    }

    public async Task<ValidationAuditExecutionCompletionResult> CompleteAsync(
        Guid auditExecutionId,
        long finalExpectedSequence,
        CancellationToken cancellationToken = default)
    {
        if (finalExpectedSequence < 0)
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_INVALID_FINAL_SEQUENCE",
                "FinalExpectedSequence must be >= 0.");
        }

        ValidationAuditExecutionCompletionResult? result = null;

        await _uow.ExecuteInTransactionAsync(async () =>
        {
            var execution = await _executions.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_EXECUTION_MISSING",
                    $"Audit execution {auditExecutionId} was not found.");

            if (execution.Status == ValidationAuditExecutionStatus.Superseded)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_CANNOT_COMPLETE_SUPERSEDED",
                    $"Audit execution {execution.AuditExecutionId} is Superseded and cannot complete.");
            }

            if (execution.Status == ValidationAuditExecutionStatus.Completed)
            {
                var completedBatches = (await _batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
                    .ConfigureAwait(false)).ToList();
                var completedAccess = await _accessAudits.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
                    .ConfigureAwait(false);
                var completedScopeRows = completedAccess
                    .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
                    .ToList();
                var completedTrials = await _trials.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
                    .ConfigureAwait(false);
                var completedTrial = completedTrials.FirstOrDefault(t => t.Id == execution.ValidationTrialId);
                var completedCompleteness = completedTrial is null
                    ? ValidationAuditCompletenessResult.ExecutionMissing()
                    : _verifier.Verify(completedTrial, execution, completedBatches, completedScopeRows);

                result = new ValidationAuditExecutionCompletionResult
                {
                    AuditExecutionId = execution.AuditExecutionId,
                    IsComplete = completedCompleteness.IsComplete,
                    CompletionCode = completedCompleteness.CompletionCode,
                    FinalExpectedSequence = execution.FinalExpectedSequence ?? finalExpectedSequence,
                    FinalPayloadSetHash = execution.FinalPayloadSetHash,
                    FailureCode = completedCompleteness.IsComplete
                        ? null
                        : completedCompleteness.CompletionCode.ToString()
                };
                return;
            }

            if (!string.Equals(
                    execution.AuditContractVersion,
                    ValidationAuditExecution.ContractVersionV1,
                    StringComparison.Ordinal))
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_UNKNOWN_CONTRACT_VERSION",
                    $"Unknown AuditContractVersion '{execution.AuditContractVersion}'. Expected '{ValidationAuditExecution.ContractVersionV1}'.");
            }

            if (execution.FinalExpectedSequence is long existing
                && existing != finalExpectedSequence)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_FINAL_SEQUENCE_LOCKED",
                    $"FinalExpectedSequence is already set to {existing} and cannot change to {finalExpectedSequence}.");
            }

            if (finalExpectedSequence == 0 && !execution.AllowsZeroAccess)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_ZERO_ACCESS_NOT_ALLOWED",
                    "Zero FinalExpectedSequence requires an explicit AllowsZeroAccess contract.");
            }

            var batches = (await _batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
                .ConfigureAwait(false)).ToList();
            var allAccess = await _accessAudits.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
                .ConfigureAwait(false);
            var scopeRows = allAccess
                .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
                .OrderBy(r => r.ScopeSequenceNumber)
                .ThenBy(r => r.AccessEventId)
                .ToList();

            var now = DateTime.UtcNow;
            execution.FinalExpectedSequence = finalExpectedSequence;
            execution.ExpectedEventCount = finalExpectedSequence == 0 ? 0 : (int)finalExpectedSequence;
            execution.UpdatedAtUtc = now;

            if (finalExpectedSequence > 0)
            {
                var entries = scopeRows
                    .Where(r => r.ScopeSequenceNumber >= 1 && r.ScopeSequenceNumber <= finalExpectedSequence)
                    .Select(r => new ValidationAuditPayloadSetEntry(
                        r.ScopeSequenceNumber,
                        r.AccessEventId,
                        r.AccessPayloadHash ?? string.Empty,
                        r.AccessPayloadContractVersion ?? ValidationAccessPayloadContractVersions.Current))
                    .ToList();

                if (entries.Count == finalExpectedSequence)
                {
                    execution.FinalPayloadSetHash = _hasher.ComputeSetHash(entries);
                }
            }
            else
            {
                execution.FinalPayloadSetHash = _hasher.ComputeSetHash(Array.Empty<ValidationAuditPayloadSetEntry>());
            }

            var trials = await _trials.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
                .ConfigureAwait(false);
            var trial = trials.FirstOrDefault(t => t.Id == execution.ValidationTrialId)
                ?? throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_TRIAL_MISSING",
                    $"Trial {execution.ValidationTrialId} was not found.");

            // Persist finalization intent before completeness verification.
            execution.RowVersion++;
            await _executions.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

            var completeness = _verifier.Verify(trial, execution, batches, scopeRows);
            // Evidence may be fully valid while Status is still EventsConfirmed — EvidenceSatisfied
            // allows the finalizer to promote to Completed. IsComplete alone requires Status==Completed.
            if (!completeness.EvidenceSatisfied
                && !completeness.IsComplete)
            {
                execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RestartRecoveryPending;
                execution.FailureCode = completeness.CompletionCode.ToString();
                execution.UpdatedAtUtc = DateTime.UtcNow;
                execution.RowVersion++;
                await _executions.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

                trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);

                result = new ValidationAuditExecutionCompletionResult
                {
                    AuditExecutionId = execution.AuditExecutionId,
                    IsComplete = false,
                    CompletionCode = completeness.CompletionCode,
                    FinalExpectedSequence = finalExpectedSequence,
                    FinalPayloadSetHash = execution.FinalPayloadSetHash,
                    FailureCode = completeness.CompletionCode.ToString()
                };
                return;
            }

            execution.Status = ValidationAuditExecutionStatus.Completed;
            execution.LastConfirmedSequence = finalExpectedSequence;
            execution.ConfirmedEventCount = execution.ExpectedEventCount ?? 0;
            execution.CompletedAtUtc = DateTime.UtcNow;
            execution.UpdatedAtUtc = execution.CompletedAtUtc.Value;
            execution.RowVersion++;
            await _executions.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
            await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (result is not null)
        {
            return result;
        }

        // Success path: terminal writes committed — reload and verify before returning.
        var reloadedExecution = await _executions.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_EXECUTION_MISSING",
                $"Audit execution {auditExecutionId} was not found after completion.");

        var reloadedBatches = (await _batches.GetByAuditExecutionIdAsync(reloadedExecution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false)).ToList();
        var reloadedAccess = await _accessAudits.GetByExperimentIdAsync(reloadedExecution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var reloadedScopeRows = reloadedAccess
            .Where(r => r.ScopeExecutionId == reloadedExecution.ScopeExecutionId)
            .ToList();
        var reloadedTrials = await _trials.GetByExperimentIdAsync(reloadedExecution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var reloadedTrial = reloadedTrials.FirstOrDefault(t => t.Id == reloadedExecution.ValidationTrialId);

        var verified = reloadedTrial is null
            ? ValidationAuditCompletenessResult.ExecutionMissing()
            : _verifier.Verify(reloadedTrial, reloadedExecution, reloadedBatches, reloadedScopeRows);

        return new ValidationAuditExecutionCompletionResult
        {
            AuditExecutionId = reloadedExecution.AuditExecutionId,
            IsComplete = verified.IsComplete,
            CompletionCode = verified.CompletionCode,
            FinalExpectedSequence = reloadedExecution.FinalExpectedSequence ?? finalExpectedSequence,
            FinalPayloadSetHash = reloadedExecution.FinalPayloadSetHash,
            FailureCode = verified.IsComplete ? null : verified.CompletionCode.ToString()
        };
    }
}

public interface IValidationTrialAuditCompletionGate
{
    bool CanMarkTrialCompleted(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        ValidationAuditCompletenessResult completeness);

    void ApplyCompletedStatus(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        ValidationAuditCompletenessResult completeness);
}

public sealed class ValidationTrialAuditCompletionGate : IValidationTrialAuditCompletionGate
{
    public bool CanMarkTrialCompleted(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        ValidationAuditCompletenessResult completeness)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(completeness);

        if (trial.AuthoritativeAuditExecutionId is null)
        {
            // Historical path: gate does not allow new Completed under durable contract.
            return false;
        }

        if (execution is null)
        {
            return false;
        }

        if (trial.AuthoritativeAuditExecutionId != execution.AuditExecutionId)
        {
            return false;
        }

        if (execution.Status != ValidationAuditExecutionStatus.Completed)
        {
            return false;
        }

        if (trial.AuditCompletionStatus != ValidationAuditCompletionStatus.Complete)
        {
            return false;
        }

        return completeness.IsComplete
               && completeness.CompletionCode == ValidationAuditCompletenessCode.Complete
               && completeness.IsAuthoritative;
    }

    public void ApplyCompletedStatus(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        ValidationAuditCompletenessResult completeness)
    {
        if (!CanMarkTrialCompleted(trial, execution, completeness))
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_TRIAL_COMPLETION_BLOCKED",
                $"Trial {trial.Id} cannot be marked Completed: audit completeness is {completeness.CompletionCode}.");
        }

        trial.Status = ValidationTrialStatus.Completed;
        trial.CompletedAtUtc ??= DateTime.UtcNow;
    }
}
