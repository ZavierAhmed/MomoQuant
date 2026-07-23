using System.Runtime.CompilerServices;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Automatically maps in-memory <see cref="ValidationCandleAccessRecord"/> entries collected by an
/// <see cref="IValidationTrainingCandleScope"/> into persisted <see cref="ValidationCandleAccessAudit"/> rows.
/// Tracks a per-scope committed cursor that advances only after a successful durable persist.
/// </summary>
public interface IValidationCandleAccessRecorder
{
    /// <summary>
    /// Persists any access-log entries appended to <paramref name="scope"/> since the last successful commit.
    /// Safe to call multiple times (including concurrently) for the same scope instance.
    /// Duplicate <see cref="ValidationCandleAccessRecord.AccessEventId"/> values are treated as already persisted.
    /// On persist failure the committed cursor is left unchanged and the exception propagates.
    /// </summary>
    /// <returns>The number of newly persisted audit rows (duplicates count as already persisted / zero new).</returns>
    Task<int> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationCandleAccessRecorder : IValidationCandleAccessRecorder
{
    public const string RecorderVersion = "ValidationCandleAccess/v1";

    private static readonly ConditionalWeakTable<IValidationTrainingCandleScope, FlushState> FlushStates = new();

    private readonly IValidationCandleAccessAuditRepository _audits;

    public ValidationCandleAccessRecorder(IValidationCandleAccessAuditRepository audits) => _audits = audits;

    public async Task<int> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var state = FlushStates.GetOrCreateValue(scope);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Snapshot after the committed cursor — appends during persist stay uncommitted.
            var log = scope.AccessLog;
            if (log.Count <= state.CommittedCount)
            {
                return 0;
            }

            var fresh = log.Skip(state.CommittedCount).ToList();
            state.FlushAttemptCount++;
            var attempt = state.FlushAttemptCount;
            var persistedAt = DateTime.UtcNow;

            foreach (var record in fresh)
            {
                record.FlushAttemptCount = attempt;
            }

            var entities = fresh.Select(r => Map(r, attempt, persistedAt)).ToList();

            // Persist transactionally / idempotently. On failure: cursor unchanged, exception propagates.
            var written = await _audits.AddRangeIdempotentByAccessEventIdAsync(entities, cancellationToken)
                .ConfigureAwait(false);

            // ONLY after successful commit — advance by the snapshotted slice length.
            state.CommittedCount += fresh.Count;

            foreach (var record in fresh)
            {
                record.PersistedAtUtc = persistedAt;
            }

            return written;
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
        ValidationExperimentId = a.ValidationExperimentId,
        TrialId = a.TrialId,
        TrialNumber = a.TrialNumber,
        CallerComponent = a.CallerComponent,
        RequestedStartUtc = a.RequestedStartUtc,
        RequestedEndUtc = a.RequestedEndUtc,
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
        DenialReason = a.DenialReason is { Length: > 512 } ? a.DenialReason[..512] : a.DenialReason,
        FlushAttemptCount = flushAttemptCount,
        PersistedAtUtc = persistedAtUtc,
        RecorderVersion = string.IsNullOrWhiteSpace(a.RecorderVersion)
            ? RecorderVersion
            : a.RecorderVersion is { Length: > 64 }
                ? a.RecorderVersion[..64]
                : a.RecorderVersion,
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed class FlushState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int CommittedCount;
        public int FlushAttemptCount;
    }
}
