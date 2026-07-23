using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class StrategyBenchmarkRunRepository : IStrategyBenchmarkRunRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyBenchmarkRunRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<StrategyBenchmarkRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Set<StrategyBenchmarkRun>().FirstOrDefaultAsync(run => run.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<StrategyBenchmarkRun> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = _dbContext.Set<StrategyBenchmarkRun>().AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(run => run.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(StrategyBenchmarkRun run, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<StrategyBenchmarkRun>().Add(run);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StrategyBenchmarkRun run, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<StrategyBenchmarkRun>().Update(run);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class StrategyBenchmarkResultRepository : IStrategyBenchmarkResultRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyBenchmarkResultRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<StrategyBenchmarkResult>> GetByBenchmarkRunIdAsync(
        long benchmarkRunId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Set<StrategyBenchmarkResult>()
            .AsNoTracking()
            .Where(result => result.BenchmarkRunId == benchmarkRunId)
            .OrderBy(result => result.StrategyCode)
            .ThenBy(result => result.Symbol)
            .ThenBy(result => result.Timeframe)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<StrategyBenchmarkResult>)task.Result, cancellationToken);

    public Task AddAsync(StrategyBenchmarkResult result, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<StrategyBenchmarkResult>().Add(result);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyCollection<StrategyBenchmarkResult> results, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<StrategyBenchmarkResult>().AddRange(results);
        return Task.CompletedTask;
    }

    public async Task DeleteByBenchmarkRunIdAsync(long benchmarkRunId, CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Set<StrategyBenchmarkResult>()
            .Where(result => result.BenchmarkRunId == benchmarkRunId)
            .ToListAsync(cancellationToken);
        _dbContext.Set<StrategyBenchmarkResult>().RemoveRange(items);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class StrategyBenchmarkRunItemRepository : IStrategyBenchmarkRunItemRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyBenchmarkRunItemRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<StrategyBenchmarkRunItem>> GetByBenchmarkRunIdAsync(
        long benchmarkRunId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Set<StrategyBenchmarkRunItem>()
            .Where(item => item.BenchmarkRunId == benchmarkRunId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

    public Task<StrategyBenchmarkRunItem?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Set<StrategyBenchmarkRunItem>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task AddRangeAsync(IReadOnlyCollection<StrategyBenchmarkRunItem> items, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<StrategyBenchmarkRunItem>().AddRange(items);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StrategyBenchmarkRunItem item, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<StrategyBenchmarkRunItem>().Update(item);
        return Task.CompletedTask;
    }

    public async Task DeleteByBenchmarkRunIdAsync(long benchmarkRunId, CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Set<StrategyBenchmarkRunItem>()
            .Where(item => item.BenchmarkRunId == benchmarkRunId)
            .ToListAsync(cancellationToken);
        _dbContext.Set<StrategyBenchmarkRunItem>().RemoveRange(items);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
