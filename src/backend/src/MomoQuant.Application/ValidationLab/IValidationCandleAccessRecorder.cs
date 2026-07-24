using System.Runtime.CompilerServices;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Automatically maps in-memory <see cref="ValidationCandleAccessRecord"/> entries collected by an
/// <see cref="IValidationTrainingCandleScope"/> into persisted <see cref="ValidationCandleAccessAudit"/> rows.
/// Advances <c>LastConfirmedSequence</c> only after contiguous confirmed durable persist.
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
    public const string RecorderVersion = "ValidationCandleAccess/v1";

    private static readonly ConditionalWeakTable<IValidationTrainingCandleScope, FlushState> FlushStates = new();

    private readonly IValidationCandleAccessAuditRepository _audits;

    public ValidationCandleAccessRecorder(IValidationCandleAccessAuditRepository audits) => _audits = audits;

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

            // Persist + confirm. On failure: LastConfirmedSequence unchanged, exception propagates.
            var result = await _audits.AddRangeIdempotentByAccessEventIdAsync(entities, cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsFullyConfirmed)
            {
                throw new ValidationAccessEvidencePersistenceException(result);
            }

            // Advance only through contiguous confirmed sequences starting at LastConfirmedSequence + 1.
            var confirmedIds = result.ConfirmedPersistedEventIds.ToHashSet();
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

                advancedTo = record.ScopeSequenceNumber;
                nextExpected++;
                record.PersistedAtUtc = persistedAt;
            }

            if (advancedTo < pending[^1].ScopeSequenceNumber)
            {
                // Contiguous prefix incomplete despite set-equality — treat as persistence failure.
                throw new ValidationAccessEvidencePersistenceException(result);
            }

            state.LastConfirmedSequence = advancedTo;
            return result;
        }
        finally
        {
            state.Gate.Release();
        }
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
