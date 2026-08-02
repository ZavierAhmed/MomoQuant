using System.Data;
using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
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
        var result = await action(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
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
        var session = await LockSessionAsync(paperSessionId, cancellationToken).ConfigureAwait(false);
        var result = await action(session, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
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
}
