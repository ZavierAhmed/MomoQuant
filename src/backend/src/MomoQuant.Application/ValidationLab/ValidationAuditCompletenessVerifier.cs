using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Authoritative durable audit-completeness result (Milestone 23.0E2C1 WP11).
/// </summary>
public sealed class ValidationAuditCompletenessResult
{
    public Guid? AuditExecutionId { get; init; }
    public bool IsAuthoritative { get; init; }
    public bool IsTerminal { get; init; }
    public bool IsComplete { get; init; }
    public long? FinalExpectedSequence { get; init; }
    public long LastConfirmedSequence { get; init; }
    public int? ExpectedEventCount { get; init; }
    public int ConfirmedEventCount { get; init; }
    public IReadOnlyList<long> MissingSequences { get; init; } = [];
    public IReadOnlyList<long> DuplicateSequences { get; init; } = [];
    public IReadOnlyList<Guid> OverlappingBatchIds { get; init; } = [];
    public IReadOnlyList<Guid> MissingEventIds { get; init; } = [];
    public IReadOnlyList<Guid> PayloadConflictEventIds { get; init; } = [];
    public IReadOnlyList<Guid> NonConfirmedBatchIds { get; init; } = [];
    public bool ScopeIdentityValid { get; init; } = true;
    public ValidationAuditCompletenessCode CompletionCode { get; init; }

    /// <summary>
    /// True when all durable evidence checks pass, regardless of whether
    /// <see cref="ValidationAuditExecution.Status"/> is already Completed.
    /// <see cref="IsComplete"/> additionally requires Status == Completed.
    /// </summary>
    public bool EvidenceSatisfied { get; init; }

    public static ValidationAuditCompletenessResult HistoricalNotEvaluated() => new()
    {
        IsAuthoritative = false,
        IsTerminal = false,
        IsComplete = false,
        ScopeIdentityValid = true,
        CompletionCode = ValidationAuditCompletenessCode.HistoricalNotEvaluated
    };

    public static ValidationAuditCompletenessResult ExecutionMissing() => new()
    {
        IsAuthoritative = false,
        IsTerminal = false,
        IsComplete = false,
        ScopeIdentityValid = false,
        CompletionCode = ValidationAuditCompletenessCode.ExecutionMissing
    };
}

public interface IValidationAuditCompletenessVerifier
{
    /// <summary>
    /// Verifies completeness for a single audit execution against its batches and
    /// access-audit rows filtered by that execution's <see cref="ValidationAuditExecution.ScopeExecutionId"/> only.
    /// Never merges multiple ScopeExecutionIds.
    /// </summary>
    ValidationAuditCompletenessResult Verify(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        IReadOnlyList<ValidationAuditBatch> batches,
        IReadOnlyList<ValidationCandleAccessAudit> accessRowsForScope);
}

public sealed class ValidationAuditCompletenessVerifier : IValidationAuditCompletenessVerifier
{
    private readonly IValidationAuditPayloadSetHasher _hasher;

    public ValidationAuditCompletenessVerifier(IValidationAuditPayloadSetHasher? hasher = null)
    {
        _hasher = hasher ?? new ValidationAuditPayloadSetHasher();
    }

    public ValidationAuditCompletenessResult Verify(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        IReadOnlyList<ValidationAuditBatch> batches,
        IReadOnlyList<ValidationCandleAccessAudit> accessRowsForScope)
    {
        ArgumentNullException.ThrowIfNull(trial);
        batches ??= Array.Empty<ValidationAuditBatch>();
        accessRowsForScope ??= Array.Empty<ValidationCandleAccessAudit>();

        if (trial.AuthoritativeAuditExecutionId is null)
        {
            return ValidationAuditCompletenessResult.HistoricalNotEvaluated();
        }

        if (execution is null)
        {
            return ValidationAuditCompletenessResult.ExecutionMissing();
        }

        var isAuthoritative = trial.AuthoritativeAuditExecutionId == execution.AuditExecutionId;
        if (!isAuthoritative)
        {
            return new ValidationAuditCompletenessResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                IsAuthoritative = false,
                IsTerminal = IsTerminalStatus(execution.Status),
                IsComplete = false,
                FinalExpectedSequence = execution.FinalExpectedSequence,
                LastConfirmedSequence = execution.LastConfirmedSequence,
                ExpectedEventCount = execution.ExpectedEventCount,
                ConfirmedEventCount = execution.ConfirmedEventCount,
                ScopeIdentityValid = true,
                CompletionCode = ValidationAuditCompletenessCode.NotAuthoritative
            };
        }

        // Never merge foreign scopes — caller must already filter; re-enforce here.
        var scopedRows = accessRowsForScope
            .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
            .ToList();

        if (accessRowsForScope.Any(r => r.ScopeExecutionId != execution.ScopeExecutionId))
        {
            return Build(
                execution,
                isAuthoritative: true,
                code: ValidationAuditCompletenessCode.ScopeIdentityMismatch,
                scopeIdentityValid: false);
        }

        var activeStatuses = new[]
        {
            ValidationAuditExecutionStatus.Created,
            ValidationAuditExecutionStatus.InProgress,
            ValidationAuditExecutionStatus.FlushManifested,
            ValidationAuditExecutionStatus.EventsConfirmed,
            ValidationAuditExecutionStatus.RecoveryRequired
        };

        // Detect multiple non-terminal executions for the same trial via attempt linkage is
        // enforced at repository create time; verifier flags RecoveryRequired / Failed specially.
        if (execution.Status == ValidationAuditExecutionStatus.Superseded)
        {
            return Build(execution, true, ValidationAuditCompletenessCode.Superseded);
        }

        if (execution.Status == ValidationAuditExecutionStatus.RecoveryRequired)
        {
            return Build(execution, true, ValidationAuditCompletenessCode.RecoveryRequired);
        }

        if (execution.Status == ValidationAuditExecutionStatus.Failed)
        {
            return Build(execution, true, ValidationAuditCompletenessCode.RecoveryRequired);
        }

        var executionBatches = batches
            .Where(b => b.AuditExecutionId == execution.AuditExecutionId)
            .OrderBy(b => b.BatchNumber)
            .ToList();

        var overlapping = FindOverlappingBatches(executionBatches);
        if (overlapping.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.BatchOverlap,
                overlappingBatchIds: overlapping);
        }

        var nonConfirmed = executionBatches
            .Where(b => b.Status != ValidationAuditBatchStatus.Confirmed)
            .Select(b => b.AuditBatchId)
            .ToList();

        var hasUnresolvedBatch = nonConfirmed.Count > 0;
        var precondition = execution.ValidateCompletionPreconditions(hasUnresolvedBatch);

        if (execution.Status == ValidationAuditExecutionStatus.Completed
            && precondition == ValidationAuditCompletenessCode.Complete)
        {
            // Still verify evidence matches for Completed rows.
        }
        else if (precondition == ValidationAuditCompletenessCode.FinalSequenceMissing)
        {
            if (activeStatuses.Contains(execution.Status)
                && execution.FinalExpectedSequence is null)
            {
                return Build(
                    execution,
                    true,
                    ValidationAuditCompletenessCode.ExecutionInProgress,
                    nonConfirmedBatchIds: nonConfirmed);
            }

            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.FinalSequenceMissing,
                nonConfirmedBatchIds: nonConfirmed);
        }
        else if (precondition != ValidationAuditCompletenessCode.Complete
                 && precondition != ValidationAuditCompletenessCode.SequenceGap
                 && precondition != ValidationAuditCompletenessCode.DuplicateSequence
                 && precondition != ValidationAuditCompletenessCode.EventMissing
                 && precondition != ValidationAuditCompletenessCode.PayloadMismatch
                 && precondition != ValidationAuditCompletenessCode.ManifestMissing)
        {
            return Build(execution, true, precondition, nonConfirmedBatchIds: nonConfirmed);
        }

        if (hasUnresolvedBatch && execution.FinalExpectedSequence is not null)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.ManifestMissing,
                nonConfirmedBatchIds: nonConfirmed);
        }

        if (execution.FinalExpectedSequence is null)
        {
            return Build(
                execution,
                true,
                activeStatuses.Contains(execution.Status)
                    ? ValidationAuditCompletenessCode.ExecutionInProgress
                    : ValidationAuditCompletenessCode.FinalSequenceMissing,
                nonConfirmedBatchIds: nonConfirmed);
        }

        var finalExpected = execution.FinalExpectedSequence.Value;
        if (finalExpected == 0)
        {
            if (!execution.AllowsZeroAccess)
            {
                return Build(execution, true, ValidationAuditCompletenessCode.FinalSequenceMissing);
            }

            if (executionBatches.Count == 0 && scopedRows.Count == 0
                && execution.LastConfirmedSequence == 0
                && (execution.ConfirmedEventCount == 0)
                && (execution.ExpectedEventCount is null or 0))
            {
                // Evidence is valid for zero-access, but Complete only when Status is Completed.
                return RequireCompletedStatusForComplete(execution);
            }

            return Build(execution, true, ValidationAuditCompletenessCode.EventMissing);
        }

        // Sequences 1..FinalExpected must appear exactly once across confirmed manifests.
        var confirmedBatches = executionBatches
            .Where(b => b.Status == ValidationAuditBatchStatus.Confirmed)
            .ToList();

        var sequenceOwners = new Dictionary<long, Guid>();
        var duplicateSequences = new List<long>();
        var manifestEventIds = new List<(Guid EventId, long Sequence, string Hash)>();

        foreach (var batch in confirmedBatches)
        {
            var ids = ParseGuidArray(batch.ExpectedEventIdsJson);
            var hashes = ParseStringArray(batch.ExpectedPayloadHashesJson);
            if (ids.Count != batch.ExpectedEventCount
                || hashes.Count != batch.ExpectedEventCount
                || ids.Count != hashes.Count)
            {
                return Build(
                    execution,
                    true,
                    ValidationAuditCompletenessCode.ManifestMissing,
                    nonConfirmedBatchIds: nonConfirmed);
            }

            for (var i = 0; i < ids.Count; i++)
            {
                var seq = batch.FirstSequence + i;
                if (seq < batch.FirstSequence || seq > batch.LastSequence)
                {
                    return Build(execution, true, ValidationAuditCompletenessCode.SequenceGap);
                }

                if (sequenceOwners.ContainsKey(seq))
                {
                    duplicateSequences.Add(seq);
                }
                else
                {
                    sequenceOwners[seq] = batch.AuditBatchId;
                }

                manifestEventIds.Add((ids[i], seq, hashes[i].ToUpperInvariant()));
            }
        }

        if (duplicateSequences.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.DuplicateSequence,
                duplicateSequences: duplicateSequences.Distinct().OrderBy(x => x).ToList());
        }

        var missingSequences = new List<long>();
        for (long seq = 1; seq <= finalExpected; seq++)
        {
            if (!sequenceOwners.ContainsKey(seq))
            {
                missingSequences.Add(seq);
            }
        }

        var excessSequences = sequenceOwners.Keys.Where(s => s < 1 || s > finalExpected).ToList();
        if (excessSequences.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.DuplicateSequence,
                duplicateSequences: excessSequences);
        }

        if (missingSequences.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.SequenceGap,
                missingSequences: missingSequences);
        }

        var rowsByEventId = scopedRows
            .GroupBy(r => r.AccessEventId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var missingEventIds = new List<Guid>();
        var payloadConflicts = new List<Guid>();

        foreach (var (eventId, sequence, expectedHash) in manifestEventIds)
        {
            if (!rowsByEventId.TryGetValue(eventId, out var rows) || rows.Count == 0)
            {
                missingEventIds.Add(eventId);
                continue;
            }

            if (rows.Count > 1)
            {
                // Multiple rows for same AccessEventId under one scope — fail closed.
                payloadConflicts.Add(eventId);
                continue;
            }

            var row = rows[0];
            if (row.ScopeSequenceNumber != sequence)
            {
                payloadConflicts.Add(eventId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.AccessPayloadHash)
                || !string.Equals(row.AccessPayloadHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                payloadConflicts.Add(eventId);
            }
        }

        if (missingEventIds.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.EventMissing,
                missingEventIds: missingEventIds);
        }

        if (payloadConflicts.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.PayloadMismatch,
                payloadConflictEventIds: payloadConflicts);
        }

        // Final payload-set hash when declared must match recomputed set.
        if (!string.IsNullOrWhiteSpace(execution.FinalPayloadSetHash))
        {
            var entries = manifestEventIds
                .OrderBy(x => x.Sequence)
                .ThenBy(x => x.EventId)
                .Select(x =>
                {
                    var row = rowsByEventId[x.EventId][0];
                    return new ValidationAuditPayloadSetEntry(
                        x.Sequence,
                        x.EventId,
                        x.Hash,
                        row.AccessPayloadContractVersion
                        ?? ValidationAccessPayloadContractVersions.Current);
                })
                .ToList();

            var computed = _hasher.ComputeSetHash(entries);
            if (!string.Equals(computed, execution.FinalPayloadSetHash, StringComparison.OrdinalIgnoreCase))
            {
                return Build(execution, true, ValidationAuditCompletenessCode.PayloadMismatch);
            }
        }

        if (execution.LastConfirmedSequence != finalExpected
            || (execution.ExpectedEventCount is int expected
                && execution.ConfirmedEventCount != expected))
        {
            return Build(
                execution,
                true,
                execution.LastConfirmedSequence < finalExpected
                    ? ValidationAuditCompletenessCode.SequenceGap
                    : ValidationAuditCompletenessCode.DuplicateSequence,
                missingSequences: missingSequences);
        }

        if (nonConfirmed.Count > 0)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.ManifestMissing,
                nonConfirmedBatchIds: nonConfirmed);
        }

        // Evidence checks passed — Complete only when execution Status is Completed.
        return RequireCompletedStatusForComplete(execution, evidenceSatisfied: true);
    }

    /// <summary>
    /// Evidence may be fully valid while Status is still EventsConfirmed/InProgress/etc.
    /// IsComplete/Complete require Status == Completed.
    /// </summary>
    private static ValidationAuditCompletenessResult RequireCompletedStatusForComplete(
        ValidationAuditExecution execution,
        bool evidenceSatisfied = true)
    {
        if (execution.Status == ValidationAuditExecutionStatus.Completed && evidenceSatisfied)
        {
            return Build(
                execution,
                true,
                ValidationAuditCompletenessCode.Complete,
                isComplete: true,
                evidenceSatisfied: true);
        }

        var code = execution.FinalExpectedSequence is null
            ? ValidationAuditCompletenessCode.FinalSequenceMissing
            : ValidationAuditCompletenessCode.ExecutionInProgress;

        return Build(execution, true, code, isComplete: false, evidenceSatisfied: evidenceSatisfied);
    }

    private static ValidationAuditCompletenessResult Build(
        ValidationAuditExecution execution,
        bool isAuthoritative,
        ValidationAuditCompletenessCode code,
        bool isComplete = false,
        bool scopeIdentityValid = true,
        bool evidenceSatisfied = false,
        IReadOnlyList<long>? missingSequences = null,
        IReadOnlyList<long>? duplicateSequences = null,
        IReadOnlyList<Guid>? overlappingBatchIds = null,
        IReadOnlyList<Guid>? missingEventIds = null,
        IReadOnlyList<Guid>? payloadConflictEventIds = null,
        IReadOnlyList<Guid>? nonConfirmedBatchIds = null)
    {
        // IsComplete is true ONLY when evidence is valid AND Status == Completed.
        var complete = isComplete
                       && code == ValidationAuditCompletenessCode.Complete
                       && execution.Status == ValidationAuditExecutionStatus.Completed;

        return new ValidationAuditCompletenessResult
        {
            AuditExecutionId = execution.AuditExecutionId,
            IsAuthoritative = isAuthoritative,
            IsTerminal = IsTerminalStatus(execution.Status) || complete,
            IsComplete = complete,
            EvidenceSatisfied = evidenceSatisfied || complete,
            FinalExpectedSequence = execution.FinalExpectedSequence,
            LastConfirmedSequence = execution.LastConfirmedSequence,
            ExpectedEventCount = execution.ExpectedEventCount,
            ConfirmedEventCount = execution.ConfirmedEventCount,
            MissingSequences = missingSequences ?? [],
            DuplicateSequences = duplicateSequences ?? [],
            OverlappingBatchIds = overlappingBatchIds ?? [],
            MissingEventIds = missingEventIds ?? [],
            PayloadConflictEventIds = payloadConflictEventIds ?? [],
            NonConfirmedBatchIds = nonConfirmedBatchIds ?? [],
            ScopeIdentityValid = scopeIdentityValid,
            CompletionCode = complete ? ValidationAuditCompletenessCode.Complete : code
        };
    }

    private static bool IsTerminalStatus(ValidationAuditExecutionStatus status) =>
        status is ValidationAuditExecutionStatus.Completed
            or ValidationAuditExecutionStatus.Superseded
            or ValidationAuditExecutionStatus.Failed;

    private static List<Guid> FindOverlappingBatches(IReadOnlyList<ValidationAuditBatch> batches)
    {
        var overlapping = new HashSet<Guid>();
        for (var i = 0; i < batches.Count; i++)
        {
            for (var j = i + 1; j < batches.Count; j++)
            {
                var a = batches[i];
                var b = batches[j];
                if (a.FirstSequence <= b.LastSequence && b.FirstSequence <= a.LastSequence)
                {
                    overlapping.Add(a.AuditBatchId);
                    overlapping.Add(b.AuditBatchId);
                }
            }
        }

        return overlapping.OrderBy(x => x).ToList();
    }

    private static List<Guid> ParseGuidArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        try
        {
            var raw = System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
            return raw.Select(s => Guid.Parse(s)).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<string> ParseStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        try
        {
            return (System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []).ToList();
        }
        catch
        {
            return [];
        }
    }
}
