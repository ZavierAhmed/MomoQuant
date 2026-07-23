using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Abstractions;

public interface IStrategyRepository
{
    Task<IReadOnlyList<Strategy>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Strategy?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Strategy?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default);
    Task<Strategy?> GetByCodeAsync(StrategyCode code, CancellationToken cancellationToken = default);
    Task UpdateAsync(Strategy strategy, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
