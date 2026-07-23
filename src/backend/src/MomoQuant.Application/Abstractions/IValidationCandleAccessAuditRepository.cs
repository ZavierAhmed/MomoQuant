using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

public interface IValidationCandleAccessAuditRepository
{
    Task AddRangeAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default);
}
