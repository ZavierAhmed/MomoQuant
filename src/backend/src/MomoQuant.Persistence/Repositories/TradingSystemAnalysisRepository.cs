using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Persistence.Repositories;

public sealed class TradingSystemAnalysisRepository : ITradingSystemAnalysisRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public TradingSystemAnalysisRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TradingSystemAnalysis?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.TradingSystemAnalyses.FirstOrDefaultAsync(analysis => analysis.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TradingSystemAnalysis>> GetRecentAsync(
        string? systemCode,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 200);
        var query = _dbContext.TradingSystemAnalyses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(systemCode))
        {
            query = query.Where(analysis => analysis.SystemCode == systemCode);
        }

        return await query
            .OrderByDescending(analysis => analysis.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(TradingSystemAnalysis analysis, CancellationToken cancellationToken = default)
    {
        _dbContext.TradingSystemAnalyses.Add(analysis);
        return Task.CompletedTask;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.TradingSystemAnalyses
            .FirstOrDefaultAsync(analysis => analysis.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbContext.TradingSystemAnalyses.Remove(entity);
        return true;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
