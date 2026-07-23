using MomoQuant.Application.Common;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

public interface IValidationTrainingDatabaseProbe
{
    Task<ServiceResult<bool>> CanConnectAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPendingMigrationNamesAsync(CancellationToken cancellationToken = default);
}

public interface IValidationExperimentRepository
{
    Task<ValidationExperiment?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ValidationExperiment>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ValidationExperiment>> GetByStrategyFingerprintOverlapAsync(
        string strategyCode,
        string strategyVersion,
        string symbol,
        string timeframe,
        CancellationToken cancellationToken = default);
    Task AddAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default);
    Task UpdateAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default);
}

public interface IValidationParameterTrialRepository
{
    Task<IReadOnlyList<ValidationParameterTrial>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);

    Task<ValidationParameterTrial?> GetByExperimentAndFingerprintAsync(
        long experimentId,
        string parameterFingerprint,
        CancellationToken cancellationToken = default);

    Task AddAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<ValidationParameterTrial> trials, CancellationToken cancellationToken = default);
    Task UpdateAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default);
}

public interface IValidationExperimentExecutionLeaseRepository
{
    Task<ValidationExperimentExecutionLease?> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically acquire or renew a lease. Succeeds only when no row exists,
    /// the existing lease is expired, or the same owner is renewing.
    /// </summary>
    Task<bool> TryAcquireAtomicAsync(
        long experimentId,
        string leaseOwner,
        DateTime acquiredAtUtc,
        DateTime expiresAtUtc,
        DateTime heartbeatAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Owner-protected heartbeat. Updates HeartbeatAtUtc/ExpiresAtUtc only when LeaseOwner matches.
    /// Preserves AcquiredAtUtc. Returns false when no matching owner row exists.
    /// </summary>
    Task<bool> TryHeartbeatOwnedAsync(
        long experimentId,
        string leaseOwner,
        DateTime expiresAtUtc,
        DateTime heartbeatAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Owner-protected release. Deletes only when LeaseOwner matches. Returns false when not deleted.
    /// </summary>
    Task<bool> TryReleaseOwnedAsync(
        long experimentId,
        string leaseOwner,
        CancellationToken cancellationToken = default);

    [Obsolete("Use TryAcquireAtomicAsync / TryHeartbeatOwnedAsync / TryReleaseOwnedAsync.")]
    Task UpsertAsync(ValidationExperimentExecutionLease lease, CancellationToken cancellationToken = default);

    [Obsolete("Use TryReleaseOwnedAsync.")]
    Task ReleaseAsync(long experimentId, CancellationToken cancellationToken = default);
}

public interface IValidationSegmentResultRepository
{
    Task<IReadOnlyList<ValidationSegmentResult>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);
    Task UpsertAsync(ValidationSegmentResult result, CancellationToken cancellationToken = default);
}
