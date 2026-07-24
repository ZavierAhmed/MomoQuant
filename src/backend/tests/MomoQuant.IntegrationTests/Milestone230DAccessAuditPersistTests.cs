using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0D WP11–15 — MySQL upsert + SELECT confirmation for access audits.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230DAccessAuditPersistTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone230DAccessAuditPersistTests(MomoQuantWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task MixedBatch_ConcurrentOverlap_ConfirmsAll_DoesNotLoseFresh()
    {
        var experimentId = 23_004_000L + Random.Shared.Next(1, 999);
        var scopeExecutionId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var freshId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var existing = NewAudit(experimentId, existingId, scopeExecutionId, seq: 1, "Existing");
            var first = await audits.AddRangeIdempotentByAccessEventIdAsync([existing]);
            Assert.True(first.IsFullyConfirmed);
            Assert.Equal(1, first.NewlyInsertedCount);

            var fresh = NewAudit(experimentId, freshId, scopeExecutionId, seq: 2, "Fresh");
            var mixed = await audits.AddRangeIdempotentByAccessEventIdAsync([existing, fresh]);
            Assert.True(mixed.IsFullyConfirmed);
            Assert.Equal(1, mixed.NewlyInsertedCount);
            Assert.Equal(1, mixed.AlreadyExistingCount);
            Assert.Equal(2, mixed.ConfirmedCount);
            Assert.Empty(mixed.MissingEventIds);

            var loaded = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Equal(2, loaded.Select(a => a.AccessEventId).Distinct().Count());
            Assert.Contains(loaded, a => a.AccessEventId == freshId && a.ScopeSequenceNumber == 2);
        }
        finally
        {
            await CleanupAsync(experimentId);
        }
    }

    [Fact]
    public async Task AllExisting_UpsertConfirms_WithoutDuplicateRows()
    {
        var experimentId = 23_004_100L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var row = NewAudit(experimentId, accessEventId, scopeExecutionId, seq: 1, "First");
            Assert.Equal(1, (await audits.AddRangeIdempotentByAccessEventIdAsync([row])).NewlyInsertedCount);

            var duplicate = NewAudit(experimentId, accessEventId, scopeExecutionId, seq: 1, "Dup");
            var replay = await audits.AddRangeIdempotentByAccessEventIdAsync([duplicate]);
            Assert.True(replay.IsFullyConfirmed);
            Assert.Equal(0, replay.NewlyInsertedCount);
            Assert.Equal(1, replay.AlreadyExistingCount);

            var loaded = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Single(loaded.Where(a => a.AccessEventId == accessEventId));
        }
        finally
        {
            await CleanupAsync(experimentId);
        }
    }

    [Fact]
    public async Task UniqueIndex_StillRejectsRawDuplicateInsert()
    {
        var experimentId = 23_004_200L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            Assert.Equal(1, (await audits.AddRangeIdempotentByAccessEventIdAsync(
                [NewAudit(experimentId, accessEventId, scopeExecutionId, 1, "First")])).NewlyInsertedCount);

            await using var scope2 = _factory.Services.CreateAsyncScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            db2.ValidationCandleAccessAudits.Add(
                NewAudit(experimentId, accessEventId, Guid.NewGuid(), 99, "RawDuplicate"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        }
        finally
        {
            await CleanupAsync(experimentId);
        }
    }

    private async Task CleanupAsync(long experimentId)
    {
        await using var cleanup = _factory.Services.CreateAsyncScope();
        var dbCleanup = cleanup.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await dbCleanup.ValidationCandleAccessAudits
            .Where(a => a.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
    }

    private static ValidationCandleAccessAudit NewAudit(
        long experimentId,
        Guid accessEventId,
        Guid scopeExecutionId,
        long seq,
        string caller) =>
        new()
        {
            AccessEventId = accessEventId,
            ScopeExecutionId = scopeExecutionId,
            ScopeSequenceNumber = seq,
            ValidationExperimentId = experimentId,
            TrialNumber = 1,
            CallerComponent = caller,
            AccessPurpose = "EvaluationRange",
            DatasetPartition = "Training",
            AccessedAtUtc = DateTime.UtcNow,
            WasDenied = false,
            ReturnedCandleCount = 0,
            FlushAttemptCount = 1,
            PersistedAtUtc = DateTime.UtcNow,
            RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion,
            CreatedAtUtc = DateTime.UtcNow
        };
}
