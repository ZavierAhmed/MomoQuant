using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit.Dtos;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public AuditLogRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);
    }

    public Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.AuditLogs.AsNoTracking().FirstOrDefaultAsync(log => log.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        AuditLogQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(filter);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(log => log.CreatedAtUtc)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<(string Action, int Count)>> GetActionCountsAsync(
        AuditLogQueryFilter filter,
        int top,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(filter);
        var results = await query
            .GroupBy(log => log.Action)
            .Select(group => new { Action = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .Take(top)
            .ToListAsync(cancellationToken);

        return results.Select(item => (item.Action, item.Count)).ToList();
    }

    public Task<int> CountAsync(AuditLogQueryFilter filter, CancellationToken cancellationToken = default) =>
        BuildQuery(filter).CountAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<AuditLog> BuildQuery(AuditLogQueryFilter filter)
    {
        var query = _dbContext.AuditLogs.AsNoTracking()
            .Where(log => log.CreatedAtUtc >= filter.FromUtc && log.CreatedAtUtc <= filter.ToUtc);

        if (filter.Severity.HasValue)
        {
            query = query.Where(log => log.Severity == filter.Severity.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(log => log.Action == filter.Action);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(log => log.UserId == filter.UserId.Value);
        }

        return query;
    }
}
