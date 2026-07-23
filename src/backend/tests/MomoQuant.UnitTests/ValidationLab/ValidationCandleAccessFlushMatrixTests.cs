using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0C Part 9 — durable AccessEventId flush matrix (A–G).
/// </summary>
public sealed class ValidationCandleAccessFlushMatrixTests
{
    [Fact]
    public async Task CaseA_NormalFlush_PersistsAndAdvancesCursor()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Normal");

        var written = await recorder.FlushAsync(scope);

        Assert.Equal(1, written);
        Assert.Single(audits.Items);
        Assert.NotEqual(Guid.Empty, audits.Items[0].AccessEventId);
        Assert.Equal(scope.ScopeExecutionId, audits.Items[0].ScopeExecutionId);
        Assert.Equal(ValidationCandleAccessRecorder.RecorderVersion, audits.Items[0].RecorderVersion);
        Assert.NotNull(audits.Items[0].PersistedAtUtc);
        Assert.Equal(1, audits.Items[0].FlushAttemptCount);

        Assert.Equal(0, await recorder.FlushAsync(scope));
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseB_DbFailureThenRetry_CursorUnchangedUntilSuccess()
    {
        var audits = new FakeCandleAccessAuditRepository { FailNextPersistCount = 1 };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Retry");

        var accessEventId = Assert.Single(scope.AccessLog).AccessEventId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.FlushAsync(scope));
        Assert.Empty(audits.Items);

        var written = await recorder.FlushAsync(scope);
        Assert.Equal(1, written);
        Assert.Single(audits.Items);
        Assert.Equal(accessEventId, audits.Items[0].AccessEventId);
        Assert.Equal(2, audits.Items[0].FlushAttemptCount);
    }

    [Fact]
    public async Task CaseC_DuplicateAccessEventId_IsIdempotent()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Dup");
        var eventId = Assert.Single(scope.AccessLog).AccessEventId;

        Assert.Equal(1, await recorder.FlushAsync(scope));

        // Simulate replay of the same AccessEventId already in DB (new recorder / cursor reset).
        var recorder2 = new ValidationCandleAccessRecorder(audits);
        // Force another flush attempt by using a fresh recorder on same scope — cursor is on ConditionalWeakTable
        // keyed by scope, so re-flush is no-op. Instead inject a duplicate via repository path.
        var duplicate = ValidationCandleAccessRecorder.Map(
            scope.AccessLog[0],
            flushAttemptCount: 99,
            persistedAtUtc: DateTime.UtcNow);
        Assert.Equal(eventId, duplicate.AccessEventId);
        Assert.Equal(0, await audits.AddRangeIdempotentByAccessEventIdAsync([duplicate]));
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseD_ConcurrentFlush_SerializesWithoutDuplicateRows()
    {
        var audits = new FakeCandleAccessAuditRepository { ArtificialPersistDelayMs = 50 };
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Concurrent");

        var t1 = recorder.FlushAsync(scope);
        var t2 = recorder.FlushAsync(scope);
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, results.Sum());
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseE_AppendDuringFlush_LeavesNewEventsUncommitted()
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
        // Append while first flush is in-flight.
        _ = scope.GetRange(scope.SegmentStartUtc.AddHours(1), scope.ValidationBoundaryUtc, "Appended");
        await flushTask;

        Assert.Single(audits.Items);
        Assert.Equal(2, scope.AccessLog.Count);

        var second = await recorder.FlushAsync(scope);
        Assert.Equal(1, second);
        Assert.Equal(2, audits.Items.Count);
        Assert.Equal(2, audits.Items.Select(a => a.AccessEventId).Distinct().Count());
    }

    [Fact]
    public async Task CaseF_Cancellation_DoesNotAdvanceCursor()
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
        Assert.Equal(1, written);
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task CaseG_Replay_SameAccessEventIds_RemainIdempotent()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Replay");
        var originalId = Assert.Single(scope.AccessLog).AccessEventId;

        Assert.Equal(1, await recorder.FlushAsync(scope));
        Assert.Equal(0, await recorder.FlushAsync(scope));

        // Replay: re-persist identical AccessEventId payload through repository.
        var replay = ValidationCandleAccessRecorder.Map(
            scope.AccessLog[0],
            flushAttemptCount: 3,
            persistedAtUtc: DateTime.UtcNow);
        Assert.Equal(originalId, replay.AccessEventId);
        Assert.Equal(0, await audits.AddRangeIdempotentByAccessEventIdAsync([replay]));
        Assert.Single(audits.Items);
        Assert.Equal(originalId, audits.Items[0].AccessEventId);
    }

    [Fact]
    public void AccessEventId_GeneratedOnce_AtEventCreation()
    {
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Once");
        var first = Assert.Single(scope.AccessLog).AccessEventId;
        var second = Assert.Single(scope.AccessLog).AccessEventId;
        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first);
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

        public async Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            await AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);

        public async Task<int> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            if (BeforePersistAsync is not null)
            {
                await BeforePersistAsync();
            }

            if (ArtificialPersistDelayMs > 0)
            {
                await Task.Delay(ArtificialPersistDelayMs, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (FailNextPersistCount > 0)
            {
                FailNextPersistCount--;
                throw new InvalidOperationException("Simulated DB failure.");
            }

            var existing = Items.Select(i => i.AccessEventId).ToHashSet();
            var fresh = audits.Where(a => !existing.Contains(a.AccessEventId)).ToList();
            Items.AddRange(fresh);
            return fresh.Count;
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(
                Items.Where(a => a.ValidationExperimentId == experimentId).ToList());
    }
}
