using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Repositories;

public sealed class StrategyRepository : IStrategyRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Strategy>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Strategies.AsNoTracking()
            .OrderBy(strategy => strategy.Name)
            .ToListAsync(cancellationToken);

    public Task<Strategy?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Strategies.AsNoTracking().FirstOrDefaultAsync(strategy => strategy.Id == id, cancellationToken);

    public Task<Strategy?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Strategies.FirstOrDefaultAsync(strategy => strategy.Id == id, cancellationToken);

    public Task<Strategy?> GetByCodeAsync(StrategyCode code, CancellationToken cancellationToken = default) =>
        _dbContext.Strategies.AsNoTracking().FirstOrDefaultAsync(strategy => strategy.Code == code, cancellationToken);

    public Task UpdateAsync(Strategy strategy, CancellationToken cancellationToken = default)
    {
        _dbContext.Strategies.Update(strategy);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
