using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Repositories;

public sealed class StrategyParameterRepository : IStrategyParameterRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyParameterRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<StrategyParameter>> GetByStrategyIdAsync(
        long strategyId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.StrategyParameters.AsNoTracking()
            .Where(parameter => parameter.StrategyId == strategyId)
            .OrderBy(parameter => parameter.ParameterKey)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StrategyParameter>> GetActiveParametersAsync(
        long strategyId,
        Timeframe timeframe,
        long? symbolId,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StrategyParameters.AsNoTracking()
            .Where(parameter =>
                parameter.StrategyId == strategyId &&
                parameter.IsActive &&
                parameter.Timeframe == timeframe &&
                (parameter.SymbolId == symbolId || parameter.SymbolId == null));

        return await query.ToListAsync(cancellationToken);
    }

    public Task<StrategyParameter?> GetByKeyAsync(
        long strategyId,
        string parameterKey,
        Timeframe timeframe,
        long? symbolId,
        CancellationToken cancellationToken = default) =>
        _dbContext.StrategyParameters.FirstOrDefaultAsync(
            parameter =>
                parameter.StrategyId == strategyId &&
                parameter.ParameterKey == parameterKey &&
                parameter.Timeframe == timeframe &&
                parameter.SymbolId == symbolId,
            cancellationToken);

    public Task AddAsync(StrategyParameter parameter, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategyParameters.Add(parameter);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StrategyParameter parameter, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategyParameters.Update(parameter);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
