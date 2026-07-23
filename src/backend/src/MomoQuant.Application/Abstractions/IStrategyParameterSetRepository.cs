using MomoQuant.Domain.Optimization;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Abstractions;

public interface IStrategyParameterSetRepository
{
    Task<StrategyParameterSet?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<StrategyParameterSet?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyParameterSet>> ListAsync(
        string? strategyCode,
        long? symbolId,
        string? timeframe,
        CancellationToken cancellationToken = default);

    Task AddAsync(StrategyParameterSet entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(StrategyParameterSet entity, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IParameterOptimizationRunRepository
{
    Task<ParameterOptimizationRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(ParameterOptimizationRun entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(ParameterOptimizationRun entity, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITargetOptimizationRunRepository
{
    Task<TargetOptimizationRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(TargetOptimizationRun entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(TargetOptimizationRun entity, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
