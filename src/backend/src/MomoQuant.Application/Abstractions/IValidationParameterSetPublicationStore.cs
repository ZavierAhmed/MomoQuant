using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

/// <summary>
/// Relational transaction and locking boundary for authoritative Validation Lab publication.
/// Every lock method is called only from inside <see cref="ExecuteInTransactionAsync{T}"/>.
/// </summary>
public interface IValidationParameterSetPublicationStore
{
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);

    Task<ValidationExperiment?> LockExperimentAsync(long experimentId, CancellationToken cancellationToken = default);
    Task<ValidationParameterTrial?> LockTrialAsync(long trialId, CancellationToken cancellationToken = default);
    Task<Strategy?> LockStrategyByCodeAsync(string strategyCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ValidationExperiment>> ListCanonicalExperimentsAsync(
        string strategyCode,
        CancellationToken cancellationToken = default);
    Task<StrategyParameterSet?> LockPublicationByExperimentAsync(
        long experimentId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyParameterSet>> LockQualifiedPublicationsByStrategyAsync(
        string strategyCode,
        CancellationToken cancellationToken = default);

    void AddParameterSet(StrategyParameterSet parameterSet);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
