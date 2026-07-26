using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2B WP3/WP4/WP13/WP15 — in-batch duplicate validation, persisted payload-conflict
/// detection, historical hashless-row compatibility, and cross-worker MySQL concurrency, all against
/// the real production repository.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2BPayloadConflictTests : IClassFixture<E2BSeamFactory>
{
    private readonly E2BSeamFactory _factory;

    public Milestone230E2BPayloadConflictTests(E2BSeamFactory factory)
    {
        _factory = factory;
        _factory.ResetSeams();
    }

    [Fact]
    public async Task InBatchDuplicateId_IdenticalPayload_CollapsesSafely()
    {
        var experimentId = 23_060_000L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var occurrenceA = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Same");
            var occurrenceB = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Same");

            var result = await audits.AddRangeIdempotentByAccessEventIdAsync([occurrenceA, occurrenceB]);

            Assert.True(result.IsFullyConfirmed);
            Assert.Contains(accessEventId, result.IdenticalInputDuplicateEventIds);
            Assert.Single(result.RequestedEventIds);
            Assert.Single(await audits.GetByExperimentIdAsync(experimentId));
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task InBatchDuplicateId_DifferentPayload_FailsBeforeTransaction()
    {
        var experimentId = 23_061_000L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var occurrenceA = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "CallerA");
            var occurrenceB = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 2, "CallerB");

            var ex = await Assert.ThrowsAsync<ValidationAccessInputBatchConflictException>(
                () => audits.AddRangeIdempotentByAccessEventIdAsync([occurrenceA, occurrenceB]));

            Assert.Equal("VALIDATION_ACCESS_INPUT_BATCH_CONFLICT", ex.ErrorCode);
            Assert.Equal(accessEventId, ex.AccessEventId);
            Assert.Equal(2, ex.ConflictingPayloadHashes.Count);
            Assert.Contains("ScopeSequenceNumber", ex.ConflictingFields);
            Assert.Contains("CallerComponent", ex.ConflictingFields);

            // Failed before any database access: no transaction, no confirmation query, no rows.
            Assert.Equal(0, _factory.Boundary.CommitCalls);
            Assert.Equal(0, _factory.Reader.ReadCalls);
            Assert.Empty(await audits.GetByExperimentIdAsync(experimentId));

            // Safe message: identifies fields without exposing payload values.
            Assert.DoesNotContain("ABCD1234", ex.SafeMessage);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task EmptyGuidAccessEventId_IsRejectedBeforeTransaction()
    {
        var experimentId = 23_062_000L + Random.Shared.Next(1, 999);

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        var invalid = E2BAuditFixtures.NewAudit(experimentId, Guid.Empty, Guid.NewGuid(), 1, "Empty");
        var ex = await Assert.ThrowsAsync<ValidationAccessInputBatchConflictException>(
            () => audits.AddRangeIdempotentByAccessEventIdAsync([invalid]));

        Assert.Equal(Guid.Empty, ex.AccessEventId);
        Assert.Equal(0, _factory.Boundary.CommitCalls);
        Assert.Equal(0, _factory.Reader.ReadCalls);
    }

    [Fact]
    public async Task ExistingId_IdenticalPayload_IsVerified()
    {
        var experimentId = 23_063_000L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var original = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Verified");
            Assert.True((await audits.AddRangeIdempotentByAccessEventIdAsync([original])).IsFullyConfirmed);

            var replay = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Verified");
            var result = await audits.AddRangeIdempotentByAccessEventIdAsync([replay]);

            Assert.True(result.IsFullyConfirmed);
            Assert.Contains(accessEventId, result.ExistingPayloadVerifiedEventIds);
            Assert.Equal(ValidationAccessBatchCommitStatus.NotAttempted, result.CommitStatus);
            Assert.Empty(result.AttemptedEventIds);
            Assert.Single(await audits.GetByExperimentIdAsync(experimentId));
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Theory]
    [InlineData("Experiment")]
    [InlineData("ScopeExecution")]
    [InlineData("Sequence")]
    [InlineData("AllowedVsDenied")]
    [InlineData("Fingerprint")]
    public async Task ExistingId_ConflictingPayload_FailsClosed_StoredRowUnchanged(string variant)
    {
        var experimentId = 23_064_000L + Random.Shared.Next(1, 999);
        var otherExperimentId = experimentId + 100_000;
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var original = E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Original");
            var first = await audits.AddRangeIdempotentByAccessEventIdAsync([original]);
            Assert.True(first.IsFullyConfirmed);
            var originalHash = first.ConfirmedPayloadHashes[accessEventId];

            var conflicting = variant switch
            {
                "Experiment" => E2BAuditFixtures.NewAudit(otherExperimentId, accessEventId, scopeExecutionId, 1, "Original"),
                "ScopeExecution" => E2BAuditFixtures.NewAudit(experimentId, accessEventId, Guid.NewGuid(), 1, "Original"),
                "Sequence" => E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 2, "Original"),
                "AllowedVsDenied" => E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Original", wasDenied: true),
                "Fingerprint" => E2BAuditFixtures.NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "Original", fingerprint: "FFFF9999"),
                _ => throw new InvalidOperationException(variant)
            };

            var ex = await Assert.ThrowsAsync<ValidationAccessPersistedPayloadConflictException>(
                () => audits.AddRangeIdempotentByAccessEventIdAsync([conflicting]));

            Assert.Equal("VALIDATION_ACCESS_PERSISTED_PAYLOAD_CONFLICT", ex.ErrorCode);
            Assert.Equal(accessEventId, ex.AccessEventId);
            Assert.NotEmpty(ex.ConflictingFieldNames);
            Assert.Equal(originalHash, ex.PersistedPayloadHash);
            Assert.NotEqual(ex.RequestedPayloadHash, ex.PersistedPayloadHash);

            // Safe message never exposes hashes, fingerprints, or candle ranges.
            Assert.DoesNotContain(originalHash, ex.SafeMessage);
            Assert.DoesNotContain("ABCD1234", ex.SafeMessage);
            Assert.DoesNotContain("FFFF9999", ex.SafeMessage);

            // Stored row is unchanged and no second row exists.
            var rows = await audits.GetByExperimentIdAsync(experimentId);
            var row = Assert.Single(rows);
            Assert.Equal("Original", row.CallerComponent);
            Assert.Equal(1, row.ScopeSequenceNumber);
            Assert.False(row.WasDenied);
            Assert.Equal("ABCD1234", row.CandleContentFingerprint);
            Assert.Equal(originalHash, row.AccessPayloadHash);
            Assert.Empty(await audits.GetByExperimentIdAsync(otherExperimentId));
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
            await E2BAuditFixtures.CleanupAsync(_factory, otherExperimentId);
        }
    }

    [Fact]
    public async Task HistoricalHashlessRow_IdenticalPayload_IsLegacyVerified_WithoutBackfill()
    {
        var experimentId = 23_065_000L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();
        var accessedAt = new DateTime(2024, 5, 1, 8, 0, 0, DateTimeKind.Utc);

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            // Historical pre-E2B row: no payload hash, v1 recorder version.
            var legacy = E2BAuditFixtures.NewAudit(
                experimentId, accessEventId, scopeExecutionId, 1, "Legacy",
                accessedAtUtc: accessedAt, recorderVersion: "ValidationCandleAccess/v1");
            legacy.AccessPayloadHash = null;
            legacy.AccessPayloadContractVersion = null;
            await audits.AddRangeAsync([legacy]);

            var replay = E2BAuditFixtures.NewAudit(
                experimentId, accessEventId, scopeExecutionId, 1, "Legacy",
                accessedAtUtc: accessedAt, recorderVersion: "ValidationCandleAccess/v1");

            var result = await audits.AddRangeIdempotentByAccessEventIdAsync([replay]);

            Assert.True(result.IsFullyConfirmed);
            Assert.Contains(accessEventId, result.LegacyPayloadVerifiedEventIds);

            // No automatic backfill: the historical row keeps its null hash.
            var row = Assert.Single(await audits.GetByExperimentIdAsync(experimentId));
            Assert.Null(row.AccessPayloadHash);
            Assert.Null(row.AccessPayloadContractVersion);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task HistoricalHashlessRow_DifferentPayload_FailsConflict()
    {
        var experimentId = 23_066_000L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();
        var accessedAt = new DateTime(2024, 5, 1, 8, 0, 0, DateTimeKind.Utc);

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var legacy = E2BAuditFixtures.NewAudit(
                experimentId, accessEventId, scopeExecutionId, 1, "Legacy",
                accessedAtUtc: accessedAt, recorderVersion: "ValidationCandleAccess/v1");
            legacy.AccessPayloadHash = null;
            legacy.AccessPayloadContractVersion = null;
            await audits.AddRangeAsync([legacy]);

            var conflicting = E2BAuditFixtures.NewAudit(
                experimentId, accessEventId, scopeExecutionId, 2, "DifferentLegacy",
                accessedAtUtc: accessedAt, recorderVersion: "ValidationCandleAccess/v1");

            var ex = await Assert.ThrowsAsync<ValidationAccessPersistedPayloadConflictException>(
                () => audits.AddRangeIdempotentByAccessEventIdAsync([conflicting]));

            Assert.Contains("ScopeSequenceNumber", ex.ConflictingFieldNames);
            Assert.Contains("CallerComponent", ex.ConflictingFieldNames);
            Assert.Null(ex.PersistedPayloadHash);

            var row = Assert.Single(await audits.GetByExperimentIdAsync(experimentId));
            Assert.Equal("Legacy", row.CallerComponent);
            Assert.Null(row.AccessPayloadHash);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task ConcurrentSameBatch_TwoContexts_OneRowPerEvent_BothConfirmed()
    {
        var experimentId = 23_067_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var auditsA = scopeA.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var auditsB = scopeB.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        Assert.NotSame(
            scopeA.ServiceProvider.GetRequiredService<MomoQuantDbContext>(),
            scopeB.ServiceProvider.GetRequiredService<MomoQuantDbContext>());

        try
        {
            var batchA = new[]
            {
                E2BAuditFixtures.NewAudit(experimentId, id1, scopeExecutionId, 1, "Worker"),
                E2BAuditFixtures.NewAudit(experimentId, id2, scopeExecutionId, 2, "Worker")
            };
            var batchB = new[]
            {
                E2BAuditFixtures.NewAudit(experimentId, id1, scopeExecutionId, 1, "Worker"),
                E2BAuditFixtures.NewAudit(experimentId, id2, scopeExecutionId, 2, "Worker")
            };

            var results = await Task.WhenAll(
                auditsA.AddRangeIdempotentByAccessEventIdAsync(batchA),
                auditsB.AddRangeIdempotentByAccessEventIdAsync(batchB));

            Assert.All(results, r => Assert.True(r.IsFullyConfirmed));
            Assert.All(results, r => Assert.Equal(
                ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed, r.VerificationStatus));

            var rows = await auditsA.GetByExperimentIdAsync(experimentId);
            Assert.Equal(2, rows.Count);
            Assert.Equal(2, rows.Select(r => r.AccessEventId).Distinct().Count());
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task ConcurrentOverlappingBatch_TwoContexts_ThreeRows_BothFullSetsConfirmed()
    {
        var experimentId = 23_068_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();
        var event1 = Guid.NewGuid();
        var event2 = Guid.NewGuid();
        var event3 = Guid.NewGuid();

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var auditsA = scopeA.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var auditsB = scopeB.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var batchA = new[]
            {
                E2BAuditFixtures.NewAudit(experimentId, event1, scopeExecutionId, 1, "Overlap"),
                E2BAuditFixtures.NewAudit(experimentId, event2, scopeExecutionId, 2, "Overlap")
            };
            var batchB = new[]
            {
                E2BAuditFixtures.NewAudit(experimentId, event2, scopeExecutionId, 2, "Overlap"),
                E2BAuditFixtures.NewAudit(experimentId, event3, scopeExecutionId, 3, "Overlap")
            };

            var results = await Task.WhenAll(
                auditsA.AddRangeIdempotentByAccessEventIdAsync(batchA),
                auditsB.AddRangeIdempotentByAccessEventIdAsync(batchB));

            Assert.All(results, r => Assert.True(r.IsFullyConfirmed));

            var rows = await auditsA.GetByExperimentIdAsync(experimentId);
            Assert.Equal(3, rows.Count);
            Assert.Equal(3, rows.Select(r => r.AccessEventId).Distinct().Count());

            // Shared Event2 has exactly one stored payload, confirmed by both workers.
            var event2Row = Assert.Single(rows.Where(r => r.AccessEventId == event2));
            Assert.Equal(results[0].ConfirmedPayloadHashes[event2], event2Row.AccessPayloadHash);
            Assert.Equal(results[1].ConfirmedPayloadHashes[event2], event2Row.AccessPayloadHash);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task ConcurrentConflictingPayload_TwoContexts_ExactlyOneStoredPayload_ConflictDetected()
    {
        var experimentId = 23_069_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();
        var sharedId = Guid.NewGuid();

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var auditsA = scopeA.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var auditsB = scopeB.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var payloadA = E2BAuditFixtures.NewAudit(experimentId, sharedId, scopeExecutionId, 1, "WorkerA");
            var payloadB = E2BAuditFixtures.NewAudit(experimentId, sharedId, scopeExecutionId, 2, "WorkerB");

            var taskA = Persist(auditsA, payloadA);
            var taskB = Persist(auditsB, payloadB);
            var outcomes = await Task.WhenAll(taskA, taskB);

            var successes = outcomes.Where(o => o.Result is not null).ToList();
            var conflicts = outcomes.Where(o => o.Conflict is not null).ToList();

            // Exactly one payload wins; at least one caller observes the conflict; nobody falsely
            // confirms a payload that is not the stored one.
            Assert.Single(successes);
            Assert.Single(conflicts);

            var rows = await auditsA.GetByExperimentIdAsync(experimentId);
            var row = Assert.Single(rows);
            Assert.Equal(successes[0].Result!.ConfirmedPayloadHashes[sharedId], row.AccessPayloadHash);
            Assert.Equal("VALIDATION_ACCESS_PERSISTED_PAYLOAD_CONFLICT", conflicts[0].Conflict!.ErrorCode);
            Assert.NotEqual(
                conflicts[0].Conflict!.RequestedPayloadHash,
                row.AccessPayloadHash);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    private static async Task<(ValidationAccessBatchPersistResult? Result, ValidationAccessPersistedPayloadConflictException? Conflict)>
        Persist(IValidationCandleAccessAuditRepository audits, ValidationCandleAccessAudit audit)
    {
        try
        {
            return (await audits.AddRangeIdempotentByAccessEventIdAsync([audit]), null);
        }
        catch (ValidationAccessPersistedPayloadConflictException ex)
        {
            return (null, ex);
        }
    }
}
