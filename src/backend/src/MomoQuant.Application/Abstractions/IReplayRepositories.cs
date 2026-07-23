using MomoQuant.Domain.Replay;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IReplaySessionRepository
{
    Task<ReplaySession?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ReplaySession> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default);

    Task AddAsync(ReplaySession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(ReplaySession session, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IReplayFrameRepository
{
    Task<ReplayFrame?> GetBySessionAndIndexAsync(long replaySessionId, int frameIndex, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReplayFrame>> GetBySessionIdAsync(long replaySessionId, CancellationToken cancellationToken = default);

    Task AddAsync(ReplayFrame frame, CancellationToken cancellationToken = default);

    Task<ReplayFrame?> GetTrackedBySessionAndIndexAsync(long replaySessionId, int frameIndex, CancellationToken cancellationToken = default);

    Task DeleteAfterFrameIndexAsync(long replaySessionId, int frameIndex, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
