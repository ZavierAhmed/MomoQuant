using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Application.Abstractions;

public interface ISkLivePaperSessionRepository
{
    Task<SkLivePaperSession?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkLivePaperSession>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> GetRunningSessionIdsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SkLivePaperSession session, CancellationToken cancellationToken = default);
    Task UpdateAsync(SkLivePaperSession session, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISkLivePaperCandidateRepository
{
    Task<IReadOnlyList<SkLivePaperCandidate>> GetBySessionAsync(long sessionId, int limit, CancellationToken cancellationToken = default);
    Task<SkLivePaperCandidate?> GetByKeyAsync(long sessionId, string candidateKey, CancellationToken cancellationToken = default);
    Task AddAsync(SkLivePaperCandidate candidate, CancellationToken cancellationToken = default);
    Task UpdateAsync(SkLivePaperCandidate candidate, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISkLivePaperTradeRepository
{
    Task<IReadOnlyList<SkLivePaperTrade>> GetBySessionAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkLivePaperTrade>> GetOpenBySessionAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<int> CountOpenedTodayAsync(long sessionId, DateTime dayUtc, CancellationToken cancellationToken = default);
    Task AddAsync(SkLivePaperTrade trade, CancellationToken cancellationToken = default);
    Task UpdateAsync(SkLivePaperTrade trade, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISkLivePaperEventRepository
{
    Task<IReadOnlyList<SkLivePaperEvent>> GetBySessionAsync(long sessionId, int limit, CancellationToken cancellationToken = default);
    Task AddAsync(SkLivePaperEvent evt, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
