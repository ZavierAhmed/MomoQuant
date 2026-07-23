using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

public interface IValidationCandleAccessAuditRepository
{
    Task AddRangeAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists audits transactionally. Rows whose <see cref="ValidationCandleAccessAudit.AccessEventId"/>
    /// already exist are skipped (idempotent). Returns the count of newly inserted rows.
    /// </summary>
    Task<int> AddRangeIdempotentByAccessEventIdAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);
}
