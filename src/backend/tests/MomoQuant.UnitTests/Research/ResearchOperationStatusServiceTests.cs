using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Research;
using MomoQuant.Domain.Research;

namespace MomoQuant.UnitTests.Research;

public sealed class ResearchOperationStatusServiceTests
{
    [Fact]
    public async Task AdvanceProgress_RejectsRegression_DetectsStale_EnforcesCancelOwnership()
    {
        var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryResearchOperationStatusRepository();
        var service = new ResearchOperationStatusService(repo, clock);

        var created = await service.UpsertValidationTrainingAsync(new ResearchOperationStatus
        {
            OperationId = "vl-train-99",
            CorrelationId = "corr-99",
            OperationType = ResearchOperationStatusCodes.ValidationTrainingType,
            EntityId = "99",
            Stage = "TrainingRunning",
            Status = "TrainingRunning",
            PercentComplete = 20m,
            RequestedWorkCount = 5,
            CompletedWorkCount = 1,
            FailedWorkCount = 0,
            LeaseOwner = "owner@test",
            StartedAtUtc = clock.GetUtcNow().UtcDateTime,
            LastHeartbeatAtUtc = clock.GetUtcNow().UtcDateTime
        });

        Assert.Equal(20m, created.PercentComplete);

        var advanced = await service.AdvanceProgressAsync("vl-train-99", 40m, 2, 0);
        Assert.True(advanced.Succeeded);
        Assert.Equal(40m, advanced.Data!.PercentComplete);

        var regress = await service.AdvanceProgressAsync("vl-train-99", 30m, 1, 0);
        Assert.False(regress.Succeeded);
        Assert.Equal(ResearchOperationStatusCodes.ProgressRegressionRejected, regress.ErrorField);

        clock.Advance(TimeSpan.FromMinutes(10));
        var stale = await service.DetectAndMarkStaleAsync("vl-train-99", TimeSpan.FromMinutes(5));
        Assert.Equal(ResearchOperationStatusCodes.Stale, stale!.Status);
        Assert.Equal(ResearchOperationStatusCodes.HeartbeatStale, stale.ErrorCode);

        var forbidden = await service.CancelAsync("vl-train-99", "stranger@test", callerIsAdmin: false);
        Assert.False(forbidden.Succeeded);
        Assert.Equal(ResearchOperationStatusCodes.CancelForbidden, forbidden.ErrorField);

        var ownerCancel = await service.CancelAsync("vl-train-99", "owner@test", callerIsAdmin: false);
        Assert.True(ownerCancel.Succeeded);
        Assert.Equal(ResearchOperationStatusCodes.Cancelled, ownerCancel.Data!.Status);
    }

    private sealed class InMemoryResearchOperationStatusRepository : IResearchOperationStatusRepository
    {
        private readonly Dictionary<string, ResearchOperationStatusEntity> _byOp = new(StringComparer.Ordinal);
        private long _nextId = 1;

        public Task<ResearchOperationStatusEntity?> GetByOperationIdAsync(
            string operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_byOp.TryGetValue(operationId, out var e) ? Clone(e) : null);

        public Task<ResearchOperationStatusEntity?> GetByEntityAsync(
            string operationType,
            string entityId,
            CancellationToken cancellationToken = default)
        {
            var match = _byOp.Values.FirstOrDefault(e =>
                e.OperationType == operationType && e.EntityId == entityId);
            return Task.FromResult(match is null ? null : Clone(match));
        }

        public Task AddAsync(ResearchOperationStatusEntity entity, CancellationToken cancellationToken = default)
        {
            entity.Id = _nextId++;
            _byOp[entity.OperationId] = Clone(entity);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ResearchOperationStatusEntity entity, CancellationToken cancellationToken = default)
        {
            _byOp[entity.OperationId] = Clone(entity);
            return Task.CompletedTask;
        }

        public Task DeleteByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
        {
            foreach (var key in _byOp.Where(kv => kv.Value.CorrelationId == correlationId).Select(kv => kv.Key).ToList())
            {
                _byOp.Remove(key);
            }

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static ResearchOperationStatusEntity Clone(ResearchOperationStatusEntity e) => new()
        {
            Id = e.Id,
            OperationId = e.OperationId,
            CorrelationId = e.CorrelationId,
            OperationType = e.OperationType,
            EntityId = e.EntityId,
            Stage = e.Stage,
            Status = e.Status,
            PercentComplete = e.PercentComplete,
            RequestedWorkCount = e.RequestedWorkCount,
            CompletedWorkCount = e.CompletedWorkCount,
            FailedWorkCount = e.FailedWorkCount,
            ActiveWorkItem = e.ActiveWorkItem,
            StartedAtUtc = e.StartedAtUtc,
            LastProgressAtUtc = e.LastProgressAtUtc,
            LastHeartbeatAtUtc = e.LastHeartbeatAtUtc,
            CompletedAtUtc = e.CompletedAtUtc,
            ErrorCode = e.ErrorCode,
            UserSafeErrorMessage = e.UserSafeErrorMessage,
            DiagnosticReference = e.DiagnosticReference,
            LeaseOwner = e.LeaseOwner,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc
        };
    }
}
