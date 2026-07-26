using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
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
        catch (Exception ex) when (IsUniqueConflict(ex))
        {
            foreach (var entry in _db.ChangeTracker.Entries<ValidationExperimentExecutionLease>()
                         .Where(e => e.Entity.ValidationExperimentId == experimentId)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    private static bool IsUniqueConflict(Exception ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException!)
        {
            var msg = cur.Message ?? string.Empty;
            if (msg.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("IX_ValExpLeases_ExperimentId", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
    private readonly IValidationAccessPayloadCanonicalizer _canonicalizer;
    private readonly IValidationAccessAuditTransactionBoundary _transactionBoundary;
    private readonly IValidationAccessAuditConfirmationReader? _confirmationReader;
    private readonly IValidationAccessPersistenceRetryPolicy _retryPolicy;
    private readonly IServiceScopeFactory? _scopeFactory;

    public ValidationCandleAccessAuditRepository(
        MomoQuantDbContext db,
        IValidationAccessPayloadCanonicalizer? canonicalizer = null,
        IValidationAccessAuditTransactionBoundary? transactionBoundary = null,
        IValidationAccessAuditConfirmationReader? confirmationReader = null,
        IValidationAccessPersistenceRetryPolicy? retryPolicy = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _db = db;
        _canonicalizer = canonicalizer ?? new ValidationAccessPayloadCanonicalizer();
        _transactionBoundary = transactionBoundary ?? new ValidationAccessAuditTransactionBoundary();
        _confirmationReader = confirmationReader;
        _retryPolicy = retryPolicy ?? new ValidationAccessPersistenceRetryPolicy();
        _scopeFactory = scopeFactory;
    }

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

    public async Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default)
    {
        if (audits.Count == 0)
        {
            return ValidationAccessBatchPersistResult.EmptyNoWork();
        }

        var execution = new PersistExecution(this, audits);
        return await execution.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationCandleAccessAudits
            .AsNoTracking()
            .Where(a => a.ValidationExperimentId == experimentId)
            .OrderBy(a => a.AccessedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Creates a fresh write scope/context when a scope factory is available so retries never reuse
    /// a failed DbContext. Falls back to the injected context only when no factory is registered
    /// (unit-test construction).
    /// </summary>
    private (MomoQuantDbContext Db, IAsyncDisposable? Lifetime) CreateWriteContext()
    {
        if (_scopeFactory is not null)
        {
            var scope = _scopeFactory.CreateAsyncScope();
            return (scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>(), scope);
        }

        return (_db, null);
    }

    private async Task<IReadOnlyList<ValidationCandleAccessAudit>> ReadDurableRowsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (_confirmationReader is not null)
        {
            return await _confirmationReader.ReadAsync(ids, cancellationToken).ConfigureAwait(false);
        }

        var list = ids.ToList();
        return await _db.ValidationCandleAccessAudits
            .AsNoTracking()
            .Where(a => list.Contains(a.AccessEventId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool UsesFreshConfirmationContext => _confirmationReader?.UsesFreshContext ?? false;

    /// <summary>
    /// One idempotent, payload-verified persist execution:
    /// Phase 1 validate/canonicalize; Phase 2 fresh pre-confirmation; Phase 3 persist missing rows;
    /// Phase 4 classify commit outcome; Phase 5 full payload confirmation; Phase 6 truthful result.
    /// </summary>
    private sealed class PersistExecution
    {
        private readonly ValidationCandleAccessAuditRepository _repo;
        private readonly IReadOnlyList<ValidationCandleAccessAudit> _input;

        private List<ValidationCandleAccessAudit> _distinct = new();
        private Dictionary<Guid, string> _requestedHashes = new();
        private List<Guid> _requestedIds = new();
        private List<Guid> _identicalDuplicates = new();

        private HashSet<Guid> _initialExisting = new();
        private List<Guid> _existingPayloadVerified = new();
        private List<Guid> _legacyPayloadVerified = new();
        private HashSet<Guid> _confirmedMatching = new();
        private Dictionary<Guid, string> _confirmedHashes = new();
        private List<Guid> _missing = new();
        private List<Guid> _attempted = new();

        private ValidationAccessBatchCommitStatus _commitStatus = ValidationAccessBatchCommitStatus.NotAttempted;
        private int _persistenceAttempts;
        private int _confirmationAttempts;
        private bool _initialConfirmationDone;
        private Exception? _lastError;

        public PersistExecution(
            ValidationCandleAccessAuditRepository repo,
            IReadOnlyList<ValidationCandleAccessAudit> input)
        {
            _repo = repo;
            _input = input;
        }

        public async Task<ValidationAccessBatchPersistResult> RunAsync(CancellationToken cancellationToken)
        {
            // Phase 1 — validate and canonicalize before any database access.
            ValidateAndCanonicalizeInput();

            // Phase 2 — fresh pre-confirmation: verified / conflicting / missing.
            await ConfirmDurableStateAsync(cancellationToken).ConfigureAwait(false);
            _initialExisting = _confirmedMatching.ToHashSet();
            _initialConfirmationDone = true;

            if (_missing.Count == 0)
            {
                return BuildSuccessResult();
            }

            // Phases 3–5 — bounded write attempts, each followed by full fresh confirmation.
            for (var attempt = 1; attempt <= _repo._retryPolicy.MaxPersistenceAttempts; attempt++)
            {
                if (_commitStatus != ValidationAccessBatchCommitStatus.CommitOutcomeUnknown)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                _persistenceAttempts = attempt;
                var writeOutcome = await TryWriteMissingRowsAsync(cancellationToken).ConfigureAwait(false);

                await ConfirmDurableStateAsync(cancellationToken).ConfigureAwait(false);
                if (_missing.Count == 0)
                {
                    return BuildSuccessResult();
                }

                if (!writeOutcome.Retryable)
                {
                    throw new ValidationAccessEvidencePersistenceException(
                        BuildResult(
                            ValidationAccessBatchVerificationStatus.FailedPermanent,
                            ValidationAccessBatchRecoveryStatus.None,
                            ValidationAccessEvidencePersistenceException.Code),
                        _lastError!);
                }

                if (attempt < _repo._retryPolicy.MaxPersistenceAttempts)
                {
                    await _repo._retryPolicy.DelayAsync(attempt, SafeDelayToken(cancellationToken))
                        .ConfigureAwait(false);
                }
            }

            var exhausted = BuildResult(
                _confirmedMatching.Count == 0
                    ? ValidationAccessBatchVerificationStatus.FailedPermanent
                    : ValidationAccessBatchVerificationStatus.PartiallyPayloadConfirmed,
                ValidationAccessBatchRecoveryStatus.RetryExhausted,
                ValidationAccessPersistenceErrorCodes.RetryExhausted);
            throw new ValidationAccessPersistenceRetryExhaustedException(
                _persistenceAttempts,
                exhausted,
                _lastError);
        }

        private void ValidateAndCanonicalizeInput()
        {
            foreach (var audit in _input)
            {
                if (audit.AccessEventId == Guid.Empty)
                {
                    throw new ValidationAccessInputBatchConflictException(
                        Guid.Empty,
                        Array.Empty<string>(),
                        new[] { nameof(ValidationCandleAccessAudit.AccessEventId) },
                        BuildInputConflictResult(Guid.Empty));
                }

                var computed = _repo._canonicalizer.ComputeSha256(audit);
                if (audit.AccessPayloadHash is not null
                    && !string.Equals(audit.AccessPayloadHash, computed, StringComparison.Ordinal))
                {
                    throw new ValidationAccessInputBatchConflictException(
                        audit.AccessEventId,
                        new[] { audit.AccessPayloadHash, computed },
                        new[] { nameof(ValidationCandleAccessAudit.AccessPayloadHash) },
                        BuildInputConflictResult(audit.AccessEventId));
                }

                if (audit.AccessPayloadContractVersion is not null
                    && !ValidationAccessPayloadContractVersions.IsSupported(audit.AccessPayloadContractVersion))
                {
                    throw new ValidationAccessInputBatchConflictException(
                        audit.AccessEventId,
                        new[] { computed },
                        new[] { nameof(ValidationCandleAccessAudit.AccessPayloadContractVersion) },
                        BuildInputConflictResult(audit.AccessEventId));
                }

                audit.AccessPayloadHash = computed;
                audit.AccessPayloadContractVersion ??= _repo._canonicalizer.ContractVersion;
            }

            foreach (var group in _input.GroupBy(a => a.AccessEventId))
            {
                var occurrences = group.ToList();
                var first = occurrences[0];
                if (occurrences.Count > 1)
                {
                    var hashes = occurrences.Select(o => o.AccessPayloadHash!).Distinct(StringComparer.Ordinal).ToList();
                    if (hashes.Count > 1)
                    {
                        var conflictingOther = occurrences.First(o =>
                            !string.Equals(o.AccessPayloadHash, first.AccessPayloadHash, StringComparison.Ordinal));
                        var fields = _repo._canonicalizer.GetConflictingFieldNames(first, conflictingOther);
                        throw new ValidationAccessInputBatchConflictException(
                            group.Key,
                            hashes,
                            fields,
                            BuildInputConflictResult(group.Key));
                    }

                    _identicalDuplicates.Add(group.Key);
                }

                _distinct.Add(first);
                _requestedIds.Add(group.Key);
                _requestedHashes[group.Key] = first.AccessPayloadHash!;
            }
        }

        /// <summary>
        /// Phase 2/5 — full payload confirmation of every requested event against durable state,
        /// using the fresh confirmation reader with bounded attempts.
        /// Throws on payload conflict; throws ConfirmationUnavailable when the reader stays unreachable.
        /// </summary>
        private async Task ConfirmDurableStateAsync(CancellationToken cancellationToken)
        {
            var (token, cts) = GetConfirmationToken(cancellationToken);
            try
            {
                IReadOnlyList<ValidationCandleAccessAudit>? rows = null;
                Exception? lastReadError = null;
                for (var attempt = 1; attempt <= _repo._retryPolicy.MaxConfirmationAttempts; attempt++)
                {
                    _confirmationAttempts++;
                    try
                    {
                        rows = await _repo.ReadDurableRowsAsync(_requestedIds, token).ConfigureAwait(false);
                        break;
                    }
                    catch (OperationCanceledException oce)
                    {
                        // Clean caller cancellation is only honored while no commit outcome is pending.
                        if (_commitStatus is ValidationAccessBatchCommitStatus.NotAttempted
                                or ValidationAccessBatchCommitStatus.KnownRolledBack
                            && cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        throw new ValidationAccessConfirmationUnavailableException(
                            BuildResult(
                                ValidationAccessBatchVerificationStatus.ConfirmationUnavailable,
                                ValidationAccessBatchRecoveryStatus.None,
                                ValidationAccessPersistenceErrorCodes.ConfirmationUnavailable),
                            oce);
                    }
                    catch (Exception ex)
                    {
                        lastReadError = ex;
                        if (attempt < _repo._retryPolicy.MaxConfirmationAttempts)
                        {
                            await _repo._retryPolicy.DelayAsync(attempt, SafeDelayToken(token)).ConfigureAwait(false);
                        }
                    }
                }

                if (rows is null)
                {
                    throw new ValidationAccessConfirmationUnavailableException(
                        BuildResult(
                            ValidationAccessBatchVerificationStatus.ConfirmationUnavailable,
                            ValidationAccessBatchRecoveryStatus.None,
                            ValidationAccessPersistenceErrorCodes.ConfirmationUnavailable),
                        lastReadError);
                }

                ClassifyDurableRows(rows);
            }
            finally
            {
                cts?.Dispose();
            }
        }

        private void ClassifyDurableRows(IReadOnlyList<ValidationCandleAccessAudit> rows)
        {
            var byId = rows
                .GroupBy(r => r.AccessEventId)
                .ToDictionary(g => g.Key, g => g.First());

            _confirmedMatching.Clear();
            _confirmedHashes.Clear();
            var missing = new List<Guid>();

            foreach (var requested in _distinct)
            {
                var id = requested.AccessEventId;
                if (!byId.TryGetValue(id, out var persisted))
                {
                    missing.Add(id);
                    continue;
                }

                var requestedHash = _requestedHashes[id];
                if (persisted.AccessPayloadHash is not null)
                {
                    if (!ValidationAccessPayloadContractVersions.IsSupported(persisted.AccessPayloadContractVersion))
                    {
                        ThrowPersistedConflict(
                            id,
                            requestedHash,
                            persisted.AccessPayloadHash,
                            new[] { nameof(ValidationCandleAccessAudit.AccessPayloadContractVersion) });
                    }

                    if (string.Equals(persisted.AccessPayloadHash, requestedHash, StringComparison.Ordinal))
                    {
                        Confirm(id, requestedHash, isLegacy: false);
                        continue;
                    }

                    var fields = _repo._canonicalizer.GetConflictingFieldNames(requested, persisted);
                    if (fields.Count == 0)
                    {
                        fields = new[] { nameof(ValidationCandleAccessAudit.AccessPayloadHash) };
                    }

                    ThrowPersistedConflict(id, requestedHash, persisted.AccessPayloadHash, fields);
                }

                // Historical hashless row: verify by full canonical payload comparison. Never auto-confirm.
                if (_repo._canonicalizer.PayloadEquals(requested, persisted))
                {
                    Confirm(id, _repo._canonicalizer.ComputeSha256(persisted), isLegacy: true);
                    continue;
                }

                var legacyFields = _repo._canonicalizer.GetConflictingFieldNames(requested, persisted);
                ThrowPersistedConflict(id, requestedHash, persistedHash: null, legacyFields);
            }

            _missing = missing;
        }

        private void Confirm(Guid id, string hash, bool isLegacy)
        {
            _confirmedMatching.Add(id);
            _confirmedHashes[id] = hash;
            if (_initialConfirmationDone)
            {
                return;
            }

            if (isLegacy)
            {
                _legacyPayloadVerified.Add(id);
            }
            else
            {
                _existingPayloadVerified.Add(id);
            }
        }

        private void ThrowPersistedConflict(
            Guid id,
            string requestedHash,
            string? persistedHash,
            IReadOnlyList<string> fields)
        {
            var result = BuildResult(
                ValidationAccessBatchVerificationStatus.PayloadConflict,
                ValidationAccessBatchRecoveryStatus.None,
                ValidationAccessPersistenceErrorCodes.PersistedPayloadConflict,
                payloadConflicts: new[] { id });
            throw new ValidationAccessPersistedPayloadConflictException(
                id,
                requestedHash,
                persistedHash,
                fields,
                result);
        }

        private sealed record WriteOutcome(bool Retryable);

        /// <summary>
        /// Phase 3/4 — persist only currently-missing rows in a fresh write context and classify
        /// the commit outcome. A commit exception is never automatically treated as rollback.
        /// </summary>
        private async Task<WriteOutcome> TryWriteMissingRowsAsync(CancellationToken cancellationToken)
        {
            var toWrite = _distinct.Where(a => _missing.Contains(a.AccessEventId)).ToList();
            foreach (var id in _missing.Where(id => !_attempted.Contains(id)))
            {
                _attempted.Add(id);
            }

            var (writeDb, writeLifetime) = _repo.CreateWriteContext();
            var commitAttempted = false;
            try
            {
                await using var tx = await writeDb.Database.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    foreach (var audit in toWrite)
                    {
                        await UpsertAuditRowAsync(writeDb, audit, cancellationToken).ConfigureAwait(false);
                    }

                    commitAttempted = true;
                    await _repo._transactionBoundary.CommitAsync(tx, cancellationToken).ConfigureAwait(false);
                    _commitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded;
                    return new WriteOutcome(Retryable: true);
                }
                catch (ValidationAccessCommitOutcomeUnknownException ex)
                {
                    // Commit may have reached the server: never assume rollback; verify durable state.
                    _commitStatus = ValidationAccessBatchCommitStatus.CommitOutcomeUnknown;
                    _lastError = ex;
                    return new WriteOutcome(Retryable: true);
                }
                catch (OperationCanceledException oce)
                {
                    if (commitAttempted)
                    {
                        _commitStatus = ValidationAccessBatchCommitStatus.CommitOutcomeUnknown;
                        _lastError = oce;
                        return new WriteOutcome(Retryable: true);
                    }

                    await TryRollbackAsync(tx).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    _lastError = ex;
                    var rolledBack = await TryRollbackAsync(tx).ConfigureAwait(false);
                    if (rolledBack)
                    {
                        // A rollback only succeeds while the transaction is still active, which proves
                        // the commit did not complete on the server.
                        _commitStatus = ValidationAccessBatchCommitStatus.KnownRolledBack;
                        return new WriteOutcome(Retryable: _repo._retryPolicy.IsRetryEligible(ex));
                    }

                    // Rollback failed: the commit may have reached the server. Never assume rollback.
                    _commitStatus = ValidationAccessBatchCommitStatus.CommitOutcomeUnknown;
                    return new WriteOutcome(Retryable: true);
                }
            }
            finally
            {
                if (writeLifetime is not null)
                {
                    await writeLifetime.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task<bool> TryRollbackAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
        {
            try
            {
                // Rollback goes through the same boundary seam as commit so that transaction
                // lifetime is observable in one place. Never bound to a cancelled caller token.
                await _repo._transactionBoundary.RollbackAsync(tx, CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Confirmation after an ambiguous commit must be bounded and must not hang on an
        /// already-cancelled caller token.
        /// </summary>
        private (CancellationToken Token, CancellationTokenSource? Source) GetConfirmationToken(
            CancellationToken cancellationToken)
        {
            if (_commitStatus != ValidationAccessBatchCommitStatus.CommitOutcomeUnknown)
            {
                return (cancellationToken, null);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                var recovery = new CancellationTokenSource(_repo._retryPolicy.RecoveryConfirmationTimeout);
                return (recovery.Token, recovery);
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_repo._retryPolicy.RecoveryConfirmationTimeout);
            return (linked.Token, linked);
        }

        private static CancellationToken SafeDelayToken(CancellationToken token) =>
            token.IsCancellationRequested ? CancellationToken.None : token;

        private ValidationAccessBatchPersistResult BuildSuccessResult()
        {
            var recovery = _commitStatus switch
            {
                ValidationAccessBatchCommitStatus.NotAttempted => ValidationAccessBatchRecoveryStatus.None,
                ValidationAccessBatchCommitStatus.CommitOutcomeUnknown =>
                    ValidationAccessBatchRecoveryStatus.ConfirmedAfterAmbiguousCommit,
                _ => _persistenceAttempts > 1
                    ? ValidationAccessBatchRecoveryStatus.MissingEventsRetriedAndConfirmed
                    : ValidationAccessBatchRecoveryStatus.ConfirmedAfterNormalCommit
            };

            var result = BuildResult(
                ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
                recovery,
                lastSafeErrorCode: null);

            if (!result.IsFullyConfirmed)
            {
                throw new ValidationAccessEvidencePersistenceException(result);
            }

            return result;
        }

        private ValidationAccessBatchPersistResult BuildInputConflictResult(Guid conflictingId) => new()
        {
            RequestedEventIds = _input.Select(a => a.AccessEventId).Distinct().ToList(),
            InputConflictEventIds = new[] { conflictingId },
            CommitStatus = ValidationAccessBatchCommitStatus.NotAttempted,
            VerificationStatus = ValidationAccessBatchVerificationStatus.InputConflict,
            RecoveryStatus = ValidationAccessBatchRecoveryStatus.None,
            LastSafeErrorCode = ValidationAccessPersistenceErrorCodes.InputBatchConflict,
            UsedFreshConfirmationContext = _repo.UsesFreshConfirmationContext,
            CompletedAtUtc = DateTime.UtcNow
        };

        private ValidationAccessBatchPersistResult BuildResult(
            ValidationAccessBatchVerificationStatus verification,
            ValidationAccessBatchRecoveryStatus recovery,
            string? lastSafeErrorCode,
            IReadOnlyList<Guid>? payloadConflicts = null) => new()
        {
            RequestedEventIds = _requestedIds.ToList(),
            IdenticalInputDuplicateEventIds = _identicalDuplicates.ToList(),
            AttemptedEventIds = _attempted.ToList(),
            ExistingPayloadVerifiedEventIds = _existingPayloadVerified.ToList(),
            LegacyPayloadVerifiedEventIds = _legacyPayloadVerified.ToList(),
            NewlyInsertedEventIds = _requestedIds.Where(id => !_initialExisting.Contains(id)
                && _confirmedMatching.Contains(id)).ToList(),
            AlreadyExistingEventIds = _requestedIds.Where(id => _initialExisting.Contains(id)).ToList(),
            ConfirmedMatchingEventIds = _confirmedMatching.ToList(),
            ConfirmedPayloadHashes = new Dictionary<Guid, string>(_confirmedHashes),
            MissingEventIds = _missing.ToList(),
            PayloadConflictEventIds = payloadConflicts ?? Array.Empty<Guid>(),
            InputConflictEventIds = Array.Empty<Guid>(),
            CommitStatus = _commitStatus,
            VerificationStatus = verification,
            RecoveryStatus = recovery,
            PersistenceAttemptCount = _persistenceAttempts,
            ConfirmationAttemptCount = _confirmationAttempts,
            UsedFreshConfirmationContext = _repo.UsesFreshConfirmationContext,
            LastSafeErrorCode = lastSafeErrorCode,
            CompletedAtUtc = DateTime.UtcNow
        };

        private static async Task UpsertAuditRowAsync(
            MomoQuantDbContext db,
            ValidationCandleAccessAudit audit,
            CancellationToken cancellationToken)
        {
            // MySQL-safe idempotent upsert: duplicate AccessEventId is a no-op update that never
            // modifies immutable payload columns. A duplicate is only treated as success after
            // mandatory payload confirmation (Phase 5).
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO ValidationCandleAccessAudits
                 (
                     AccessEventId, ScopeExecutionId, ScopeSequenceNumber, ValidationExperimentId,
                     TrialId, TrialNumber, CallerComponent, AccessPurpose,
                     RequestedStartUtc, RequestedEndUtc, RequestedCandleCount,
                     ReturnedStartUtc, ReturnedEndUtc, ReturnedCandleCount,
                     MinimumReturnedTimestampUtc, MaximumReturnedTimestampUtc,
                     CandleContentFingerprint, AccessedAtUtc, WasDenied, DenialCode, DenialReason,
                     CorrelationId, DatasetPartition, AccessPayloadHash, AccessPayloadContractVersion,
                     FlushAttemptCount, PersistedAtUtc, RecorderVersion, CreatedAtUtc
                 )
                 VALUES
                 (
                     {audit.AccessEventId}, {audit.ScopeExecutionId}, {audit.ScopeSequenceNumber}, {audit.ValidationExperimentId},
                     {audit.TrialId}, {audit.TrialNumber}, {audit.CallerComponent}, {audit.AccessPurpose},
                     {audit.RequestedStartUtc}, {audit.RequestedEndUtc}, {audit.RequestedCandleCount},
                     {audit.ReturnedStartUtc}, {audit.ReturnedEndUtc}, {audit.ReturnedCandleCount},
                     {audit.MinimumReturnedTimestampUtc}, {audit.MaximumReturnedTimestampUtc},
                     {audit.CandleContentFingerprint}, {audit.AccessedAtUtc}, {audit.WasDenied}, {audit.DenialCode}, {audit.DenialReason},
                     {audit.CorrelationId}, {audit.DatasetPartition}, {audit.AccessPayloadHash}, {audit.AccessPayloadContractVersion},
                     {audit.FlushAttemptCount}, {audit.PersistedAtUtc}, {audit.RecorderVersion}, {audit.CreatedAtUtc}
                 )
                 ON DUPLICATE KEY UPDATE AccessEventId = AccessEventId
                 """,
                cancellationToken).ConfigureAwait(false);
        }
    }
}


