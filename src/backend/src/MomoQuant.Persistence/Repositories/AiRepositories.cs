using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Signals;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class AiDecisionRepository : IAiDecisionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public AiDecisionRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AiDecision?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.AiDecisions.AsNoTracking().FirstOrDefaultAsync(decision => decision.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AiDecision> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.AiDecisions.AsNoTracking();
        if (symbolId.HasValue)
        {
            query = query.Where(decision => decision.SymbolId == symbolId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(decision => decision.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(AiDecision decision, CancellationToken cancellationToken = default)
    {
        _dbContext.AiDecisions.Add(decision);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AiDecision>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        await _dbContext.AiDecisions.AsNoTracking()
            .Where(decision => decision.TradingSessionId == tradingSessionId)
            .OrderBy(decision => decision.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class StrategySignalRepository : IStrategySignalRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategySignalRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<StrategySignal?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.StrategySignals.AsNoTracking().FirstOrDefaultAsync(signal => signal.Id == id, cancellationToken);

    public Task AddAsync(StrategySignal signal, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategySignals.Add(signal);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyCollection<StrategySignal> signals, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategySignals.AddRange(signals);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<StrategySignal>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        await _dbContext.StrategySignals.AsNoTracking()
            .Where(signal => signal.TradingSessionId == tradingSessionId)
            .OrderBy(signal => signal.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
