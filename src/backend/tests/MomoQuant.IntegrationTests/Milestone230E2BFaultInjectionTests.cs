using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.MarketData;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2B WP14/WP16 — MySQL-backed commit-ambiguity, confirmation-failure, cancellation,
/// and bounded-retry fault injection. Every test executes the REAL production repository algorithm
/// against real MySQL; only the narrow transaction-boundary / confirmation-reader seams are scripted.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2BFaultInjectionTests : IClassFixture<E2BSeamFactory>
{
    private readonly E2BSeamFactory _factory;

    public Milestone230E2BFaultInjectionTests(E2BSeamFactory factory)
    {
        _factory = factory;
        _factory.ResetSeams();
    }

    [Fact]
    public async Task NormalCommit_AllPayloadsConfirmed_WithFreshContext_NoPerEventQueries()
    {
        var experimentId = 23_050_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var batch = Enumerable.Range(1, 3)
                .Select(i => E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, i, "Normal"))
                .ToList();

            var result = await audits.AddRangeIdempotentByAccessEventIdAsync(batch);

            Assert.True(result.IsFullyConfirmed);
            Assert.Equal(ValidationAccessBatchCommitStatus.CommitSucceeded, result.CommitStatus);
            Assert.Equal(ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed, result.VerificationStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.ConfirmedAfterNormalCommit, result.RecoveryStatus);
            Assert.True(result.UsedFreshConfirmationContext);
            Assert.Equal(1, result.PersistenceAttemptCount);
            Assert.Equal(3, result.ConfirmedMatchingEventIds.Count);
            Assert.Equal(1, _factory.Boundary.RealCommits);

            // Performance guard: one batched pre-confirmation + one batched post-confirmation. No N+1.
            Assert.Equal(2, _factory.Reader.ReadCalls);

            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(3, rows.Count);
            foreach (var row in rows)
            {
                Assert.Equal(result.ConfirmedPayloadHashes[row.AccessEventId], row.AccessPayloadHash);
                Assert.Equal("ValidationAccessPayload/v1", row.AccessPayloadContractVersion);
            }
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task CommitThrowsAfterServerCommit_FreshConfirmationRecovers()
    {
        var experimentId = 23_051_000L + Random.Shared.Next(1, 999);

        await using var diScope = _factory.Services.CreateAsyncScope();
        var recorder = diScope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();
        var scope = CreateScope(experimentId);
        _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "A");
        _ = scope.GetRange(scope.SegmentStartUtc.AddHours(1), scope.ValidationBoundaryUtc, "B");

        try
        {
            _factory.Boundary.Mode = CommitFaultMode.CommitThenThrowOutcomeUnknown;
            _factory.Boundary.RemainingFaults = 1;

            var result = await recorder.FlushAsync(scope);

            // Real MySQL commit happened; the client-visible exception was recovered by fresh confirmation.
            Assert.Equal(1, _factory.Boundary.RealCommits);
            Assert.True(result.IsFullyConfirmed);
            Assert.Equal(ValidationAccessBatchCommitStatus.CommitOutcomeUnknown, result.CommitStatus);
            Assert.Equal(ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed, result.VerificationStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.ConfirmedAfterAmbiguousCommit, result.RecoveryStatus);
            Assert.True(result.UsedFreshConfirmationContext);

            await using var verifyScope = _factory.Services.CreateAsyncScope();
            var audits = verifyScope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(2, rows.Select(r => r.AccessEventId).Distinct().Count());
            Assert.Equal(2, rows.Count);

            // Cursor advanced exactly once: nothing pending afterwards, no duplicate rows on reflush.
            var noop = await recorder.FlushAsync(scope);
            Assert.Empty(noop.RequestedEventIds);
            Assert.Equal(2, (await audits.GetByExperimentIdAsync(experimentId)).Count);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task CommitThrowsBeforeServerCommit_VerificationFindsMissingAndRetries()
    {
        var experimentId = 23_052_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var batch = Enumerable.Range(1, 2)
                .Select(i => E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, i, "PreCommitFail"))
                .ToList();
            var expectedIds = batch.Select(b => b.AccessEventId).OrderBy(x => x).ToList();

            _factory.Boundary.Mode = CommitFaultMode.ThrowBeforeCommit;
            _factory.Boundary.RemainingFaults = 1;

            var result = await audits.AddRangeIdempotentByAccessEventIdAsync(batch);

            Assert.True(result.IsFullyConfirmed);
            Assert.Equal(2, result.PersistenceAttemptCount);
            Assert.Equal(ValidationAccessBatchCommitStatus.CommitSucceeded, result.CommitStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.MissingEventsRetriedAndConfirmed, result.RecoveryStatus);
            Assert.Equal(expectedIds, result.ConfirmedMatchingEventIds.OrderBy(x => x).ToList());
            Assert.True(_factory.Boundary.RollbackCalls >= 1);
            Assert.Equal(2, _factory.Boundary.CommitCalls);
            Assert.Equal(1, _factory.Boundary.RealCommits);

            // Retry reused the same AccessEventIds and unchanged payload hashes.
            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(2, rows.Count);
            foreach (var row in rows)
            {
                Assert.Equal(result.ConfirmedPayloadHashes[row.AccessEventId], row.AccessPayloadHash);
            }
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task PostCommitConfirmationSelectFails_CursorRemainsRetryable()
    {
        var experimentId = 23_053_000L + Random.Shared.Next(1, 999);

        await using var diScope = _factory.Services.CreateAsyncScope();
        var recorder = diScope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();
        var scope = CreateScope(experimentId);
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "ConfirmFail");

        try
        {
            // Pre-confirmation read stays healthy; every post-commit confirmation read fails.
            _factory.Reader.HealthyReadsBeforeFault = 1;
            _factory.Reader.RemainingFaults = 3;

            var ex = await Assert.ThrowsAsync<ValidationAccessConfirmationUnavailableException>(
                () => recorder.FlushAsync(scope));

            Assert.Equal(ValidationAccessBatchCommitStatus.CommitSucceeded, ex.PersistResult.CommitStatus);
            Assert.Equal(
                ValidationAccessBatchVerificationStatus.ConfirmationUnavailable,
                ex.PersistResult.VerificationStatus);
            Assert.Equal("VALIDATION_ACCESS_CONFIRMATION_UNAVAILABLE", ex.ErrorCode);

            // Cursor unchanged.
            Assert.Null(scope.AccessLog[0].PersistedAtUtc);

            // Heal the reader: a later flush confirms the SAME events; exactly one row per event.
            _factory.Reader.HealthyReadsBeforeFault = int.MaxValue;
            _factory.Reader.RemainingFaults = 0;

            var recovered = await recorder.FlushAsync(scope);
            Assert.True(recovered.IsFullyConfirmed);
            Assert.Contains(scope.AccessLog[0].AccessEventId, recovered.ConfirmedMatchingEventIds);
            Assert.NotNull(scope.AccessLog[0].PersistedAtUtc);

            await using var verifyScope = _factory.Services.CreateAsyncScope();
            var audits = verifyScope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Single(rows);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task MixedBatch_AmbiguousCommit_ConfirmsEveryEvent()
    {
        var experimentId = 23_054_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var existing = E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, 1, "Mixed");
            Assert.True((await audits.AddRangeIdempotentByAccessEventIdAsync([existing])).IsFullyConfirmed);

            var fresh1 = E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, 2, "Mixed");
            var fresh2 = E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, 3, "Mixed");

            _factory.Boundary.Mode = CommitFaultMode.CommitThenThrowOutcomeUnknown;
            _factory.Boundary.RemainingFaults = 1;

            var result = await audits.AddRangeIdempotentByAccessEventIdAsync([existing, fresh1, fresh2]);

            Assert.True(result.IsFullyConfirmed);
            Assert.Equal(3, result.ConfirmedMatchingEventIds.Count);
            Assert.Contains(existing.AccessEventId, result.ExistingPayloadVerifiedEventIds);
            Assert.Equal(ValidationAccessBatchCommitStatus.CommitOutcomeUnknown, result.CommitStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.ConfirmedAfterAmbiguousCommit, result.RecoveryStatus);

            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(3, rows.Select(r => r.AccessEventId).Distinct().Count());
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task ConfirmationSubset_DoesNotAdvanceCursor()
    {
        var experimentId = 23_055_000L + Random.Shared.Next(1, 999);

        await using var diScope = _factory.Services.CreateAsyncScope();
        var recorder = diScope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();
        var scope = CreateScope(experimentId);
        _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "S1");
        _ = scope.GetRange(scope.SegmentStartUtc.AddHours(1), scope.ValidationBoundaryUtc, "S2");

        try
        {
            // Every confirmation after the healthy pre-confirmation returns a subset (one row dropped).
            _factory.Reader.HealthyReadsBeforeFault = 1;
            _factory.Reader.RemainingFaults = 99;
            _factory.Reader.DropOneRowInsteadOfThrow = true;

            var ex = await Assert.ThrowsAsync<ValidationAccessPersistenceRetryExhaustedException>(
                () => recorder.FlushAsync(scope));

            Assert.Equal(
                ValidationAccessBatchVerificationStatus.PartiallyPayloadConfirmed,
                ex.PersistResult.VerificationStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.RetryExhausted, ex.PersistResult.RecoveryStatus);
            Assert.NotEmpty(ex.PersistResult.MissingEventIds);
            Assert.All(scope.AccessLog, r => Assert.Null(r.PersistedAtUtc));

            _factory.Reader.RemainingFaults = 0;
            _factory.Reader.DropOneRowInsteadOfThrow = false;
            _factory.Reader.HealthyReadsBeforeFault = int.MaxValue;

            var recovered = await recorder.FlushAsync(scope);
            Assert.True(recovered.IsFullyConfirmed);

            await using var verifyScope = _factory.Services.CreateAsyncScope();
            var audits = verifyScope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(2, rows.Count);
            Assert.Equal(2, rows.Select(r => r.AccessEventId).Distinct().Count());
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task ConfirmationUsesFreshDbContextAfterAmbiguousTransaction()
    {
        var experimentId = 23_056_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var scopedDb = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        try
        {
            _factory.Boundary.Mode = CommitFaultMode.CommitThenThrowOutcomeUnknown;
            _factory.Boundary.RemainingFaults = 1;

            var batch = new[]
            {
                E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, 1, "FreshCtx")
            };
            var result = await audits.AddRangeIdempotentByAccessEventIdAsync(batch);

            Assert.True(result.IsFullyConfirmed);
            Assert.True(result.UsedFreshConfirmationContext);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.ConfirmedAfterAmbiguousCommit, result.RecoveryStatus);

            // Every confirmation used a context distinct from the scoped write-side context,
            // and each read created its own fresh context/connection.
            Assert.NotEmpty(_factory.Reader.ObservedContextIds);
            Assert.DoesNotContain(scopedDb.ContextId.InstanceId, _factory.Reader.ObservedContextIds);
            Assert.Equal(
                _factory.Reader.ObservedContextIds.Count,
                _factory.Reader.ObservedContextIds.Distinct().Count());
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task CancellationDuringCommit_IsTreatedAsOutcomeUnknown()
    {
        var experimentId = 23_057_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            // Real commit reaches MySQL, then the client observes OperationCanceledException.
            _factory.Boundary.Mode = CommitFaultMode.CommitThenThrowOperationCanceled;
            _factory.Boundary.RemainingFaults = 1;

            var batch = new[]
            {
                E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, 1, "CancelCommit")
            };
            var result = await audits.AddRangeIdempotentByAccessEventIdAsync(batch);

            // Never classified as rollback: outcome unknown, then verified durable.
            Assert.Equal(ValidationAccessBatchCommitStatus.CommitOutcomeUnknown, result.CommitStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.ConfirmedAfterAmbiguousCommit, result.RecoveryStatus);
            Assert.True(result.IsFullyConfirmed);
            Assert.Equal(1, _factory.Boundary.RealCommits);

            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Single(rows);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task RetryExhausted_ReturnsTruthfulResultAndLeavesCursorUnchanged()
    {
        var experimentId = 23_058_000L + Random.Shared.Next(1, 999);

        await using var diScope = _factory.Services.CreateAsyncScope();
        var recorder = diScope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();
        var scope = CreateScope(experimentId);
        _ = scope.GetRange(scope.SegmentStartUtc, scope.ValidationBoundaryUtc, "Exhaust");

        try
        {
            _factory.Boundary.Mode = CommitFaultMode.ThrowBeforeCommit;
            _factory.Boundary.RemainingFaults = 99;

            var ex = await Assert.ThrowsAsync<ValidationAccessPersistenceRetryExhaustedException>(
                () => recorder.FlushAsync(scope));

            Assert.Equal(3, ex.PersistResult.PersistenceAttemptCount);
            Assert.Equal(ValidationAccessBatchCommitStatus.KnownRolledBack, ex.PersistResult.CommitStatus);
            Assert.Equal(ValidationAccessBatchRecoveryStatus.RetryExhausted, ex.PersistResult.RecoveryStatus);
            Assert.Equal("VALIDATION_ACCESS_PERSISTENCE_RETRY_EXHAUSTED", ex.ErrorCode);
            Assert.NotEmpty(ex.PersistResult.MissingEventIds);
            Assert.Null(scope.AccessLog[0].PersistedAtUtc);

            await using var verifyScope = _factory.Services.CreateAsyncScope();
            var audits = verifyScope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
            Assert.Empty(await audits.GetByExperimentIdAsync(experimentId));

            // Heal: a later flush persists the SAME events exactly once.
            _factory.Boundary.Mode = CommitFaultMode.CommitNormally;
            _factory.Boundary.RemainingFaults = 0;

            var recovered = await recorder.FlushAsync(scope);
            Assert.True(recovered.IsFullyConfirmed);
            Assert.Single(await audits.GetByExperimentIdAsync(experimentId));
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task PerformanceGuard_LargeBatch_UsesBatchedConfirmation_NoPerEventQueries()
    {
        var experimentId = 23_059_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var batch = Enumerable.Range(1, 100)
                .Select(i => E2BAuditFixtures.NewAudit(experimentId, Guid.NewGuid(), scopeExecutionId, i, "Perf"))
                .ToList();

            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = await audits.AddRangeIdempotentByAccessEventIdAsync(batch);
            started.Stop();

            Assert.True(result.IsFullyConfirmed);
            Assert.Equal(100, result.ConfirmedMatchingEventIds.Count);
            Assert.Equal(1, result.PersistenceAttemptCount);

            // One batched pre-confirmation SELECT + one batched post-commit confirmation SELECT.
            Assert.Equal(2, _factory.Reader.ReadCalls);

            var rows = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(100, rows.Count);
            Assert.True(
                started.Elapsed < TimeSpan.FromSeconds(30),
                $"100-event payload-verified persist took {started.Elapsed}.");
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    private static ValidationTrainingCandleScope CreateScope(long experimentId)
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
            }
        };
        return new ValidationTrainingCandleScope(experimentId, start, boundary, candles);
    }
}
