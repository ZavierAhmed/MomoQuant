using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Persistence.Repositories;

public sealed class ValidationAuditExecutionRepository : IValidationAuditExecutionRepository
{
    private static readonly ValidationAuditExecutionStatus[] ActiveStatuses =
    [
        ValidationAuditExecutionStatus.Created,
        ValidationAuditExecutionStatus.InProgress,
        ValidationAuditExecutionStatus.FlushManifested,
        ValidationAuditExecutionStatus.EventsConfirmed,
        ValidationAuditExecutionStatus.RecoveryRequired
    ];

    private readonly MomoQuantDbContext _db;

    public ValidationAuditExecutionRepository(MomoQuantDbContext db) => _db = db;

    public Task<ValidationAuditExecution?> GetByAuditExecutionIdAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default) =>
        _db.ValidationAuditExecutions
            .FirstOrDefaultAsync(e => e.AuditExecutionId == auditExecutionId, cancellationToken);

    public async Task<IReadOnlyList<ValidationAuditExecution>> GetByTrialIdAsync(
        long validationTrialId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationAuditExecutions
            .Where(e => e.ValidationTrialId == validationTrialId)
            .OrderBy(e => e.AttemptNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ValidationAuditExecution>> GetActiveByTrialIdAsync(
        long validationTrialId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationAuditExecutions
            .Where(e => e.ValidationTrialId == validationTrialId && ActiveStatuses.Contains(e.Status))
            .OrderBy(e => e.AttemptNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        _db.ValidationAuditExecutions.Add(execution);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        _db.ValidationAuditExecutions.Update(execution);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_CONCURRENCY_CONFLICT",
                $"Optimistic concurrency conflict updating audit execution {execution.AuditExecutionId}.",
                ex);
        }
    }

    public async Task<ValidationAuditExecution> CreateAndAssignTrialAuthoritativeAsync(
        ValidationAuditExecution execution,
        ValidationParameterTrial trial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(trial);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lockedTrialIds = await _db.Database
                .SqlQuery<long>($"""
                    SELECT `Id` AS `Value`
                    FROM `ValidationParameterTrials`
                    WHERE `Id` = {trial.Id}
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (lockedTrialIds.Count != 1)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_TRIAL_MISSING",
                    $"Validation trial {trial.Id} no longer exists.");
            }

            var durableTrial = _db.ValidationParameterTrials.Local
                .SingleOrDefault(candidate => candidate.Id == trial.Id)
                ?? trial;
            if (_db.Entry(durableTrial).State == EntityState.Detached)
            {
                _db.ValidationParameterTrials.Attach(durableTrial);
            }

            await _db.Entry(durableTrial).ReloadAsync(cancellationToken).ConfigureAwait(false);

            var active = await _db.ValidationAuditExecutions
                .Where(e => e.ValidationTrialId == durableTrial.Id && ActiveStatuses.Contains(e.Status))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (active.Count > 0)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_MULTIPLE_ACTIVE_EXECUTIONS",
                    $"Trial {durableTrial.Id} already has {active.Count} active audit execution(s).");
            }

            _db.ValidationAuditExecutions.Add(execution);

            durableTrial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
            durableTrial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
            durableTrial.AuditAttemptNumber = execution.AttemptNumber;
            _db.ValidationParameterTrials.Update(durableTrial);

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return execution;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await _db.Entry(trial).ReloadAsync(cancellationToken).ConfigureAwait(false);
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_CONCURRENCY_CONFLICT",
                $"Concurrency conflict assigning authoritative audit execution for trial {trial.Id}.",
                ex);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}

public sealed class ValidationAuditBatchRepository : IValidationAuditBatchRepository
{
    private readonly MomoQuantDbContext _db;

    public ValidationAuditBatchRepository(MomoQuantDbContext db) => _db = db;

    public Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(
        Guid auditBatchId,
        CancellationToken cancellationToken = default) =>
        _db.ValidationAuditBatches
            .FirstOrDefaultAsync(b => b.AuditBatchId == auditBatchId, cancellationToken);

    public async Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default) =>
        await _db.ValidationAuditBatches
            .Where(b => b.AuditExecutionId == auditExecutionId)
            .OrderBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _db.ValidationAuditBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _db.ValidationAuditBatches.Update(batch);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_CONCURRENCY_CONFLICT",
                $"Optimistic concurrency conflict updating audit batch {batch.AuditBatchId}.",
                ex);
        }
    }

    public async Task<ValidationAuditBatch> GetOrCreateManifestAsync(
        ValidationAuditBatch proposed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposed);

        var existing = await _db.ValidationAuditBatches
            .FirstOrDefaultAsync(
                b => b.AuditExecutionId == proposed.AuditExecutionId
                     && b.BatchNumber == proposed.BatchNumber,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = await _db.ValidationAuditBatches
                .FirstOrDefaultAsync(
                    b => b.AuditExecutionId == proposed.AuditExecutionId
                         && b.ExpectedPayloadSetHash == proposed.ExpectedPayloadSetHash,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (existing is not null)
        {
            if (!string.Equals(
                    existing.ExpectedPayloadSetHash,
                    proposed.ExpectedPayloadSetHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_MANIFEST_CONFLICT",
                    $"Batch {existing.BatchNumber} for execution {existing.AuditExecutionId} already exists with a different payload set hash.");
            }

            if (existing.FirstSequence != proposed.FirstSequence
                || existing.LastSequence != proposed.LastSequence
                || existing.ExpectedEventCount != proposed.ExpectedEventCount)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_MANIFEST_CONFLICT",
                    $"Batch {existing.BatchNumber} sequence/count does not match the proposed retry manifest.");
            }

            existing.PersistenceAttemptCount++;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            if (existing.Status == ValidationAuditBatchStatus.Created)
            {
                existing.Status = ValidationAuditBatchStatus.Persisting;
            }

            existing.RowVersion++;
            await UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            return existing;
        }

        proposed.Status = ValidationAuditBatchStatus.Persisting;
        proposed.PersistenceAttemptCount = Math.Max(1, proposed.PersistenceAttemptCount);
        proposed.CreatedAtUtc = proposed.CreatedAtUtc == default ? DateTime.UtcNow : proposed.CreatedAtUtc;
        proposed.UpdatedAtUtc = DateTime.UtcNow;
        proposed.RowVersion = proposed.RowVersion == 0 ? 1 : proposed.RowVersion;
        await AddAsync(proposed, cancellationToken).ConfigureAwait(false);
        return proposed;
    }
}

public sealed class ValidationAuditUnitOfWork : IValidationAuditUnitOfWork
{
    private readonly MomoQuantDbContext _db;

    public ValidationAuditUnitOfWork(MomoQuantDbContext db) => _db = db;

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_db.Database.CurrentTransaction is not null)
        {
            await action().ConfigureAwait(false);
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
