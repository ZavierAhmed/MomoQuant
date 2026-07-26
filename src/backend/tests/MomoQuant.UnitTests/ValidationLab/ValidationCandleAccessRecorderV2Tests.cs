using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E2B WP12 — recorder cursor advancement requires full payload confirmation.
/// The cursor never advances on conflict, partial confirmation, confirmation unavailability,
/// retry exhaustion, or hash mismatch; retries reuse the original AccessEventIds and hashes.
/// </summary>
public sealed class ValidationCandleAccessRecorderV2Tests
{
    [Fact]
    public async Task CursorAdvances_OnlyAfterFullyPayloadConfirmedResult()
    {
        var repo = new ScriptableRepo();
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Advance");

        var result = await recorder.FlushAsync(scope);
        Assert.True(result.IsFullyConfirmed);
        Assert.NotNull(scope.AccessLog[0].PersistedAtUtc);

        // Cursor advanced: nothing pending on the next flush.
        var noop = await recorder.FlushAsync(scope);
        Assert.Empty(noop.RequestedEventIds);
        Assert.Equal(ValidationAccessBatchCommitStatus.NotAttempted, noop.CommitStatus);
        Assert.Equal(1, repo.PersistCalls);
    }

    [Fact]
    public async Task RecorderMapsPayloadHash_AndRecorderVersionIsV2()
    {
        var repo = new ScriptableRepo();
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Hash");

        await recorder.FlushAsync(scope);

        var entity = Assert.Single(repo.LastSubmitted!);
        Assert.Matches("^[0-9A-F]{64}$", entity.AccessPayloadHash);
        Assert.Equal("ValidationAccessPayload/v1", entity.AccessPayloadContractVersion);
        Assert.Equal("ValidationCandleAccess/v2", ValidationCandleAccessRecorder.RecorderVersion);
        Assert.Equal("ValidationCandleAccess/v2", entity.RecorderVersion);
    }

    [Fact]
    public async Task CursorUnchanged_AfterPayloadConflict()
    {
        var repo = new ScriptableRepo
        {
            ThrowOnce = ids => new ValidationAccessPersistedPayloadConflictException(
                ids[0], "REQ", "PERSISTED", new[] { "CallerComponent" }, ConflictResult(ids))
        };
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Conflict");
        var eventId = Assert.Single(scope.AccessLog).AccessEventId;

        await Assert.ThrowsAsync<ValidationAccessPersistedPayloadConflictException>(() => recorder.FlushAsync(scope));
        Assert.Null(scope.AccessLog[0].PersistedAtUtc);

        // Cursor unchanged: the same event is retried with the same id.
        var retry = await recorder.FlushAsync(scope);
        Assert.Contains(eventId, retry.RequestedEventIds);
        Assert.True(retry.IsFullyConfirmed);
    }

    [Fact]
    public async Task CursorUnchanged_AfterConfirmationUnavailable()
    {
        var repo = new ScriptableRepo
        {
            ThrowOnce = ids => new ValidationAccessConfirmationUnavailableException(
                UnavailableResult(ids), new TimeoutException("Simulated outage"))
        };
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Unavailable");
        var eventId = Assert.Single(scope.AccessLog).AccessEventId;

        await Assert.ThrowsAsync<ValidationAccessConfirmationUnavailableException>(() => recorder.FlushAsync(scope));
        Assert.Null(scope.AccessLog[0].PersistedAtUtc);

        var retry = await recorder.FlushAsync(scope);
        Assert.Contains(eventId, retry.RequestedEventIds);
        Assert.True(retry.IsFullyConfirmed);
    }

    [Fact]
    public async Task CursorUnchanged_AfterRetryExhausted()
    {
        var repo = new ScriptableRepo
        {
            ThrowOnce = ids => new ValidationAccessPersistenceRetryExhaustedException(
                3, UnavailableResult(ids), new TimeoutException("Simulated transient"))
        };
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Exhausted");
        var eventId = Assert.Single(scope.AccessLog).AccessEventId;

        await Assert.ThrowsAsync<ValidationAccessPersistenceRetryExhaustedException>(() => recorder.FlushAsync(scope));
        Assert.Null(scope.AccessLog[0].PersistedAtUtc);

        var retry = await recorder.FlushAsync(scope);
        Assert.Contains(eventId, retry.RequestedEventIds);
    }

    [Fact]
    public async Task CursorUnchanged_WhenResultIsNotFullyConfirmed()
    {
        // Repository returns a partial (non-throwing) result: recorder must fail closed itself.
        var repo = new ScriptableRepo { ReturnPartialOnce = true };
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Partial");

        await Assert.ThrowsAsync<ValidationAccessEvidencePersistenceException>(() => recorder.FlushAsync(scope));
        Assert.Null(scope.AccessLog[0].PersistedAtUtc);

        var retry = await recorder.FlushAsync(scope);
        Assert.True(retry.IsFullyConfirmed);
    }

    [Fact]
    public async Task CursorUnchanged_WhenConfirmedHashDoesNotMatchRequestedHash()
    {
        // Result claims full confirmation, but the confirmed hash differs from the requested hash.
        var repo = new ScriptableRepo { CorruptConfirmedHashOnce = true };
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "HashMismatch");

        await Assert.ThrowsAsync<ValidationAccessEvidencePersistenceException>(() => recorder.FlushAsync(scope));
        Assert.Null(scope.AccessLog[0].PersistedAtUtc);
    }

    [Fact]
    public async Task Retry_ReusesOriginalAccessEventIdsAndHashes_MutableMetadataDoesNotChangeHash()
    {
        var repo = new ScriptableRepo
        {
            ThrowOnce = ids => new ValidationAccessConfirmationUnavailableException(
                UnavailableResult(ids), new TimeoutException("Simulated outage"))
        };
        var recorder = new ValidationCandleAccessRecorder(repo);
        var scope = CreateScope();
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Retry");

        await Assert.ThrowsAsync<ValidationAccessConfirmationUnavailableException>(() => recorder.FlushAsync(scope));
        var firstSubmitted = Assert.Single(repo.LastSubmitted!);
        var firstId = firstSubmitted.AccessEventId;
        var firstHash = firstSubmitted.AccessPayloadHash;
        var firstAttempt = firstSubmitted.FlushAttemptCount;

        var retry = await recorder.FlushAsync(scope);
        var secondSubmitted = Assert.Single(repo.LastSubmitted!);

        Assert.Equal(firstId, secondSubmitted.AccessEventId);
        Assert.Equal(firstHash, secondSubmitted.AccessPayloadHash);
        Assert.NotEqual(firstAttempt, secondSubmitted.FlushAttemptCount); // mutable metadata may change
        Assert.True(retry.IsFullyConfirmed);
    }

    private static ValidationAccessBatchPersistResult ConflictResult(IReadOnlyList<Guid> ids) => new()
    {
        RequestedEventIds = ids.ToList(),
        PayloadConflictEventIds = new[] { ids[0] },
        CommitStatus = ValidationAccessBatchCommitStatus.NotAttempted,
        VerificationStatus = ValidationAccessBatchVerificationStatus.PayloadConflict,
        CompletedAtUtc = DateTime.UtcNow
    };

    private static ValidationAccessBatchPersistResult UnavailableResult(IReadOnlyList<Guid> ids) => new()
    {
        RequestedEventIds = ids.ToList(),
        MissingEventIds = ids.ToList(),
        CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
        VerificationStatus = ValidationAccessBatchVerificationStatus.ConfirmationUnavailable,
        CompletedAtUtc = DateTime.UtcNow
    };

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
            }
        };
        return new ValidationTrainingCandleScope(77, start, boundary, candles);
    }

    private sealed class ScriptableRepo : IValidationCandleAccessAuditRepository
    {
        private static readonly ValidationAccessPayloadCanonicalizer Canonicalizer = new();

        public Func<IReadOnlyList<Guid>, Exception>? ThrowOnce { get; set; }
        public bool ReturnPartialOnce { get; set; }
        public bool CorruptConfirmedHashOnce { get; set; }
        public int PersistCalls { get; private set; }
        public IReadOnlyList<ValidationCandleAccessAudit>? LastSubmitted { get; private set; }

        public Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            PersistCalls++;
            LastSubmitted = audits;
            var ids = audits.Select(a => a.AccessEventId).ToList();

            if (ThrowOnce is not null)
            {
                var toThrow = ThrowOnce(ids);
                ThrowOnce = null;
                throw toThrow;
            }

            if (ReturnPartialOnce)
            {
                ReturnPartialOnce = false;
                return Task.FromResult(new ValidationAccessBatchPersistResult
                {
                    RequestedEventIds = ids,
                    ConfirmedMatchingEventIds = Array.Empty<Guid>(),
                    MissingEventIds = ids,
                    CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
                    VerificationStatus = ValidationAccessBatchVerificationStatus.PartiallyPayloadConfirmed,
                    CompletedAtUtc = DateTime.UtcNow
                });
            }

            var hashes = audits.ToDictionary(
                a => a.AccessEventId,
                a => a.AccessPayloadHash ?? Canonicalizer.ComputeSha256(a));

            if (CorruptConfirmedHashOnce)
            {
                CorruptConfirmedHashOnce = false;
                foreach (var key in hashes.Keys.ToList())
                {
                    hashes[key] = new string('0', 64);
                }
            }

            return Task.FromResult(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = ids,
                NewlyInsertedEventIds = ids,
                AttemptedEventIds = ids,
                ConfirmedMatchingEventIds = ids,
                ConfirmedPayloadHashes = hashes,
                CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
                RecoveryStatus = ValidationAccessBatchRecoveryStatus.ConfirmedAfterNormalCommit,
                PersistenceAttemptCount = 1,
                ConfirmationAttemptCount = 1,
                CompletedAtUtc = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(Array.Empty<ValidationCandleAccessAudit>());
    }
}
