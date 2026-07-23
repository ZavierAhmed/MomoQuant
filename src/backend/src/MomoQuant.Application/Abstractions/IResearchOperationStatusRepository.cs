using MomoQuant.Domain.Research;

namespace MomoQuant.Application.Abstractions;

public interface IResearchOperationStatusRepository
{
    Task<ResearchOperationStatusEntity?> GetByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<ResearchOperationStatusEntity?> GetByEntityAsync(
        string operationType,
        string entityId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ResearchOperationStatusEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(ResearchOperationStatusEntity entity, CancellationToken cancellationToken = default);

    Task DeleteByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
