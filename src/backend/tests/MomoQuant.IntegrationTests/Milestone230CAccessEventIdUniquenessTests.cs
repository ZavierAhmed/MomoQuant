using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0C Part 9 — MySQL uniqueness on ValidationCandleAccessAudits.AccessEventId.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230CAccessEventIdUniquenessTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone230CAccessEventIdUniquenessTests(MomoQuantWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task AccessEventId_UniqueIndex_RejectsDuplicate_AndIdempotentAddSkips()
    {
        var accessEventId = Guid.NewGuid();
        var scopeExecutionId = Guid.NewGuid();
        var experimentId = 23_003_000L + Random.Shared.Next(1, 999);

        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var first = NewAudit(experimentId, accessEventId, scopeExecutionId, "First");
            Assert.Equal(1, (await audits.AddRangeIdempotentByAccessEventIdAsync([first])).NewlyInsertedCount);

            // E2B: replay must present the identical immutable payload; conflicting payloads fail closed.
            var replay = await audits.AddRangeIdempotentByAccessEventIdAsync([first]);
            Assert.True(replay.IsFullyConfirmed);
            Assert.Equal(0, replay.NewlyInsertedCount);

            var loaded = await audits.GetByExperimentIdAsync(experimentId);
            Assert.Single(loaded.Where(a => a.AccessEventId == accessEventId));

            // Direct insert of same AccessEventId must violate unique index (fresh context).
            await using var scope2 = _factory.Services.CreateAsyncScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            db2.ValidationCandleAccessAudits.Add(
                NewAudit(experimentId, accessEventId, Guid.NewGuid(), "RawDuplicate"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = _factory.Services.CreateAsyncScope();
            var dbCleanup = cleanup.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            await dbCleanup.ValidationCandleAccessAudits
                .Where(a => a.ValidationExperimentId == experimentId)
                .ExecuteDeleteAsync();
        }
    }

    private static ValidationCandleAccessAudit NewAudit(
        long experimentId,
        Guid accessEventId,
        Guid scopeExecutionId,
        string caller) =>
        new()
        {
            AccessEventId = accessEventId,
            ScopeExecutionId = scopeExecutionId,
            ScopeSequenceNumber = 1,
            ValidationExperimentId = experimentId,
            TrialNumber = 1,
            CallerComponent = caller,
            AccessPurpose = "ByOpenTime",
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
