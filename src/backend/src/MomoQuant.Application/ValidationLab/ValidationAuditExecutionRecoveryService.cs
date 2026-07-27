using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationAuditExecutionRecoveryService
{
    Task<ValidationAuditExecutionRecoveryResult> RecoverAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Restart recovery for durable audit executions (WP6 rules A–E, E2C1B contiguous cursor).</summary>
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
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.AlreadyCompleted,
                confirmedBatchCount: 0,
                unresolvedBatchCount: 0,
                prefix: new ValidationAuditContiguousSequenceCalculator.ContiguousPrefixResult(
                    execution.LastConfirmedSequence,
                    execution.ConfirmedEventCount,
                    null,
                    false),
                requiresStrategyLab: false,
                isComplete: true);
        }

        if (execution.Status == ValidationAuditExecutionStatus.Superseded)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.FailClosed,
                confirmedBatchCount: 0,
                unresolvedBatchCount: 0,
                prefix: PrefixFromExecution(execution),
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: "VALIDATION_AUDIT_ALREADY_SUPERSEDED",
                mustRerun: true);
        }

        var active = await _executions.GetActiveByTrialIdAsync(execution.ValidationTrialId, cancellationToken)
            .ConfigureAwait(false);
        if (active.Count > 1)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.ConflictDetected,
                confirmedBatchCount: 0,
                unresolvedBatchCount: 0,
                prefix: PrefixFromExecution(execution),
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: "VALIDATION_AUDIT_MULTIPLE_ACTIVE_EXECUTIONS",
                mustRerun: true);
        }

        var batches = (await _batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false)).ToList();
        var allAccess = await _accessAudits.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var scopeRows = allAccess.Where(r => r.ScopeExecutionId == execution.ScopeExecutionId).ToList();

        var trials = await _trials.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var trial = trials.FirstOrDefault(t => t.Id == execution.ValidationTrialId);
        var requiresStrategyLab = TrialRequiresStrategyLabExecution(trial);

        // Rule D — crash before first flush (not a brand-new execution awaiting first access).
        if (batches.Count == 0)
        {
            if (execution.Status == ValidationAuditExecutionStatus.InProgress
                && execution.RecoveryStatus == ValidationAuditRecoveryStatus.None
                && execution.LastConfirmedSequence == 0
                && previousStatus is not ValidationAuditExecutionStatus.RecoveryRequired)
            {
                return BuildResult(
                    execution,
                    previousStatus,
                    ValidationAuditRecoveryDecision.NoRecoveryNeeded,
                    confirmedBatchCount: 0,
                    unresolvedBatchCount: 0,
                    prefix: new ValidationAuditContiguousSequenceCalculator.ContiguousPrefixResult(0, 0, 1, false),
                    requiresStrategyLab: TrialRequiresStrategyLabExecution(trial),
                    isComplete: false,
                    canContinue: false,
                    mustRerun: false);
            }

            await MarkRecoveryRequiredAsync(
                execution,
                trial,
                "PROCESS_INTERRUPTED_BEFORE_FLUSH",
                ValidationAuditRecoveryStatus.RestartRecoveryPending,
                cancellationToken).ConfigureAwait(false);

            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.SupersedeAndRerun,
                confirmedBatchCount: 0,
                unresolvedBatchCount: 0,
                prefix: new ValidationAuditContiguousSequenceCalculator.ContiguousPrefixResult(0, 0, 1, false),
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: "PROCESS_INTERRUPTED_BEFORE_FLUSH",
                mustRerun: true);
        }

        var confirmedCount = 0;
        var unresolvedCount = 0;
        var now = DateTime.UtcNow;

        await _uow.ExecuteInTransactionAsync(async () =>
        {
            foreach (var batch in batches.OrderBy(b => b.BatchNumber))
            {
                if (batch.Status == ValidationAuditBatchStatus.Confirmed)
                {
                    confirmedCount++;
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
                    var seq = batch.FirstSequence + i;
                    var row = scopeRows.FirstOrDefault(r =>
                        r.AccessEventId == ids[i] && r.ScopeSequenceNumber == seq);
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
                    batch.Status = ValidationAuditBatchStatus.Confirmed;
                    batch.ConfirmedAtUtc = now;
                    batch.UpdatedAtUtc = now;
                    batch.ConfirmationAttemptCount++;
                    batch.RowVersion++;
                    await _batches.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);
                    confirmedCount++;
                }
                else
                {
                    unresolvedCount++;
                }
            }

            // Recompute contiguous prefix from confirmed batches — never MAX(LastSequence).
            var prefix = ComputeContiguousPrefixFromBatches(
                batches.Where(b => b.Status == ValidationAuditBatchStatus.Confirmed).ToList(),
                scopeRows);

            execution.LastConfirmedSequence = prefix.LastConfirmedSequence;
            execution.ConfirmedEventCount = prefix.ConfirmedEventCount;
            execution.UpdatedAtUtc = now;
            execution.RowVersion++;

            if (unresolvedCount > 0 && prefix.LastConfirmedSequence == 0)
            {
                execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RestartRecoveryPending;
                execution.FailureCode = "AUDIT_EVENT_PAYLOAD_UNRECOVERABLE";
            }
            else if (prefix.HasGap || unresolvedCount > 0)
            {
                execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RestartRecoveryPending;
                execution.FailureCode = prefix.HasGap
                    ? "AUDIT_SEQUENCE_GAP"
                    : "AUDIT_MANIFEST_INCOMPLETE";
            }
            else if (prefix.LastConfirmedSequence > 0)
            {
                execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
                execution.RecoveryStatus = ValidationAuditRecoveryStatus.RecoveredFromConfirmedBatch;
            }

            await _executions.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

            if (trial is not null && (unresolvedCount > 0 || prefix.HasGap))
            {
                trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        execution = await _executions.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken)
            .ConfigureAwait(false) ?? execution;
        batches = (await _batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false)).ToList();
        scopeRows = (await _accessAudits.GetByExperimentIdAsync(execution.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false))
            .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
            .ToList();

        var contiguousPrefix = ComputeContiguousPrefixFromBatches(
            batches.Where(b => b.Status == ValidationAuditBatchStatus.Confirmed).ToList(),
            scopeRows);

        if (trial is null)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.FailClosed,
                confirmedCount,
                unresolvedCount,
                contiguousPrefix,
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: "VALIDATION_AUDIT_TRIAL_MISSING",
                mustRerun: true);
        }

        var completeness = _verifier.Verify(trial, execution, batches, scopeRows);
        if (completeness.IsComplete)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.AlreadyCompleted,
                confirmedCount,
                unresolvedCount,
                contiguousPrefix,
                requiresStrategyLab: false,
                isComplete: true);
        }

        if (unresolvedCount > 0 && contiguousPrefix.LastConfirmedSequence == 0)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.SupersedeAndRerun,
                confirmedCount,
                unresolvedCount,
                contiguousPrefix,
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: execution.FailureCode ?? "AUDIT_EVENT_PAYLOAD_UNRECOVERABLE",
                mustRerun: true);
        }

        if (contiguousPrefix.HasGap)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.SupersedeAndRerun,
                confirmedCount,
                unresolvedCount,
                contiguousPrefix,
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: "AUDIT_SEQUENCE_GAP",
                mustRerun: true);
        }

        if (unresolvedCount > 0)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.SupersedeAndRerun,
                confirmedCount,
                unresolvedCount,
                contiguousPrefix,
                requiresStrategyLab: true,
                isComplete: false,
                failureCode: execution.FailureCode ?? "AUDIT_MANIFEST_INCOMPLETE",
                mustRerun: true);
        }

        // Contiguous evidence recovered — decide FinalizationOnly vs Supersede based on StrategyLab need.
        if (!requiresStrategyLab
            && contiguousPrefix.LastConfirmedSequence > 0
            && execution.FinalExpectedSequence is null)
        {
            return BuildResult(
                execution,
                previousStatus,
                ValidationAuditRecoveryDecision.FinalizationOnlyRecovery,
                confirmedCount,
                unresolvedCount,
                contiguousPrefix,
                requiresStrategyLab: false,
                isComplete: false,
                failureCode: "FINAL_SEQUENCE_NOT_DECLARED",
                canContinue: true,
                mustRerun: false);
        }

        // Strategy work must execute again under a new superseding execution.
        return BuildResult(
            execution,
            previousStatus,
            ValidationAuditRecoveryDecision.SupersedeAndRerun,
            confirmedCount,
            unresolvedCount,
            contiguousPrefix,
            requiresStrategyLab: true,
            isComplete: false,
            failureCode: requiresStrategyLab
                ? "STRATEGY_LAB_RERUN_REQUIRED"
                : "PREVIOUS_EXECUTION_NOT_TERMINAL",
            mustRerun: true);
    }

    private static ValidationAuditContiguousSequenceCalculator.ContiguousPrefixResult ComputeContiguousPrefixFromBatches(
        IReadOnlyList<ValidationAuditBatch> confirmedBatches,
        IReadOnlyList<ValidationCandleAccessAudit> scopeRows)
    {
        var confirmedSequences = new HashSet<long>();
        foreach (var batch in confirmedBatches)
        {
            var ids = ParseGuidArray(batch.ExpectedEventIdsJson);
            var hashes = ParseStringArray(batch.ExpectedPayloadHashesJson);
            for (var i = 0; i < ids.Count; i++)
            {
                var seq = batch.FirstSequence + i;
                var row = scopeRows.FirstOrDefault(r =>
                    r.AccessEventId == ids[i] && r.ScopeSequenceNumber == seq);
                if (row is not null
                    && !string.IsNullOrWhiteSpace(row.AccessPayloadHash)
                    && string.Equals(row.AccessPayloadHash, hashes[i], StringComparison.OrdinalIgnoreCase))
                {
                    confirmedSequences.Add(seq);
                }
            }
        }

        return ValidationAuditContiguousSequenceCalculator.ComputeFromConfirmedSequences(confirmedSequences);
    }

    private static bool TrialRequiresStrategyLabExecution(ValidationParameterTrial? trial)
    {
        if (trial is null)
        {
            return true;
        }

        if (trial.StrategyLabRunId is null)
        {
            return true;
        }

        return string.Equals(trial.GuardrailDecision, "NotEvaluated", StringComparison.OrdinalIgnoreCase);
    }

    private static ValidationAuditContiguousSequenceCalculator.ContiguousPrefixResult PrefixFromExecution(
        ValidationAuditExecution execution) =>
        new(execution.LastConfirmedSequence, execution.ConfirmedEventCount, null, false);

    private static ValidationAuditExecutionRecoveryResult BuildResult(
        ValidationAuditExecution execution,
        ValidationAuditExecutionStatus previousStatus,
        ValidationAuditRecoveryDecision decision,
        int confirmedBatchCount,
        int unresolvedBatchCount,
        ValidationAuditContiguousSequenceCalculator.ContiguousPrefixResult prefix,
        bool requiresStrategyLab,
        bool isComplete,
        string? failureCode = null,
        bool canContinue = false,
        bool mustRerun = false) =>
        new()
        {
            AuditExecutionId = execution.AuditExecutionId,
            PreviousStatus = previousStatus,
            RecoveryDecision = decision,
            ConfirmedBatchCount = confirmedBatchCount,
            UnresolvedBatchCount = unresolvedBatchCount,
            RecoveredLastConfirmedSequence = prefix.LastConfirmedSequence,
            RecoveredConfirmedEventCount = prefix.ConfirmedEventCount,
            FirstMissingSequence = prefix.FirstMissingSequence,
            FinalExpectedSequence = execution.FinalExpectedSequence,
            CanContinueSameExecution = canContinue && decision == ValidationAuditRecoveryDecision.FinalizationOnlyRecovery,
            MustRerunTrial = mustRerun,
            RequiresStrategyLabExecution = requiresStrategyLab,
            IsComplete = isComplete,
            FailureCode = failureCode
        };

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
