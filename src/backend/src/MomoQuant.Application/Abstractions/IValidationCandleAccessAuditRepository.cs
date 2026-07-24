using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

public interface IValidationCandleAccessAuditRepository
{
    Task AddRangeAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// MySQL-safe upsert by <see cref="ValidationCandleAccessAudit.AccessEventId"/>, then SELECT-confirms
    /// every requested id. Success requires ConfirmedPersistedEventIds == RequestedEventIds (as sets);
    /// otherwise throws <see cref="ValidationAccessEvidencePersistenceException"/>.
    /// Never treats a duplicate-key rollback as successful persistence of a mixed batch.
    /// </summary>
    Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);
}
