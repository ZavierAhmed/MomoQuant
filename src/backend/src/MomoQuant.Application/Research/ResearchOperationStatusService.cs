using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Research;

namespace MomoQuant.Application.Research;

public static class ResearchOperationStatusCodes
{
    public const string Stale = "Stale";
    public const string Cancelled = "Cancelled";
    public const string HeartbeatStale = "OPERATION_HEARTBEAT_STALE";
    public const string CancelForbidden = "OPERATION_CANCEL_FORBIDDEN";
    public const string ProgressRegressionRejected = "OPERATION_PROGRESS_REGRESSION_REJECTED";
    public const string ValidationTrainingType = "ValidationLaboratory.Training";
}

public interface IResearchOperationStatusService
{
    Task<ResearchOperationStatus?> GetByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<ResearchOperationStatus?> GetForValidationExperimentAsync(
        long experimentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces status for a validation training operation from controlled/mapped fields.
    /// </summary>
    Task<ResearchOperationStatus> UpsertValidationTrainingAsync(
        ResearchOperationStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs from experiment training progress (mapper path) into the durable store.
    /// </summary>
    Task<ResearchOperationStatus> SyncFromValidationTrainingAsync(
        long experimentId,
        string status,
        string stage,
        ValidationTrainingProgressDto progress,
        string? leaseOwner = null,
        string? correlationId = null,
        string? errorCode = null,
        string? userSafeError = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances progress monotonically — PercentComplete and completed work counts cannot regress.
    /// </summary>
    Task<ServiceResult<ResearchOperationStatus>> AdvanceProgressAsync(
        string operationId,
        decimal percentComplete,
        int completedWorkCount,
        int failedWorkCount,
        string? stage = null,
        string? status = null,
        string? activeWorkItem = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ResearchOperationStatus>> HeartbeatAsync(
        string operationId,
        string leaseOwner,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks operations stale when LastHeartbeatAtUtc is older than the threshold (injectable clock).
    /// </summary>
    Task<ResearchOperationStatus?> DetectAndMarkStaleAsync(
        string operationId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ResearchOperationStatus>> CancelAsync(
        string operationId,
        string callerIdentity,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);
}

public sealed class ResearchOperationStatusService : IResearchOperationStatusService
{
    private readonly IResearchOperationStatusRepository _repository;
    private readonly TimeProvider _clock;

    public ResearchOperationStatusService(
        IResearchOperationStatusRepository repository,
        TimeProvider? clock = null)
    {
        _repository = repository;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ResearchOperationStatus?> GetByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByOperationIdAsync(operationId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<ResearchOperationStatus?> GetForValidationExperimentAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByEntityAsync(
            ResearchOperationStatusCodes.ValidationTrainingType,
            experimentId.ToString(),
            cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<ResearchOperationStatus> UpsertValidationTrainingAsync(
        ResearchOperationStatus status,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var existing = await _repository.GetByOperationIdAsync(status.OperationId, cancellationToken);
        if (existing is null)
        {
            existing = new ResearchOperationStatusEntity
            {
                OperationId = status.OperationId,
                CreatedAtUtc = now
            };
            Apply(existing, status, now, allowRegression: true);
            await _repository.AddAsync(existing, cancellationToken);
        }
        else
        {
            Apply(existing, status, now, allowRegression: false);
            await _repository.UpdateAsync(existing, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return Map(existing);
    }

    public Task<ResearchOperationStatus> SyncFromValidationTrainingAsync(
        long experimentId,
        string status,
        string stage,
        ValidationTrainingProgressDto progress,
        string? leaseOwner = null,
        string? correlationId = null,
        string? errorCode = null,
        string? userSafeError = null,
        CancellationToken cancellationToken = default)
    {
        var mapped = ResearchOperationStatusMapper.FromValidationTraining(
            experimentId,
            status,
            stage,
            progress,
            leaseOwner,
            correlationId,
            errorCode,
            userSafeError);

        var now = _clock.GetUtcNow().UtcDateTime;
        mapped = new ResearchOperationStatus
        {
            OperationId = mapped.OperationId,
            CorrelationId = mapped.CorrelationId,
            OperationType = mapped.OperationType,
            EntityId = mapped.EntityId,
            Stage = mapped.Stage,
            Status = mapped.Status,
            PercentComplete = mapped.PercentComplete,
            RequestedWorkCount = mapped.RequestedWorkCount,
            CompletedWorkCount = mapped.CompletedWorkCount,
            FailedWorkCount = mapped.FailedWorkCount,
            ActiveWorkItem = mapped.ActiveWorkItem,
            StartedAtUtc = mapped.StartedAtUtc ?? now,
            LastProgressAtUtc = mapped.LastProgressAtUtc ?? now,
            LastHeartbeatAtUtc = mapped.LastHeartbeatAtUtc ?? now,
            CompletedAtUtc = mapped.CompletedAtUtc,
            ErrorCode = mapped.ErrorCode,
            UserSafeErrorMessage = mapped.UserSafeErrorMessage,
            DiagnosticReference = mapped.DiagnosticReference,
            LeaseOwner = mapped.LeaseOwner
        };

        return UpsertValidationTrainingAsync(mapped, cancellationToken);
    }

    public async Task<ServiceResult<ResearchOperationStatus>> AdvanceProgressAsync(
        string operationId,
        decimal percentComplete,
        int completedWorkCount,
        int failedWorkCount,
        string? stage = null,
        string? status = null,
        string? activeWorkItem = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByOperationIdAsync(operationId, cancellationToken);
        if (entity is null)
        {
            return ServiceResult<ResearchOperationStatus>.Fail("Operation was not found.");
        }

        if (percentComplete < entity.PercentComplete
            || completedWorkCount < entity.CompletedWorkCount)
        {
            return ServiceResult<ResearchOperationStatus>.Fail(
                "Progress cannot regress.",
                ResearchOperationStatusCodes.ProgressRegressionRejected);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        entity.PercentComplete = percentComplete;
        entity.CompletedWorkCount = completedWorkCount;
        entity.FailedWorkCount = Math.Max(entity.FailedWorkCount, failedWorkCount);
        if (!string.IsNullOrWhiteSpace(stage))
        {
            entity.Stage = stage;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            entity.Status = status;
        }

        if (activeWorkItem is not null)
        {
            entity.ActiveWorkItem = activeWorkItem;
        }

        entity.LastProgressAtUtc = now;
        entity.LastHeartbeatAtUtc = now;
        entity.UpdatedAtUtc = now;
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return ServiceResult<ResearchOperationStatus>.Ok(Map(entity));
    }

    public async Task<ServiceResult<ResearchOperationStatus>> HeartbeatAsync(
        string operationId,
        string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByOperationIdAsync(operationId, cancellationToken);
        if (entity is null)
        {
            return ServiceResult<ResearchOperationStatus>.Fail("Operation was not found.");
        }

        if (!string.IsNullOrWhiteSpace(entity.LeaseOwner)
            && !string.Equals(entity.LeaseOwner, leaseOwner, StringComparison.Ordinal))
        {
            return ServiceResult<ResearchOperationStatus>.Fail(
                "Heartbeat rejected: lease owner mismatch.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        entity.LeaseOwner = leaseOwner;
        entity.LastHeartbeatAtUtc = now;
        entity.UpdatedAtUtc = now;
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return ServiceResult<ResearchOperationStatus>.Ok(Map(entity));
    }

    public async Task<ResearchOperationStatus?> DetectAndMarkStaleAsync(
        string operationId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByOperationIdAsync(operationId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (string.Equals(entity.Status, ResearchOperationStatusCodes.Cancelled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity.Status, ResearchOperationStatusCodes.Stale, StringComparison.OrdinalIgnoreCase)
            || entity.CompletedAtUtc.HasValue)
        {
            return Map(entity);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var heartbeat = entity.LastHeartbeatAtUtc ?? entity.LastProgressAtUtc ?? entity.StartedAtUtc;
        if (heartbeat is null || now - heartbeat.Value < staleAfter)
        {
            return Map(entity);
        }

        entity.Status = ResearchOperationStatusCodes.Stale;
        entity.ErrorCode = ResearchOperationStatusCodes.HeartbeatStale;
        entity.UserSafeErrorMessage = "Operation heartbeat is stale.";
        entity.UpdatedAtUtc = now;
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ServiceResult<ResearchOperationStatus>> CancelAsync(
        string operationId,
        string callerIdentity,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByOperationIdAsync(operationId, cancellationToken);
        if (entity is null)
        {
            return ServiceResult<ResearchOperationStatus>.Fail("Operation was not found.");
        }

        var isOwner = !string.IsNullOrWhiteSpace(entity.LeaseOwner)
            && string.Equals(entity.LeaseOwner, callerIdentity, StringComparison.Ordinal);
        if (!callerIsAdmin && !isOwner)
        {
            return ServiceResult<ResearchOperationStatus>.Fail(
                "Caller is not authorized to cancel this operation.",
                ResearchOperationStatusCodes.CancelForbidden);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        entity.Status = ResearchOperationStatusCodes.Cancelled;
        entity.CompletedAtUtc = now;
        entity.UpdatedAtUtc = now;
        entity.ActiveWorkItem = null;
        entity.UserSafeErrorMessage = "Operation cancelled.";
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return ServiceResult<ResearchOperationStatus>.Ok(Map(entity));
    }

    private static void Apply(
        ResearchOperationStatusEntity entity,
        ResearchOperationStatus status,
        DateTime now,
        bool allowRegression)
    {
        entity.OperationId = status.OperationId;
        entity.CorrelationId = status.CorrelationId;
        entity.OperationType = status.OperationType;
        entity.EntityId = status.EntityId;
        entity.Stage = status.Stage;
        entity.Status = status.Status;
        entity.RequestedWorkCount = status.RequestedWorkCount;
        entity.FailedWorkCount = Math.Max(entity.FailedWorkCount, status.FailedWorkCount);
        entity.ActiveWorkItem = status.ActiveWorkItem;
        entity.StartedAtUtc = status.StartedAtUtc ?? entity.StartedAtUtc ?? now;
        entity.CompletedAtUtc = status.CompletedAtUtc ?? entity.CompletedAtUtc;
        entity.ErrorCode = status.ErrorCode;
        entity.UserSafeErrorMessage = status.UserSafeErrorMessage;
        entity.DiagnosticReference = status.DiagnosticReference;
        entity.LeaseOwner = status.LeaseOwner ?? entity.LeaseOwner;
        entity.LastHeartbeatAtUtc = status.LastHeartbeatAtUtc ?? entity.LastHeartbeatAtUtc ?? now;
        entity.UpdatedAtUtc = now;

        if (allowRegression
            || status.PercentComplete >= entity.PercentComplete)
        {
            entity.PercentComplete = status.PercentComplete;
            entity.LastProgressAtUtc = status.LastProgressAtUtc ?? now;
        }

        if (allowRegression
            || status.CompletedWorkCount >= entity.CompletedWorkCount)
        {
            entity.CompletedWorkCount = status.CompletedWorkCount;
        }
    }

    private static ResearchOperationStatus Map(ResearchOperationStatusEntity entity) => new()
    {
        OperationId = entity.OperationId,
        CorrelationId = entity.CorrelationId,
        OperationType = entity.OperationType,
        EntityId = entity.EntityId,
        Stage = entity.Stage,
        Status = entity.Status,
        PercentComplete = entity.PercentComplete,
        RequestedWorkCount = entity.RequestedWorkCount,
        CompletedWorkCount = entity.CompletedWorkCount,
        FailedWorkCount = entity.FailedWorkCount,
        ActiveWorkItem = entity.ActiveWorkItem,
        StartedAtUtc = entity.StartedAtUtc,
        LastProgressAtUtc = entity.LastProgressAtUtc,
        LastHeartbeatAtUtc = entity.LastHeartbeatAtUtc,
        CompletedAtUtc = entity.CompletedAtUtc,
        ErrorCode = entity.ErrorCode,
        UserSafeErrorMessage = entity.UserSafeErrorMessage,
        DiagnosticReference = entity.DiagnosticReference,
        LeaseOwner = entity.LeaseOwner
    };
}
