using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Signals;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IAiDecisionRepository
{
    Task<AiDecision?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AiDecision> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default);

    Task AddAsync(AiDecision decision, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiDecision>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStrategySignalRepository
{
    Task<Domain.Signals.StrategySignal?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategySignal>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task AddAsync(StrategySignal signal, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<StrategySignal> signals, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
