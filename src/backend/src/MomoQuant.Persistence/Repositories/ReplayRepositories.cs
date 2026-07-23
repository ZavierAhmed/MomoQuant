using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Replay;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class ReplaySessionRepository : IReplaySessionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public ReplaySessionRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ReplaySession?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.ReplaySessions.AsNoTracking().FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<ReplaySession> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.ReplaySessions.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(session => session.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(ReplaySession session, CancellationToken cancellationToken = default)
    {
        _dbContext.ReplaySessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ReplaySession session, CancellationToken cancellationToken = default)
    {
        if (session.Id > 0)
        {
            var tracked = _dbContext.ChangeTracker.Entries<ReplaySession>()
                .Select(entry => entry.Entity)
                .FirstOrDefault(existing => existing.Id == session.Id);

            if (tracked is not null && !ReferenceEquals(tracked, session))
            {
                _dbContext.Entry(tracked).CurrentValues.SetValues(session);
                return Task.CompletedTask;
            }
        }

        _dbContext.ReplaySessions.Update(session);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class ReplayFrameRepository : IReplayFrameRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public ReplayFrameRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ReplayFrame?> GetBySessionAndIndexAsync(long replaySessionId, int frameIndex, CancellationToken cancellationToken = default) =>
        _dbContext.ReplayFrames.AsNoTracking()
            .FirstOrDefaultAsync(frame => frame.ReplaySessionId == replaySessionId && frame.FrameIndex == frameIndex, cancellationToken);

    public async Task<IReadOnlyList<ReplayFrame>> GetBySessionIdAsync(long replaySessionId, CancellationToken cancellationToken = default) =>
        await _dbContext.ReplayFrames.AsNoTracking()
            .Where(frame => frame.ReplaySessionId == replaySessionId)
            .OrderBy(frame => frame.FrameIndex)
            .ToListAsync(cancellationToken);

    public Task AddAsync(ReplayFrame frame, CancellationToken cancellationToken = default)
    {
        _dbContext.ReplayFrames.Add(frame);
        return Task.CompletedTask;
    }

    public Task<ReplayFrame?> GetTrackedBySessionAndIndexAsync(long replaySessionId, int frameIndex, CancellationToken cancellationToken = default) =>
        _dbContext.ReplayFrames
            .FirstOrDefaultAsync(
                item => item.ReplaySessionId == replaySessionId && item.FrameIndex == frameIndex,
                cancellationToken);

    public async Task DeleteAfterFrameIndexAsync(long replaySessionId, int frameIndex, CancellationToken cancellationToken = default)
    {
        var frames = await _dbContext.ReplayFrames
            .Where(item => item.ReplaySessionId == replaySessionId && item.FrameIndex > frameIndex)
            .ToListAsync(cancellationToken);

        if (frames.Count > 0)
        {
            _dbContext.ReplayFrames.RemoveRange(frames);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
