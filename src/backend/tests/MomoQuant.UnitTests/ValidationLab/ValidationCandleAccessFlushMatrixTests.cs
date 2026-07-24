using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0D WP11–15 — durable access audit flush / persist matrix (A–K).
/// </summary>
public sealed class ValidationCandleAccessFlushMatrixTests
{
    [Fact]
    public async Task CaseA_AllNew_PersistsAndAdvancesConfirmedSequence()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Normal");

        var result = await recorder.FlushAsync(scope);

        Assert.True(result.IsFullyConfirmed);
        Assert.Equal(1, result.NewlyInsertedCount);
        Assert.Empty(result.AlreadyExistingEventIds);
        Assert.Equal(ValidationAccessBatchCommitStatus.Committed, result.CommitStatus);
        Assert.Equal(ValidationAccessBatchVerificationStatus.FullyConfirmed, result.VerificationStatus);
        Assert.Single(audits.Items);
        Assert.Equal(1, audits.Items[0].ScopeSequenceNumber);
        Assert.NotEqual(Guid.Empty, audits.Items[0].AccessEventId);
        Assert.Equal(scope.ScopeExecutionId, audits.Items[0].ScopeExecutionId);
        Assert.Equal(ValidationCandleAccessRecorder.RecorderVersion, audits.Items[0].RecorderVersion);
        Assert.NotNull(audits.Items[0].PersistedAtUtc);
        Assert.Equal(1, audits.Items[0].FlushAttemptCount);

        var noop = await recorder.FlushAsync(scope);
        Assert.Equal(0, noop.NewlyInsertedCount);
        Assert.Equal(ValidationAccessBatchCommitStatus.NotAttempted, noop.CommitStatus);
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseB_AllExisting_ConfirmsWithoutNewInserts()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Dup");
        var eventId = Assert.Single(scope.AccessLog).AccessEventId;

        Assert.Equal(1, (await recorder.FlushAsync(scope)).NewlyInsertedCount);

        var duplicate = ValidationCandleAccessRecorder.Map(
            scope.AccessLog[0],
            flushAttemptCount: 99,
            persistedAtUtc: DateTime.UtcNow);
        Assert.Equal(eventId, duplicate.AccessEventId);

        var replay = await audits.AddRangeIdempotentByAccessEventIdAsync([duplicate]);
        Assert.True(replay.IsFullyConfirmed);
        Assert.Equal(0, replay.NewlyInsertedCount);
        Assert.Equal(1, replay.AlreadyExistingCount);
        Assert.Contains(eventId, replay.ConfirmedPersistedEventIds);
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseC_MixedBatch_InsertsOnlyFresh_ConfirmsAll()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "A");
        _ = scope.GetRange(scope.SegmentStartUtc.AddHours(1), scope.ValidationBoundaryUtc, "B");

        var first = ValidationCandleAccessRecorder.Map(scope.AccessLog[0], 1, DateTime.UtcNow);
        var second = ValidationCandleAccessRecorder.Map(scope.AccessLog[1], 1, DateTime.UtcNow);

        var seed = await audits.AddRangeIdempotentByAccessEventIdAsync([first]);
        Assert.Equal(1, seed.NewlyInsertedCount);

        var mixed = await audits.AddRangeIdempotentByAccessEventIdAsync([first, second]);
        Assert.True(mixed.IsFullyConfirmed);
        Assert.Equal(1, mixed.NewlyInsertedCount);
        Assert.Equal(1, mixed.AlreadyExistingCount);
        Assert.Equal(2, mixed.ConfirmedCount);
        Assert.Equal(2, audits.Items.Count);
        Assert.Empty(mixed.MissingEventIds);
    }

    [Fact]
    public async Task CaseD_DbFailureThenRetry_SequenceUnchangedUntilSuccess()
    {
        var audits = new FakeCandleAccessAuditRepository { FailNextPersistCount = 1 };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Retry");

        var accessEventId = Assert.Single(scope.AccessLog).AccessEventId;
        var seq = Assert.Single(scope.AccessLog).ScopeSequenceNumber;

        await Assert.ThrowsAsync<ValidationAccessEvidencePersistenceException>(() => recorder.FlushAsync(scope));
        Assert.Empty(audits.Items);

        var result = await recorder.FlushAsync(scope);
        Assert.Equal(1, result.NewlyInsertedCount);
        Assert.Single(audits.Items);
        Assert.Equal(accessEventId, audits.Items[0].AccessEventId);
        Assert.Equal(seq, audits.Items[0].ScopeSequenceNumber);
        Assert.Equal(2, audits.Items[0].FlushAttemptCount);
    }

    [Fact]
    public async Task CaseE_ConcurrentFlush_SerializesWithoutDuplicateRows()
    {
        var audits = new FakeCandleAccessAuditRepository { ArtificialPersistDelayMs = 50 };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Concurrent");

        var t1 = recorder.FlushAsync(scope);
        var t2 = recorder.FlushAsync(scope);
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, results.Sum(r => r.NewlyInsertedCount));
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseF_ConcurrentOverlap_MixedBatchDoesNotLoseFreshRows()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "Existing");
        _ = scope.GetRange(scope.SegmentStartUtc.AddHours(1), scope.ValidationBoundaryUtc, "Fresh");

        var existing = ValidationCandleAccessRecorder.Map(scope.AccessLog[0], 1, DateTime.UtcNow);
        var fresh = ValidationCandleAccessRecorder.Map(scope.AccessLog[1], 1, DateTime.UtcNow);

        // Simulate concurrent writer: existing id already present when mixed upsert runs.
        await audits.AddRangeIdempotentByAccessEventIdAsync([existing]);

        var mixed = await audits.AddRangeIdempotentByAccessEventIdAsync([existing, fresh]);
        Assert.True(mixed.IsFullyConfirmed);
        Assert.Contains(fresh.AccessEventId, mixed.NewlyInsertedEventIds);
        Assert.Contains(existing.AccessEventId, mixed.AlreadyExistingEventIds);
        Assert.Equal(2, audits.Items.Select(i => i.AccessEventId).Distinct().Count());
    }

    [Fact]
    public async Task CaseG_AppendDuringFlush_LeavesNewEventsUnconfirmed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audits = new FakeCandleAccessAuditRepository
        {
            BeforePersistAsync = async () =>
            {
                gate.TrySetResult();
                await Task.Delay(80);
            }
        };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "First");

        var flushTask = recorder.FlushAsync(scope);
        await gate.Task;
        _ = scope.GetRange(scope.SegmentStartUtc.AddHours(1), scope.ValidationBoundaryUtc, "Appended");
        await flushTask;

        Assert.Single(audits.Items);
        Assert.Equal(2, scope.AccessLog.Count);

        var second = await recorder.FlushAsync(scope);
        Assert.Equal(1, second.NewlyInsertedCount);
        Assert.Equal(2, audits.Items.Count);
        Assert.Equal(2, audits.Items.Select(a => a.AccessEventId).Distinct().Count());
        Assert.Equal(new[] { 1L, 2L }, audits.Items.Select(a => a.ScopeSequenceNumber).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task CaseH_Cancellation_DoesNotAdvanceConfirmedSequence()
    {
        using var cts = new CancellationTokenSource();
        var audits = new FakeCandleAccessAuditRepository
        {
            BeforePersistAsync = () =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            }
        };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Cancel");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recorder.FlushAsync(scope, cts.Token));
        Assert.Empty(audits.Items);

        var written = await recorder.FlushAsync(scope, CancellationToken.None);
        Assert.Equal(1, written.NewlyInsertedCount);
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseI_UnknownCommitRetry_ConfirmViaSelectSucceeds()
    {
        var audits = new FakeCandleAccessAuditRepository { SimulateUnknownCommitThenConfirm = true };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Unknown");

        // First call: rows land but commit status reported as Unknown; confirmation SELECT still succeeds.
        var result = await recorder.FlushAsync(scope);
        Assert.True(result.IsFullyConfirmed);
        Assert.Equal(ValidationAccessBatchCommitStatus.Unknown, result.CommitStatus);
        Assert.Single(audits.Items);

        var retry = await recorder.FlushAsync(scope);
        Assert.Equal(ValidationAccessBatchCommitStatus.NotAttempted, retry.CommitStatus);
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseJ_Replay_SameAccessEventIds_RemainIdempotent()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Replay");
        var originalId = Assert.Single(scope.AccessLog).AccessEventId;

        Assert.Equal(1, (await recorder.FlushAsync(scope)).NewlyInsertedCount);
        Assert.Equal(0, (await recorder.FlushAsync(scope)).NewlyInsertedCount);

        var replay = ValidationCandleAccessRecorder.Map(
            scope.AccessLog[0],
            flushAttemptCount: 3,
            persistedAtUtc: DateTime.UtcNow);
        Assert.Equal(originalId, replay.AccessEventId);
        var replayResult = await audits.AddRangeIdempotentByAccessEventIdAsync([replay]);
        Assert.True(replayResult.IsFullyConfirmed);
        Assert.Equal(0, replayResult.NewlyInsertedCount);
        Assert.Single(audits.Items);
        Assert.Equal(originalId, audits.Items[0].AccessEventId);
    }

    [Fact]
    public async Task CaseK_MissingConfirmation_ThrowsAndDoesNotAdvance()
    {
        var audits = new FakeCandleAccessAuditRepository { DropConfirmedOnVerify = true };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Missing");

        var ex = await Assert.ThrowsAsync<ValidationAccessEvidencePersistenceException>(
            () => recorder.FlushAsync(scope));
        Assert.False(ex.PersistResult.IsFullyConfirmed);
        Assert.NotEmpty(ex.PersistResult.MissingEventIds);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, ex.ErrorCode);

        // Cursor not advanced — retry after fixing verify still can persist.
        audits.DropConfirmedOnVerify = false;
        var recovered = await recorder.FlushAsync(scope);
        Assert.True(recovered.IsFullyConfirmed);
        Assert.Single(audits.Items);
    }

    [Fact]
    public void AccessEventId_AndScopeSequence_GeneratedOnce_AtEventCreation()
    {
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Once");
        var first = Assert.Single(scope.AccessLog);
        var second = Assert.Single(scope.AccessLog);
        Assert.Equal(first.AccessEventId, second.AccessEventId);
        Assert.Equal(1, first.ScopeSequenceNumber);
        Assert.NotEqual(Guid.Empty, first.AccessEventId);

        _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "Two");
        Assert.Equal(2, scope.AccessLog.Count);
        Assert.Equal(2, scope.AccessLog[1].ScopeSequenceNumber);
    }

    private static ValidationTrainingCandleScope CreateScope()
    {
        var boundary = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var start = boundary.AddDays(-2);
        var candles = new List<Candle>
        {
            new()
            {
                OpenTimeUtc = start,
                CloseTimeUtc = start.AddHours(1),
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            },
            new()
            {
                OpenTimeUtc = start.AddHours(1),
                CloseTimeUtc = start.AddHours(2),
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            },
            new()
            {
                OpenTimeUtc = boundary.AddHours(-1),
                CloseTimeUtc = boundary,
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            }
        };
        return new ValidationTrainingCandleScope(42, start, boundary, candles);
    }

    private sealed class FakeCandleAccessAuditRepository : IValidationCandleAccessAuditRepository
    {
        public List<ValidationCandleAccessAudit> Items { get; } = [];
        public int FailNextPersistCount { get; set; }
        public int ArtificialPersistDelayMs { get; set; }
        public Func<Task>? BeforePersistAsync { get; set; }
        public bool SimulateUnknownCommitThenConfirm { get; set; }
        public bool DropConfirmedOnVerify { get; set; }

        public async Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            await AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);

        public async Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            if (audits.Count == 0)
            {
                return ValidationAccessBatchPersistResult.EmptyNoWork();
            }

            if (BeforePersistAsync is not null)
            {
                await BeforePersistAsync();
            }

            if (ArtificialPersistDelayMs > 0)
            {
                await Task.Delay(ArtificialPersistDelayMs, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var distinct = audits.GroupBy(a => a.AccessEventId).Select(g => g.First()).ToList();
            var requested = distinct.Select(a => a.AccessEventId).ToList();
            var existingBefore = Items.Select(i => i.AccessEventId).ToHashSet();

            if (FailNextPersistCount > 0)
            {
                FailNextPersistCount--;
                var failed = ValidationAccessBatchPersistResult.Create(
                    requested,
                    Array.Empty<Guid>(),
                    requested.Where(existingBefore.Contains).ToList(),
                    Array.Empty<Guid>(),
                    ValidationAccessBatchCommitStatus.Failed);
                throw new ValidationAccessEvidencePersistenceException(
                    failed,
                    new InvalidOperationException("Simulated DB failure."));
            }

            var newly = new List<Guid>();
            var already = new List<Guid>();
            foreach (var audit in distinct)
            {
                if (existingBefore.Contains(audit.AccessEventId))
                {
                    already.Add(audit.AccessEventId);
                }
                else
                {
                    Items.Add(audit);
                    newly.Add(audit.AccessEventId);
                }
            }

            var confirmed = DropConfirmedOnVerify
                ? Array.Empty<Guid>()
                : requested.ToArray();

            var commit = SimulateUnknownCommitThenConfirm
                ? ValidationAccessBatchCommitStatus.Unknown
                : ValidationAccessBatchCommitStatus.Committed;

            // Only consume the unknown-commit simulation once.
            SimulateUnknownCommitThenConfirm = false;

            var result = ValidationAccessBatchPersistResult.Create(
                requested,
                newly,
                already,
                confirmed,
                commit);

            if (!result.IsFullyConfirmed)
            {
                throw new ValidationAccessEvidencePersistenceException(result);
            }

            return result;
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(
                Items.Where(a => a.ValidationExperimentId == experimentId).ToList());
    }
}
