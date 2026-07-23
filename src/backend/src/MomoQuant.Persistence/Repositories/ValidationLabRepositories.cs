using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Persistence.Repositories;

public sealed class ValidationExperimentRepository : IValidationExperimentRepository
{
    private readonly MomoQuantDbContext _db;

    public ValidationExperimentRepository(MomoQuantDbContext db) => _db = db;

    public Task<ValidationExperiment?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _db.ValidationExperiments.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ValidationExperiment>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        await _db.ValidationExperiments
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ValidationExperiment>> GetByStrategyFingerprintOverlapAsync(
        string strategyCode,
        string strategyVersion,
        string symbol,
        string timeframe,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationExperiments
            .Where(e => e.StrategyCode == strategyCode
                && e.StrategyVersion == strategyVersion
                && e.Symbol == symbol
                && e.Timeframe == timeframe
                && e.ValidationRevealStatus == ValidationRevealStatus.Revealed)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default)
    {
        _db.ValidationExperiments.Add(experiment);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default)
    {
        _db.ValidationExperiments.Update(experiment);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ValidationParameterTrialRepository : IValidationParameterTrialRepository
{
    private readonly MomoQuantDbContext _db;

    public ValidationParameterTrialRepository(MomoQuantDbContext db) => _db = db;

    public async Task<IReadOnlyList<ValidationParameterTrial>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationParameterTrials
            .Where(t => t.ValidationExperimentId == experimentId)
            .OrderBy(t => t.TrialNumber)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default)
    {
        _db.ValidationParameterTrials.Add(trial);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<ValidationParameterTrial> trials, CancellationToken cancellationToken = default)
    {
        _db.ValidationParameterTrials.AddRange(trials);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default)
    {
        _db.ValidationParameterTrials.Update(trial);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<ValidationParameterTrial?> GetByExperimentAndFingerprintAsync(
        long experimentId,
        string parameterFingerprint,
        CancellationToken cancellationToken = default) =>
        _db.ValidationParameterTrials.FirstOrDefaultAsync(
            t => t.ValidationExperimentId == experimentId && t.ParameterFingerprint == parameterFingerprint,
            cancellationToken);
}

public sealed class ValidationExperimentExecutionLeaseRepository : IValidationExperimentExecutionLeaseRepository
{
    private readonly MomoQuantDbContext _db;

    public ValidationExperimentExecutionLeaseRepository(MomoQuantDbContext db) => _db = db;

    public Task<ValidationExperimentExecutionLease?> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        _db.ValidationExperimentExecutionLeases.AsNoTracking().FirstOrDefaultAsync(
            l => l.ValidationExperimentId == experimentId,
            cancellationToken);

    public async Task<bool> TryAcquireAtomicAsync(
        long experimentId,
        string leaseOwner,
        DateTime acquiredAtUtc,
        DateTime expiresAtUtc,
        DateTime heartbeatAtUtc,
        CancellationToken cancellationToken = default)
    {
        // Same-owner renew: preserve AcquiredAtUtc.
        var renewed = await _db.ValidationExperimentExecutionLeases
            .Where(l =>
                l.ValidationExperimentId == experimentId
                && l.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.ExpiresAtUtc, expiresAtUtc)
                    .SetProperty(l => l.HeartbeatAtUtc, heartbeatAtUtc),
                cancellationToken);
        if (renewed == 1)
        {
            return true;
        }

        // Expired reclaim: take ownership and reset AcquiredAtUtc.
        var reclaimed = await _db.ValidationExperimentExecutionLeases
            .Where(l =>
                l.ValidationExperimentId == experimentId
                && l.ExpiresAtUtc <= acquiredAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.LeaseOwner, leaseOwner)
                    .SetProperty(l => l.AcquiredAtUtc, acquiredAtUtc)
                    .SetProperty(l => l.ExpiresAtUtc, expiresAtUtc)
                    .SetProperty(l => l.HeartbeatAtUtc, heartbeatAtUtc),
                cancellationToken);
        if (reclaimed == 1)
        {
            return true;
        }

        // No row (or active foreign owner): attempt insert. Unique index makes races conflict-safe.
        try
        {
            _db.ValidationExperimentExecutionLeases.Add(new ValidationExperimentExecutionLease
            {
                ValidationExperimentId = experimentId,
                LeaseOwner = leaseOwner,
                AcquiredAtUtc = acquiredAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                HeartbeatAtUtc = heartbeatAtUtc
            });
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another worker inserted/won; clear tracked entity and report conflict.
            foreach (var entry in _db.ChangeTracker.Entries<ValidationExperimentExecutionLease>()
                         .Where(e => e.Entity.ValidationExperimentId == experimentId)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    public async Task<bool> TryHeartbeatOwnedAsync(
        long experimentId,
        string leaseOwner,
        DateTime expiresAtUtc,
        DateTime heartbeatAtUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await _db.ValidationExperimentExecutionLeases
            .Where(l =>
                l.ValidationExperimentId == experimentId
                && l.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.ExpiresAtUtc, expiresAtUtc)
                    .SetProperty(l => l.HeartbeatAtUtc, heartbeatAtUtc),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryReleaseOwnedAsync(
        long experimentId,
        string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _db.ValidationExperimentExecutionLeases
            .Where(l =>
                l.ValidationExperimentId == experimentId
                && l.LeaseOwner == leaseOwner)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1;
    }

    public async Task UpsertAsync(ValidationExperimentExecutionLease lease, CancellationToken cancellationToken = default)
    {
        await TryAcquireAtomicAsync(
            lease.ValidationExperimentId,
            lease.LeaseOwner,
            lease.AcquiredAtUtc,
            lease.ExpiresAtUtc,
            lease.HeartbeatAtUtc,
            cancellationToken);
    }

    public async Task ReleaseAsync(long experimentId, CancellationToken cancellationToken = default)
    {
        await _db.ValidationExperimentExecutionLeases
            .Where(l => l.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed class ValidationSegmentResultRepository : IValidationSegmentResultRepository
{
    private readonly MomoQuantDbContext _db;

    public ValidationSegmentResultRepository(MomoQuantDbContext db) => _db = db;

    public async Task<IReadOnlyList<ValidationSegmentResult>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationSegmentResults
            .Where(r => r.ValidationExperimentId == experimentId)
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(ValidationSegmentResult result, CancellationToken cancellationToken = default)
    {
        var existing = await _db.ValidationSegmentResults.FirstOrDefaultAsync(
            r => r.ValidationExperimentId == result.ValidationExperimentId
                && r.SegmentType == result.SegmentType
                && r.LayerType == result.LayerType,
            cancellationToken);

        if (existing is null)
        {
            _db.ValidationSegmentResults.Add(result);
        }
        else
        {
            existing.StrategyLabRunId = result.StrategyLabRunId;
            existing.MetricsJson = result.MetricsJson;
            existing.CandleCount = result.CandleCount;
            existing.CandidateCount = result.CandidateCount;
            existing.ClosedTradeCount = result.ClosedTradeCount;
            existing.NetExpectancyR = result.NetExpectancyR;
            existing.ProfitFactor = result.ProfitFactor;
            existing.NetPnl = result.NetPnl;
            existing.NetReturnPercent = result.NetReturnPercent;
            existing.MaximumDrawdownPercent = result.MaximumDrawdownPercent;
            existing.TransactionCosts = result.TransactionCosts;
            existing.BoundaryCensoredCount = result.BoundaryCensoredCount;
            existing.ResultFingerprint = result.ResultFingerprint;
            existing.CreatedAtUtc = result.CreatedAtUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ValidationCandleAccessAuditRepository : IValidationCandleAccessAuditRepository
{
    private readonly MomoQuantDbContext _db;

    public ValidationCandleAccessAuditRepository(MomoQuantDbContext db) => _db = db;

    public async Task AddRangeAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default)
    {
        if (audits.Count == 0)
        {
            return;
        }

        _db.ValidationCandleAccessAudits.AddRange(audits);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationCandleAccessAudits
            .AsNoTracking()
            .Where(a => a.ValidationExperimentId == experimentId)
            .OrderBy(a => a.AccessedAtUtc)
            .ToListAsync(cancellationToken);
}

