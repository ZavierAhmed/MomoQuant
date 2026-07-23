using MomoQuant.Domain.Risk;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IRiskProfileRepository
{
    Task<IReadOnlyList<RiskProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<RiskProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<RiskProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<RiskProfile?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(RiskProfile profile, CancellationToken cancellationToken = default);

    Task UpdateAsync(RiskProfile profile, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IRiskRuleRepository
{
    Task<IReadOnlyList<RiskRule>> GetByProfileIdAsync(long profileId, CancellationToken cancellationToken = default);

    Task<RiskRule?> GetByKeyAsync(long profileId, string ruleKey, CancellationToken cancellationToken = default);

    Task AddAsync(RiskRule rule, CancellationToken cancellationToken = default);

    Task UpdateAsync(RiskRule rule, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IRiskDecisionRepository
{
    Task<RiskDecision?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<RiskDecision> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default);

    Task AddAsync(RiskDecision decision, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskDecision>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
