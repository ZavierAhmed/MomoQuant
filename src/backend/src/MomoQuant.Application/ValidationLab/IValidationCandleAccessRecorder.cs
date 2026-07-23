using System.Runtime.CompilerServices;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Automatically maps in-memory <see cref="ValidationCandleAccessRecord"/> entries collected by an
/// <see cref="IValidationTrainingCandleScope"/> into persisted <see cref="ValidationCandleAccessAudit"/> rows.
/// Tracks how much of a scope's access log has already been flushed (per scope instance) so repeated
/// calls for the same scope are idempotent — only newly appended records are persisted.
/// </summary>
public interface IValidationCandleAccessRecorder
{
    /// <summary>
    /// Persists any access-log entries appended to <paramref name="scope"/> since the last flush.
    /// Safe to call multiple times (including concurrently-sequenced calls from catch/finally blocks)
    /// for the same scope instance; already-flushed entries are never re-persisted.
    /// </summary>
    /// <returns>The number of newly persisted audit rows.</returns>
    Task<int> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationCandleAccessRecorder : IValidationCandleAccessRecorder
{
    private static readonly ConditionalWeakTable<IValidationTrainingCandleScope, FlushCursor> FlushCursors = new();

    private readonly IValidationCandleAccessAuditRepository _audits;

    public ValidationCandleAccessRecorder(IValidationCandleAccessAuditRepository audits) => _audits = audits;

    public async Task<int> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default)
    {
        var cursor = FlushCursors.GetOrCreateValue(scope);
        List<ValidationCandleAccessRecord> fresh;
        lock (cursor)
        {
            var log = scope.AccessLog;
            if (log.Count <= cursor.FlushedCount)
            {
                return 0;
            }

            fresh = log.Skip(cursor.FlushedCount).ToList();
            cursor.FlushedCount = log.Count;
        }

        var entities = fresh.Select(Map).ToList();
        await _audits.AddRangeAsync(entities, cancellationToken);
        return entities.Count;
    }

    internal static ValidationCandleAccessAudit Map(ValidationCandleAccessRecord a) => new()
    {
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
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed class FlushCursor
    {
        public int FlushedCount;
    }
}
