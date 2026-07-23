using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Trades;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class PaperAccountRepository : IPaperAccountRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public PaperAccountRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<PaperAccount?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.PaperAccounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<PaperAccount> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PaperAccounts.AsNoTracking().OrderByDescending(account => account.CreatedAtUtc);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default)
    {
        _dbContext.PaperAccounts.Add(account);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PaperAccount account, CancellationToken cancellationToken = default)
    {
        _dbContext.PaperAccounts.Update(account);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class PaperAccountSnapshotRepository : IPaperAccountSnapshotRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public PaperAccountSnapshotRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<PaperAccountSnapshot>> GetByAccountIdAsync(long paperAccountId, CancellationToken cancellationToken = default) =>
        _dbContext.PaperAccountSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.PaperAccountId == paperAccountId)
            .OrderByDescending(snapshot => snapshot.TimestampUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<PaperAccountSnapshot>)task.Result, cancellationToken);

    public Task AddAsync(PaperAccountSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _dbContext.PaperAccountSnapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class PaperTradingSessionRepository : IPaperTradingSessionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public PaperTradingSessionRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<PaperTradingSession?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.PaperTradingSessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<PaperTradingSession> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PaperTradingSessions.AsNoTracking().OrderByDescending(session => session.CreatedAtUtc);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(PaperTradingSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.PaperTradingSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PaperTradingSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.PaperTradingSessions.Update(session);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<long>> GetRunningSessionIdsAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.PaperTradingSessions.AsNoTracking()
            .Where(session =>
                session.Status == PaperSessionStatus.Running &&
                session.Mode == PaperTradingMode.HistoricalPaper)
            .Select(session => session.Id)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class PositionRepository : IPositionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public PositionRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<Position>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.Positions.AsNoTracking()
            .Where(position => position.TradingSessionId == tradingSessionId)
            .OrderByDescending(position => position.OpenedAtUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<Position>)task.Result, cancellationToken);

    public Task<IReadOnlyList<Position>> GetTrackedByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.Positions
            .Where(position => position.TradingSessionId == tradingSessionId)
            .OrderByDescending(position => position.OpenedAtUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<Position>)task.Result, cancellationToken);

    public Task AddAsync(Position position, CancellationToken cancellationToken = default)
    {
        _dbContext.Positions.Add(position);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Position position, CancellationToken cancellationToken = default)
    {
        if (position.Id > 0)
        {
            var tracked = _dbContext.ChangeTracker.Entries<Position>()
                .Select(entry => entry.Entity)
                .FirstOrDefault(existing => existing.Id == position.Id);

            if (tracked is not null && !ReferenceEquals(tracked, position))
            {
                _dbContext.Entry(tracked).CurrentValues.SetValues(position);
                return Task.CompletedTask;
            }
        }

        _dbContext.Positions.Update(position);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
