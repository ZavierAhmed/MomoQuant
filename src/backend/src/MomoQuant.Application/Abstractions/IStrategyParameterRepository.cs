using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Abstractions;

public interface IStrategyParameterRepository
{
    Task<IReadOnlyList<StrategyParameter>> GetByStrategyIdAsync(
        long strategyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyParameter>> GetActiveParametersAsync(
        long strategyId,
        Timeframe timeframe,
        long? symbolId,
        CancellationToken cancellationToken = default);

    Task<StrategyParameter?> GetByKeyAsync(
        long strategyId,
        string parameterKey,
        Timeframe timeframe,
        long? symbolId,
        CancellationToken cancellationToken = default);

    Task AddAsync(StrategyParameter parameter, CancellationToken cancellationToken = default);

    Task UpdateAsync(StrategyParameter parameter, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
