using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Simulation;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class SimulationRunSummaryRepository : ISimulationRunSummaryRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SimulationRunSummaryRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SimulationRunSummary?> GetBySourceAsync(
        SimulationRunSourceType sourceType,
        long sourceId,
        CancellationToken cancellationToken = default) =>
        _dbContext.SimulationRunSummaries
            .FirstOrDefaultAsync(
                summary => summary.SourceType == sourceType && summary.SourceId == sourceId,
                cancellationToken);

    public async Task<(IReadOnlyList<SimulationRunSummary> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        SimulationRunSourceType? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.SimulationRunSummaries.AsNoTracking().AsQueryable();

        if (sourceType.HasValue)
        {
            query = query.Where(summary => summary.SourceType == sourceType.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(summary => summary.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(SimulationRunSummary summary, CancellationToken cancellationToken = default)
    {
        _dbContext.SimulationRunSummaries.Add(summary);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SimulationRunSummary summary, CancellationToken cancellationToken = default)
    {
        _dbContext.SimulationRunSummaries.Update(summary);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
