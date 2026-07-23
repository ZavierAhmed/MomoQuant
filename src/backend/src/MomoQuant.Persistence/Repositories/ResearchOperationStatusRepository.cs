using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Research;

namespace MomoQuant.Persistence.Repositories;

public sealed class ResearchOperationStatusRepository : IResearchOperationStatusRepository
{
    private readonly MomoQuantDbContext _db;

    public ResearchOperationStatusRepository(MomoQuantDbContext db) => _db = db;

    public Task<ResearchOperationStatusEntity?> GetByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        _db.ResearchOperationStatuses
            .FirstOrDefaultAsync(e => e.OperationId == operationId, cancellationToken);

    public Task<ResearchOperationStatusEntity?> GetByEntityAsync(
        string operationType,
        string entityId,
        CancellationToken cancellationToken = default) =>
        _db.ResearchOperationStatuses
            .FirstOrDefaultAsync(
                e => e.OperationType == operationType && e.EntityId == entityId,
                cancellationToken);

    public async Task AddAsync(ResearchOperationStatusEntity entity, CancellationToken cancellationToken = default)
    {
        await _db.ResearchOperationStatuses.AddAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(ResearchOperationStatusEntity entity, CancellationToken cancellationToken = default)
    {
        _db.ResearchOperationStatuses.Update(entity);
        return Task.CompletedTask;
    }

    public async Task DeleteByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        await _db.ResearchOperationStatuses
            .Where(e => e.CorrelationId == correlationId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
