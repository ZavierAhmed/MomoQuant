using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Optimization;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Repositories;

public sealed class StrategyParameterSetRepository : IStrategyParameterSetRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyParameterSetRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<StrategyParameterSet?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.StrategyParameterSets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<StrategyParameterSet?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.StrategyParameterSets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StrategyParameterSet>> ListAsync(
        string? strategyCode,
        long? symbolId,
        string? timeframe,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StrategyParameterSets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(strategyCode))
        {
            query = query.Where(x => x.StrategyCode == strategyCode);
        }

        if (symbolId.HasValue)
        {
            query = query.Where(x => x.SymbolId == symbolId || x.SymbolId == null);
        }

        if (!string.IsNullOrWhiteSpace(timeframe))
        {
            query = query.Where(x => x.Timeframe == timeframe);
        }

        return await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public Task AddAsync(StrategyParameterSet entity, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategyParameterSets.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StrategyParameterSet entity, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategyParameterSets.Update(entity);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class ParameterOptimizationRunRepository : IParameterOptimizationRunRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public ParameterOptimizationRunRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<ParameterOptimizationRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.ParameterOptimizationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task AddAsync(ParameterOptimizationRun entity, CancellationToken cancellationToken = default)
    {
        _dbContext.ParameterOptimizationRuns.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ParameterOptimizationRun entity, CancellationToken cancellationToken = default)
    {
        _dbContext.ParameterOptimizationRuns.Update(entity);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class TargetOptimizationRunRepository : ITargetOptimizationRunRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public TargetOptimizationRunRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<TargetOptimizationRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.TargetOptimizationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task AddAsync(TargetOptimizationRun entity, CancellationToken cancellationToken = default)
    {
        _dbContext.TargetOptimizationRuns.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TargetOptimizationRun entity, CancellationToken cancellationToken = default)
    {
        _dbContext.TargetOptimizationRuns.Update(entity);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
