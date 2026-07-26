using System.Runtime.CompilerServices;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Automatically maps in-memory <see cref="ValidationCandleAccessRecord"/> entries collected by an
/// <see cref="IValidationTrainingCandleScope"/> into persisted <see cref="ValidationCandleAccessAudit"/> rows.
/// Advances <c>LastConfirmedSequence</c> only after contiguous confirmed durable persist.
/// When a durable audit execution is bound (ambient or trial authoritative), flushes through
/// immutable batch manifests before the E2B event repository (Milestone 23.0E2C1 WP5).
/// </summary>
public interface IValidationCandleAccessRecorder
{
    /// <summary>
    /// Persists access-log entries with <see cref="ValidationCandleAccessRecord.ScopeSequenceNumber"/>
    /// greater than the last confirmed sequence. Requires full confirmation of the snapshotted batch.
    /// On persist failure the confirmed sequence is left unchanged and the exception propagates.
    /// </summary>
    Task<ValidationAccessBatchPersistResult> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationCandleAccessRecorder : IValidationCandleAccessRecorder
{
    public const string RecorderVersion = "ValidationCandleAccess/v2";

    private static readonly ConditionalWeakTable<IValidationTrainingCandleScope, FlushState> FlushStates = new();

    private readonly IValidationCandleAccessAuditRepository _audits;
    private readonly IValidationAccessPayloadCanonicalizer _canonicalizer;
    private readonly IValidationAuditExecutionRepository? _executions;
    private readonly IValidationAuditBatchRepository? _batches;
    private readonly IValidationAuditUnitOfWork? _uow;
    private readonly IValidationAuditPayloadSetHasher? _hasher;
    private readonly IValidationParameterTrialRepository? _trials;

    /// <summary>Legacy constructor — E2B-only path used by existing unit tests.</summary>
    public ValidationCandleAccessRecorder(
        IValidationCandleAccessAuditRepository audits,
        IValidationAccessPayloadCanonicalizer? canonicalizer = null)
        : this(audits, canonicalizer, null, null, null, null, null)
    {
    }

    public ValidationCandleAccessRecorder(
        IValidationCandleAccessAuditRepository audits,
        IValidationAccessPayloadCanonicalizer? canonicalizer,
        IValidationAuditExecutionRepository? executions,
        IValidationAuditBatchRepository? batches,
        IValidationAuditUnitOfWork? uow,
        IValidationAuditPayloadSetHasher? hasher,
        IValidationParameterTrialRepository? trials = null)
    {
        _audits = audits;
        _canonicalizer = canonicalizer ?? new ValidationAccessPayloadCanonicalizer();
        _executions = executions;
        _batches = batches;
        _uow = uow;
        _hasher = hasher;
        _trials = trials;
    }

    public async Task<ValidationAccessBatchPersistResult> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var state = FlushStates.GetOrCreateValue(scope);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var durable = await TryResolveDurableExecutionAsync(scope, cancellationToken).ConfigureAwait(false);
            if (durable is not null)
            {
                return await FlushWithDurableAuditAsync(scope, state, durable, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await FlushLegacyAsync(scope, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task<ValidationAuditExecution?> TryResolveDurableExecutionAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken)
    {
        if (_executions is null || _batches is null || _uow is null || _hasher is null)
        {
            return null;
        }

        var ambient = ValidationAuditExecutionAmbient.CurrentValue;
        if (ambient is not null)
        {
            return await _executions.GetByAuditExecutionIdAsync(ambient.AuditExecutionId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_trials is null || scope.ActiveTrialId is not long trialId)
        {
            return null;
        }

        // Resolve via experiment trials when ambient is absent but trial has authoritative link.
        var trials = await _trials.GetByExperimentIdAsync(scope.ValidationExperimentId, cancellationToken)
            .ConfigureAwait(false);
        var trial = trials.FirstOrDefault(t => t.Id == trialId);
        if (trial?.AuthoritativeAuditExecutionId is not Guid auditId)
        {
            return null;
        }

        return await _executions.GetByAuditExecutionIdAsync(auditId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ValidationAccessBatchPersistResult> FlushWithDurableAuditAsync(
        IValidationTrainingCandleScope scope,
        FlushState state,
        ValidationAuditExecution execution,
        CancellationToken cancellationToken)
    {
        var ambient = ValidationAuditExecutionAmbient.CurrentValue;

        // Identity checks — fail closed.
        if (execution.ScopeExecutionId != scope.ScopeExecutionId)
        {
            throw new ValidationAuditExecutionIdentityMismatchException(
                "Durable audit ScopeExecutionId does not match the training scope.",
                expectedAuditExecutionId: execution.AuditExecutionId,
                actualAuditExecutionId: execution.AuditExecutionId,
                expectedScopeExecutionId: execution.ScopeExecutionId,
                actualScopeExecutionId: scope.ScopeExecutionId,
                expectedExecutionToken: execution.ExecutionToken,
                actualExecutionToken: ambient?.ExecutionToken);
        }

        if (ambient is not null)
        {
            if (ambient.AuditExecutionId != execution.AuditExecutionId
                || ambient.ScopeExecutionId != execution.ScopeExecutionId
                || !string.Equals(ambient.ExecutionToken, execution.ExecutionToken, StringComparison.Ordinal))
            {
                throw new ValidationAuditExecutionIdentityMismatchException(
                    "Ambient audit context does not match the durable audit execution.",
                    expectedAuditExecutionId: execution.AuditExecutionId,
                    actualAuditExecutionId: ambient.AuditExecutionId,
                    expectedScopeExecutionId: execution.ScopeExecutionId,
                    actualScopeExecutionId: ambient.ScopeExecutionId,
                    expectedExecutionToken: execution.ExecutionToken,
                    actualExecutionToken: ambient.ExecutionToken);
            }
        }

        if (execution.Status is ValidationAuditExecutionStatus.Superseded
            or ValidationAuditExecutionStatus.Completed
            or ValidationAuditExecutionStatus.Failed)
        {
            // Post-completion outer finally flushes are expected no-ops when the access log
            // has nothing beyond the durable cursor. Pending unflushed rows fail closed.
            var pendingAfterTerminal = scope.AccessLog
                .Where(r => r.ScopeSequenceNumber > execution.LastConfirmedSequence)
                .ToList();
            if (pendingAfterTerminal.Count == 0)
            {
                return ValidationAccessBatchPersistResult.EmptyNoWork();
            }

            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_EXECUTION_NOT_ACTIVE",
                $"Audit execution {execution.AuditExecutionId} status {execution.Status} is not flushable.");
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

        if (execution.ValidationExperimentId != scope.ValidationExperimentId)
        {
            throw new ValidationAuditExecutionIdentityMismatchException(
                "Audit execution experiment does not match the training scope.",
                expectedAuditExecutionId: execution.AuditExecutionId,
                actualAuditExecutionId: execution.AuditExecutionId,
                expectedScopeExecutionId: execution.ScopeExecutionId,
                actualScopeExecutionId: scope.ScopeExecutionId);
        }

        // Durable cursor wins over in-memory cache.
        var durableLast = execution.LastConfirmedSequence;
        if (state.LastConfirmedSequence != durableLast)
        {
            state.LastConfirmedSequence = durableLast;
        }

        var log = scope.AccessLog;
        var pending = log
            .Where(r => r.ScopeSequenceNumber > durableLast)
            .OrderBy(r => r.ScopeSequenceNumber)
            .ToList();

        if (pending.Count == 0)
        {
            return ValidationAccessBatchPersistResult.EmptyNoWork();
        }

        state.FlushAttemptCount++;
        var attempt = state.FlushAttemptCount;
        var persistedAt = DateTime.UtcNow;

        foreach (var record in pending)
        {
            record.FlushAttemptCount = attempt;
        }

        var entities = pending.Select(r => Map(r, attempt, persistedAt)).ToList();
        var requestedHashes = new Dictionary<Guid, string>(entities.Count);
        foreach (var entity in entities)
        {
            entity.AccessPayloadHash = _canonicalizer.ComputeSha256(entity);
            entity.AccessPayloadContractVersion = _canonicalizer.ContractVersion;
            requestedHashes[entity.AccessEventId] = entity.AccessPayloadHash;
        }

        var entries = entities
            .Select(e => new ValidationAuditPayloadSetEntry(
                e.ScopeSequenceNumber,
                e.AccessEventId,
                e.AccessPayloadHash!,
                e.AccessPayloadContractVersion!))
            .ToList();

        _hasher!.ValidateContiguousSequences(entries, durableLast + 1);
        var setHash = _hasher.ComputeSetHash(entries);
        var (idsJson, hashesJson) = _hasher.BuildManifestJsons(entries);

        var existingBatches = await _batches!.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false);
        var batchNumber = existingBatches.Count == 0
            ? 1
            : existingBatches.Max(b => b.BatchNumber) + 1;

        // Prefer recovering an unconfirmed batch covering the same set hash.
        var recoverable = existingBatches.FirstOrDefault(b =>
            string.Equals(b.ExpectedPayloadSetHash, setHash, StringComparison.OrdinalIgnoreCase)
            && b.Status is ValidationAuditBatchStatus.Created or ValidationAuditBatchStatus.Persisting);

        var proposed = recoverable ?? new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = batchNumber,
            FirstSequence = entries[0].ScopeSequenceNumber,
            LastSequence = entries[^1].ScopeSequenceNumber,
            ExpectedEventCount = entries.Count,
            ExpectedEventIdsJson = idsJson,
            ExpectedPayloadHashesJson = hashesJson,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Created,
            PersistenceAttemptCount = 0,
            ConfirmationAttemptCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
            RowVersion = 1
        };

        if (recoverable is null)
        {
            proposed.ExpectedEventIdsJson = idsJson;
            proposed.ExpectedPayloadHashesJson = hashesJson;
            proposed.ExpectedPayloadSetHash = setHash;
        }

        // CRITICAL: create/recover manifest BEFORE E2B event persistence.
        var batch = await _batches.GetOrCreateManifestAsync(proposed, cancellationToken).ConfigureAwait(false);

        execution.Status = ValidationAuditExecutionStatus.FlushManifested;
        execution.UpdatedAtUtc = DateTime.UtcNow;
        execution.RowVersion++;
        await _executions!.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

        var expectedSequences = pending.ToDictionary(r => r.AccessEventId, r => r.ScopeSequenceNumber);

        // E2B algorithm unchanged.
        var result = await _audits.AddRangeIdempotentByAccessEventIdAsync(entities, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsFullyConfirmed)
        {
            throw new ValidationAccessEvidencePersistenceException(result);
        }

        var confirmedIds = result.ConfirmedMatchingEventIds.ToHashSet();
        var nextExpected = durableLast + 1;
        var advancedTo = durableLast;
        foreach (var record in pending)
        {
            if (record.ScopeSequenceNumber != nextExpected)
            {
                break;
            }

            if (!confirmedIds.Contains(record.AccessEventId))
            {
                break;
            }

            if (!result.ConfirmedPayloadHashes.TryGetValue(record.AccessEventId, out var confirmedHash)
                || !string.Equals(confirmedHash, requestedHashes[record.AccessEventId], StringComparison.Ordinal))
            {
                break;
            }

            if (expectedSequences[record.AccessEventId] != record.ScopeSequenceNumber)
            {
                break;
            }

            advancedTo = record.ScopeSequenceNumber;
            nextExpected++;
        }

        if (advancedTo < pending[^1].ScopeSequenceNumber)
        {
            throw new ValidationAccessEvidencePersistenceException(result);
        }

        // Confirm batch + advance durable cursor in one transaction.
        await _uow!.ExecuteInTransactionAsync(async () =>
        {
            var now = DateTime.UtcNow;
            batch.Status = ValidationAuditBatchStatus.Confirmed;
            batch.ConfirmedAtUtc = now;
            batch.UpdatedAtUtc = now;
            batch.ConfirmationAttemptCount++;
            batch.RowVersion++;
            await _batches.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);

            var fresh = await _executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
                .ConfigureAwait(false)
                ?? execution;

            if (!fresh.CanAdvanceSequence(advancedTo))
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_SEQUENCE_ADVANCE_DENIED",
                    $"Cannot advance LastConfirmedSequence to {advancedTo} for execution {fresh.AuditExecutionId}.");
            }

            fresh.LastConfirmedSequence = advancedTo;
            fresh.ConfirmedEventCount = (int)advancedTo;
            fresh.Status = ValidationAuditExecutionStatus.EventsConfirmed;
            fresh.UpdatedAtUtc = now;
            fresh.RowVersion++;
            await _executions.UpdateAsync(fresh, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        // Only after durable update: advance cache + PersistedAtUtc.
        foreach (var record in pending)
        {
            record.PersistedAtUtc = persistedAt;
        }

        state.LastConfirmedSequence = advancedTo;
        return result;
    }

    private async Task<ValidationAccessBatchPersistResult> FlushLegacyAsync(
        IValidationTrainingCandleScope scope,
        FlushState state,
        CancellationToken cancellationToken)
    {
        var log = scope.AccessLog;
        var pending = log
            .Where(r => r.ScopeSequenceNumber > state.LastConfirmedSequence)
            .OrderBy(r => r.ScopeSequenceNumber)
            .ToList();

        if (pending.Count == 0)
        {
            return ValidationAccessBatchPersistResult.EmptyNoWork();
        }

        state.FlushAttemptCount++;
        var attempt = state.FlushAttemptCount;
        var persistedAt = DateTime.UtcNow;

        foreach (var record in pending)
        {
            record.FlushAttemptCount = attempt;
        }

        var entities = pending.Select(r => Map(r, attempt, persistedAt)).ToList();

        var requestedHashes = new Dictionary<Guid, string>(entities.Count);
        foreach (var entity in entities)
        {
            entity.AccessPayloadHash = _canonicalizer.ComputeSha256(entity);
            entity.AccessPayloadContractVersion = _canonicalizer.ContractVersion;
            requestedHashes[entity.AccessEventId] = entity.AccessPayloadHash;
        }

        var expectedSequences = pending.ToDictionary(r => r.AccessEventId, r => r.ScopeSequenceNumber);

        var result = await _audits.AddRangeIdempotentByAccessEventIdAsync(entities, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsFullyConfirmed)
        {
            throw new ValidationAccessEvidencePersistenceException(result);
        }

        var confirmedIds = result.ConfirmedMatchingEventIds.ToHashSet();
        var nextExpected = state.LastConfirmedSequence + 1;
        var advancedTo = state.LastConfirmedSequence;
        foreach (var record in pending)
        {
            if (record.ScopeSequenceNumber != nextExpected)
            {
                break;
            }

            if (!confirmedIds.Contains(record.AccessEventId))
            {
                break;
            }

            if (!result.ConfirmedPayloadHashes.TryGetValue(record.AccessEventId, out var confirmedHash)
                || !string.Equals(confirmedHash, requestedHashes[record.AccessEventId], StringComparison.Ordinal))
            {
                break;
            }

            if (expectedSequences[record.AccessEventId] != record.ScopeSequenceNumber)
            {
                break;
            }

            advancedTo = record.ScopeSequenceNumber;
            nextExpected++;
            record.PersistedAtUtc = persistedAt;
        }

        if (advancedTo < pending[^1].ScopeSequenceNumber)
        {
            throw new ValidationAccessEvidencePersistenceException(result);
        }

        state.LastConfirmedSequence = advancedTo;
        return result;
    }

    internal static ValidationCandleAccessAudit Map(
        ValidationCandleAccessRecord a,
        int flushAttemptCount,
        DateTime persistedAtUtc) => new()
    {
        AccessEventId = a.AccessEventId,
        ScopeExecutionId = a.ScopeExecutionId,
        ScopeSequenceNumber = a.ScopeSequenceNumber,
        ValidationExperimentId = a.ValidationExperimentId,
        TrialId = a.TrialId,
        TrialNumber = a.TrialNumber,
        CallerComponent = a.CallerComponent,
        AccessPurpose = Truncate(a.AccessPurpose.ToString(), 64),
        RequestedStartUtc = a.RequestedStartUtc,
        RequestedEndUtc = a.RequestedEndUtc,
        RequestedCandleCount = a.RequestedCandleCount,
        ReturnedStartUtc = a.ReturnedStartUtc,
        ReturnedEndUtc = a.ReturnedEndUtc,
        ReturnedCandleCount = a.ReturnedCandleCount,
        MinimumReturnedTimestampUtc = a.MinimumReturnedTimestampUtc,
        MaximumReturnedTimestampUtc = a.MaximumReturnedTimestampUtc,
        CandleContentFingerprint = a.CandleContentFingerprint is { Length: > 64 }
            ? a.CandleContentFingerprint[..64]
            : a.CandleContentFingerprint,
        AccessedAtUtc = a.AccessedAtUtc,
        WasDenied = a.WasDenied,
        DenialCode = Truncate(a.DenialCode, 64),
        DenialReason = Truncate(a.DenialReason, 512),
        CorrelationId = Truncate(a.CorrelationId, 64),
        DatasetPartition = Truncate(a.DatasetPartition, 64),
        FlushAttemptCount = flushAttemptCount,
        PersistedAtUtc = persistedAtUtc,
        RecorderVersion = string.IsNullOrWhiteSpace(a.RecorderVersion)
            ? RecorderVersion
            : a.RecorderVersion is { Length: > 64 }
                ? a.RecorderVersion[..64]
                : a.RecorderVersion,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    private sealed class FlushState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public long LastConfirmedSequence;
        public int FlushAttemptCount;
    }
}
