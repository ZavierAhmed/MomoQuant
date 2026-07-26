using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationAuditExecutionRecoveryResult
{
    public Guid AuditExecutionId { get; init; }
    public ValidationAuditExecutionStatus PreviousStatus { get; init; }
    public ValidationAuditRecoveryDecision RecoveryDecision { get; init; }
    public int ConfirmedBatchCount { get; init; }
    public int UnresolvedBatchCount { get; init; }
    public long RecoveredLastConfirmedSequence { get; init; }
    public long? FinalExpectedSequence { get; init; }
    public bool CanContinueSameExecution { get; init; }
    public bool MustRerunTrial { get; init; }
    public bool IsComplete { get; init; }
    public string? FailureCode { get; init; }
}

public interface IValidationAuditExecutionRecoveryService
{
    Task<ValidationAuditExecutionRecoveryResult> RecoverAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Restart recovery for durable audit executions (WP6 rules A–E).</summary>
public sealed class ValidationAuditExecutionRecoveryService : IValidationAuditExecutionRecoveryService
{
    private readonly IValidationAuditExecutionRepository _executions;
    private readonly IValidationAuditBatchRepository _batches;
    private readonly IValidationCandleAccessAuditRepository _accessAudits;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationAuditCompletenessVerifier _verifier;
    private readonly IValidationAuditUnitOfWork _uow;

    public ValidationAuditExecutionRecoveryService(
        IValidationAuditExecutionRepository executions,
        IValidationAuditBatchRepository batches,
        IValidationCandleAccessAuditRepository accessAudits,
        IValidationParameterTrialRepository trials,
        IValidationAuditCompletenessVerifier verifier,
        IValidationAuditUnitOfWork uow)
    {
        _executions = executions;
        _batches = batches;
        _accessAudits = accessAudits;
        _trials = trials;
        _verifier = verifier;
        _uow = uow;
    }

    public async Task<ValidationAuditExecutionRecoveryResult> RecoverAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default)
    {
        var execution = await _executions.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_EXECUTION_MISSING",
                $"Audit execution {auditExecutionId} was not found.");

        var previousStatus = execution.Status;
        if (execution.Status == ValidationAuditExecutionStatus.Completed)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.AlreadyCompleted,
                ConfirmedBatchCount = 0,
                UnresolvedBatchCount = 0,
                RecoveredLastConfirmedSequence = execution.LastConfirmedSequence,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = false,
                IsComplete = true
            };
        }

        if (execution.Status == ValidationAuditExecutionStatus.Superseded)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.FailClosed,
                RecoveredLastConfirmedSequence = execution.LastConfirmedSequence,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = true,
                IsComplete = false,
                FailureCode = "VALIDATION_AUDIT_ALREADY_SUPERSEDED"
            };
        }

        // Rule E — multiple active executions fail closed.
        var active = await _executions.GetActiveByTrialIdAsync(execution.ValidationTrialId, cancellationToken)
            .ConfigureAwait(false);
        if (active.Count > 1)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.ConflictDetected,
                ConfirmedBatchCount = 0,
                UnresolvedBatchCount = 0,
                RecoveredLastConfirmedSequence = execution.LastConfirmedSequence,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = true,
                IsComplete = false,
                FailureCode = "VALIDATION_AUDIT_MULTIPLE_ACTIVE_EXECUTIONS"
            };
        }

        var batches = (await _batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false)).ToList();
        var allAccess = await _accessAudits.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var scopeRows = allAccess.Where(r => r.ScopeExecutionId == execution.ScopeExecutionId).ToList();

        var trials = await _trials.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var trial = trials.FirstOrDefault(t => t.Id == execution.ValidationTrialId);

        // Rule D — crash before first flush: execution exists, no batch manifest.
        if (batches.Count == 0)
        {
            await MarkRecoveryRequiredAsync(
                execution,
                trial,
                "PROCESS_INTERRUPTED_BEFORE_FLUSH",
                ValidationAuditRecoveryStatus.RestartRecoveryPending,
                cancellationToken).ConfigureAwait(false);

            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.SupersedeAndRerun,
                ConfirmedBatchCount = 0,
                UnresolvedBatchCount = 0,
                RecoveredLastConfirmedSequence = 0,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = true,
                IsComplete = false,
                FailureCode = "PROCESS_INTERRUPTED_BEFORE_FLUSH"
            };
        }

        var confirmedCount = 0;
        var unresolvedCount = 0;
        long recoveredLast = execution.LastConfirmedSequence;
        var now = DateTime.UtcNow;

        await _uow.ExecuteInTransactionAsync(async () =>
        {
            foreach (var batch in batches.OrderBy(b => b.BatchNumber))
            {
                if (batch.Status == ValidationAuditBatchStatus.Confirmed)
                {
                    confirmedCount++;
                    recoveredLast = Math.Max(recoveredLast, batch.LastSequence);
                    continue;
                }

                var ids = ParseGuidArray(batch.ExpectedEventIdsJson);
                var hashes = ParseStringArray(batch.ExpectedPayloadHashesJson);
                if (ids.Count == 0 || ids.Count != hashes.Count)
                {
                    unresolvedCount++;
                    continue;
                }

                var allConfirmed = true;
                for (var i = 0; i < ids.Count; i++)
                {
                    var row = scopeRows.FirstOrDefault(r => r.AccessEventId == ids[i]);
                    if (row is null
                        || string.IsNullOrWhiteSpace(row.AccessPayloadHash)
                        || !string.Equals(row.AccessPayloadHash, hashes[i], StringComparison.OrdinalIgnoreCase))
                    {
                        allConfirmed = false;
                        break;
                    }
                }

                if (allConfirmed)
                {
                    // Rule B — crash after event commit before cursor: confirm batch + advance.
                    batch.Status = ValidationAuditBatchStatus.Confirmed;
                    batch.ConfirmedAtUtc = now;
                    batch.UpdatedAtUtc = now;
                    batch.ConfirmationAttemptCount++;
                    batch.RowVersion++;
                    await _batches.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);
                    confirmedCount++;
                    recoveredLast = Math.Max(recoveredLast, batch.LastSequence);
                }
                else
                {
                    // Rule A — manifest exists but events missing / unrecoverable from hash alone.
                    unresolvedCount++;
                }
            }

            execution.LastConfirmedSequence = recoveredLast;
            execution.ConfirmedEventCount = (int)recoveredLast;
            execution.UpdatedAtUtc = now;
            execution.RowVersion++;

            if (unresolvedCount > 0 && confirmedCount == 0 && recoveredLast == 0)
            {
                execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RestartRecoveryPending;
                execution.FailureCode = "AUDIT_EVENT_PAYLOAD_UNRECOVERABLE";
            }
            else if (unresolvedCount > 0)
            {
                execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RestartRecoveryPending;
                execution.FailureCode = "AUDIT_MANIFEST_INCOMPLETE";
            }
            else
            {
                execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RecoveredFromConfirmedBatch;
            }

            await _executions.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

            if (trial is not null && unresolvedCount > 0)
            {
                trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        // Reload for completeness (Rule C).
        execution = await _executions.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken)
            .ConfigureAwait(false) ?? execution;
        batches = (await _batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false)).ToList();
        scopeRows = (await _accessAudits.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false))
            .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
            .ToList();

        if (trial is null)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.FailClosed,
                ConfirmedBatchCount = confirmedCount,
                UnresolvedBatchCount = unresolvedCount,
                RecoveredLastConfirmedSequence = recoveredLast,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = true,
                IsComplete = false,
                FailureCode = "VALIDATION_AUDIT_TRIAL_MISSING"
            };
        }

        var completeness = _verifier.Verify(trial, execution, batches, scopeRows);
        if (completeness.IsComplete)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.AlreadyCompleted,
                ConfirmedBatchCount = confirmedCount,
                UnresolvedBatchCount = 0,
                RecoveredLastConfirmedSequence = recoveredLast,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = false,
                IsComplete = true
            };
        }

        if (unresolvedCount > 0 && confirmedCount == 0)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.SupersedeAndRerun,
                ConfirmedBatchCount = confirmedCount,
                UnresolvedBatchCount = unresolvedCount,
                RecoveredLastConfirmedSequence = recoveredLast,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = true,
                IsComplete = false,
                FailureCode = execution.FailureCode ?? "AUDIT_EVENT_PAYLOAD_UNRECOVERABLE"
            };
        }

        if (unresolvedCount > 0)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.ResumePendingManifest,
                ConfirmedBatchCount = confirmedCount,
                UnresolvedBatchCount = unresolvedCount,
                RecoveredLastConfirmedSequence = recoveredLast,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                CanContinueSameExecution = false,
                MustRerunTrial = true,
                IsComplete = false,
                FailureCode = execution.FailureCode ?? "AUDIT_MANIFEST_INCOMPLETE"
            };
        }

        // Rule C — all batches confirmed / unresolvedCount==0.
        // Missing terminal marker is incomplete, NOT a supersede/rerun trigger.
        if (execution.FinalExpectedSequence is null)
        {
            return new ValidationAuditExecutionRecoveryResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                PreviousStatus = previousStatus,
                RecoveryDecision = ValidationAuditRecoveryDecision.ConfirmedCommittedBatch,
                ConfirmedBatchCount = confirmedCount,
                UnresolvedBatchCount = 0,
                RecoveredLastConfirmedSequence = recoveredLast,
                FinalExpectedSequence = null,
                CanContinueSameExecution = true,
                MustRerunTrial = false,
                IsComplete = false,
                FailureCode = "FINAL_SEQUENCE_NOT_DECLARED"
            };
        }

        return new ValidationAuditExecutionRecoveryResult
        {
            AuditExecutionId = execution.AuditExecutionId,
            PreviousStatus = previousStatus,
            RecoveryDecision = ValidationAuditRecoveryDecision.ConfirmedCommittedBatch,
            ConfirmedBatchCount = confirmedCount,
            UnresolvedBatchCount = 0,
            RecoveredLastConfirmedSequence = recoveredLast,
            FinalExpectedSequence = execution.FinalExpectedSequence,
            CanContinueSameExecution = true,
            MustRerunTrial = false,
            IsComplete = completeness.IsComplete
        };
    }

    private async Task MarkRecoveryRequiredAsync(
        ValidationAuditExecution execution,
        ValidationParameterTrial? trial,
        string failureCode,
        ValidationAuditRecoveryStatus recoveryStatus,
        CancellationToken cancellationToken)
    {
        await _uow.ExecuteInTransactionAsync(async () =>
        {
            var now = DateTime.UtcNow;
            execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;
            execution.RecoveryStatus = recoveryStatus;
            execution.FailureCode = failureCode;
            execution.UpdatedAtUtc = now;
            execution.RowVersion++;
            await _executions.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

            if (trial is not null)
            {
                trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static List<Guid> ParseGuidArray(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return raw.Select(Guid.Parse).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<string> ParseStringArray(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? []).ToList();
        }
        catch
        {
            return [];
        }
    }
}
