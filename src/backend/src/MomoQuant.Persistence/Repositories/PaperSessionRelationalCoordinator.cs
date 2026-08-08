using System.Data;
using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Persistence.Repositories;

public sealed class PaperSessionRelationalCoordinator : IPaperSessionRelationalCoordinator
{
    private readonly MomoQuantDbContext _dbContext;

    public PaperSessionRelationalCoordinator(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task<T> ExecuteCreationAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            _dbContext.ChangeTracker.Clear();
            return await action(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        // Pre-transaction validation may have tracked qualification evidence. Clear it only
        // after the authoritative transaction begins so every verifier read reloads the durable
        // row through this scoped context and takes the transaction's MySQL SERIALIZABLE locks.
        _dbContext.ChangeTracker.Clear();
        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            if (result is ITransactionResult { Succeeded: false })
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return result;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return result;
        }
        catch (DbUpdateException ex) when (HasPendingAuditEvidence())
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw AuditEvidenceException.Unavailable(ex);
        }
        catch
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<T> ExecuteSerializedAsync<T>(
        long paperSessionId,
        Func<PaperTradingSession?, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            var existing = await LockSessionAsync(paperSessionId, cancellationToken).ConfigureAwait(false);
            return await action(existing, cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var session = await LockSessionAsync(paperSessionId, cancellationToken).ConfigureAwait(false);
            var result = await action(session, cancellationToken).ConfigureAwait(false);
            if (result is ITransactionResult { Succeeded: false })
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return result;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return result;
        }
        catch (DbUpdateException ex) when (HasPendingAuditEvidence())
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw AuditEvidenceException.Unavailable(ex);
        }
        catch
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<PaperTradingSession?> LockSessionAsync(
        long paperSessionId,
        CancellationToken cancellationToken)
    {
        // The caller may already have loaded the session while resolving the request.
        // Detach that snapshot before taking the database lock so EF cannot satisfy the
        // locking query from identity resolution after another transaction has committed.
        foreach (var entry in _dbContext.ChangeTracker
                     .Entries<PaperTradingSession>()
                     .Where(entry => entry.Entity.Id == paperSessionId)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }

        if (_dbContext.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return _dbContext.PaperTradingSessions
                .FromSqlInterpolated($"SELECT * FROM `PaperTradingSessions` WHERE `Id` = {paperSessionId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return _dbContext.PaperTradingSessions
            .SingleOrDefaultAsync(session => session.Id == paperSessionId, cancellationToken);
    }

    private bool HasPendingAuditEvidence() =>
        _dbContext.ChangeTracker.Entries<AuditLog>().Any(entry => entry.State == EntityState.Added);

    private static async Task TryRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original transactional failure.
        }
    }
}
