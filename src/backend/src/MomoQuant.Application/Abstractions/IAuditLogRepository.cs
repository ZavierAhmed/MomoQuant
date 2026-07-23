using MomoQuant.Application.Audit.Dtos;
using MomoQuant.Domain.Audit;

namespace MomoQuant.Application.Abstractions;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
    Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        AuditLogQueryFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Action, int Count)>> GetActionCountsAsync(
        AuditLogQueryFilter filter,
        int top,
        CancellationToken cancellationToken = default);
    Task<int> CountAsync(AuditLogQueryFilter filter, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
