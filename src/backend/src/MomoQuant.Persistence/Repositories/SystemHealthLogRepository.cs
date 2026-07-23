using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Monitoring;

namespace MomoQuant.Persistence.Repositories;

public sealed class SystemHealthLogRepository : ISystemHealthLogRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SystemHealthLogRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SystemHealthLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.SystemHealthLogs.AddAsync(log, cancellationToken);
    }

    public Task<SystemHealthLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.SystemHealthLogs.AsNoTracking().FirstOrDefaultAsync(log => log.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<SystemHealthLog> Items, int TotalCount)> GetPagedAsync(
        MonitoringQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(filter);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(log => log.CheckedAtUtc)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<SystemHealthLog>> GetRecentAsync(
        MonitoringQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        return await BuildQuery(filter)
            .OrderByDescending(log => log.CheckedAtUtc)
            .Take(filter.Limit)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<SystemHealthLog> BuildQuery(MonitoringQueryFilter filter)
    {
        var query = _dbContext.SystemHealthLogs.AsNoTracking()
            .Where(log => log.CheckedAtUtc >= filter.FromUtc && log.CheckedAtUtc <= filter.ToUtc);

        if (filter.Severity.HasValue)
        {
            query = query.Where(log => log.Severity == filter.Severity.Value);
        }

        if (filter.Subsystem.HasValue)
        {
            var subsystem = filter.Subsystem.Value.ToString();
            query = query.Where(log => log.ServiceName == subsystem);
        }

        return query;
    }
}
