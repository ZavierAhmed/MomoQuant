using MomoQuant.Domain.PaperTrading;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IPaperAccountRepository
{
    Task<PaperAccount?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PaperAccount> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default);

    Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default);

    Task UpdateAsync(PaperAccount account, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPaperAccountSnapshotRepository
{
    Task<IReadOnlyList<PaperAccountSnapshot>> GetByAccountIdAsync(long paperAccountId, CancellationToken cancellationToken = default);

    Task AddAsync(PaperAccountSnapshot snapshot, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPaperTradingSessionRepository
{
    Task<PaperTradingSession?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PaperTradingSession> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default);

    Task AddAsync(PaperTradingSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(PaperTradingSession session, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<long>> GetRunningSessionIdsAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPositionRepository
{
    Task<IReadOnlyList<Domain.Trades.Position>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Trades.Position>> GetTrackedByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task AddAsync(Domain.Trades.Position position, CancellationToken cancellationToken = default);

    Task UpdateAsync(Domain.Trades.Position position, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
