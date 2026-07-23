using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Persistence.Repositories;

public sealed class MarketDataImportRepository : IMarketDataImportRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public MarketDataImportRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<MarketDataImport?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.MarketDataImports.AsNoTracking()
            .FirstOrDefaultAsync(import => import.Id == id, cancellationToken);

    public Task AddAsync(MarketDataImport import, CancellationToken cancellationToken = default)
    {
        _dbContext.MarketDataImports.Add(import);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(MarketDataImport import, CancellationToken cancellationToken = default)
    {
        _dbContext.MarketDataImports.Update(import);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MarketDataImport>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 100);
        return await _dbContext.MarketDataImports.AsNoTracking()
            .OrderByDescending(import => import.StartedAtUtc)
            .Take(effectiveLimit)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
