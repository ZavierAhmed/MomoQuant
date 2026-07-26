using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

public interface IValidationCandleAccessAuditRepository
{
    Task AddRangeAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Payload-verified, idempotent persist by <see cref="ValidationCandleAccessAudit.AccessEventId"/>.
    /// Success requires every distinct requested event to be confirmed durable with a matching
    /// canonical immutable payload (ConfirmedMatchingEventIds == RequestedEventIds as sets) using a
    /// fresh confirmation context; otherwise throws a
    /// <see cref="ValidationAccessEvidencePersistenceException"/> subtype.
    /// A commit exception is treated as outcome-unknown and verified — never assumed rolled back.
    /// Duplicate IDs with conflicting payloads fail closed (in-batch: before any database access).
    /// </summary>
    Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);
}
