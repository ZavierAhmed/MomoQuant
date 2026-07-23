using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Persistence.Repositories;

public sealed class SkLivePaperSessionRepository : ISkLivePaperSessionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SkLivePaperSessionRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<SkLivePaperSession?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.SkLivePaperSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SkLivePaperSession>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperSessions
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(limit <= 0 ? 50 : limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<long>> GetRunningSessionIdsAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperSessions
            .Where(s => s.Status == SkLivePaperSessionStatus.Running)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SkLivePaperSession session, CancellationToken cancellationToken = default)
    {
        await _dbContext.SkLivePaperSessions.AddAsync(session, cancellationToken);
    }

    public Task UpdateAsync(SkLivePaperSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.SkLivePaperSessions.Update(session);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class SkLivePaperCandidateRepository : ISkLivePaperCandidateRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SkLivePaperCandidateRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<SkLivePaperCandidate>> GetBySessionAsync(long sessionId, int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperCandidates
            .Where(c => c.SessionId == sessionId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(limit <= 0 ? 100 : limit)
            .ToListAsync(cancellationToken);

    public Task<SkLivePaperCandidate?> GetByKeyAsync(long sessionId, string candidateKey, CancellationToken cancellationToken = default) =>
        _dbContext.SkLivePaperCandidates
            .Where(c => c.SessionId == sessionId && c.CandidateKey == candidateKey)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(SkLivePaperCandidate candidate, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperCandidates.AddAsync(candidate, cancellationToken);

    public Task UpdateAsync(SkLivePaperCandidate candidate, CancellationToken cancellationToken = default)
    {
        _dbContext.SkLivePaperCandidates.Update(candidate);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class SkLivePaperTradeRepository : ISkLivePaperTradeRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SkLivePaperTradeRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<SkLivePaperTrade>> GetBySessionAsync(long sessionId, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperTrades
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SkLivePaperTrade>> GetOpenBySessionAsync(long sessionId, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperTrades
            .Where(t => t.SessionId == sessionId && t.Status == SkLivePaperTradeStatus.Open)
            .ToListAsync(cancellationToken);

    public Task<int> CountOpenedTodayAsync(long sessionId, DateTime dayUtc, CancellationToken cancellationToken = default)
    {
        var start = dayUtc.Date;
        var end = start.AddDays(1);
        return _dbContext.SkLivePaperTrades.CountAsync(
            t => t.SessionId == sessionId && t.EntryTimeUtc >= start && t.EntryTimeUtc < end,
            cancellationToken);
    }

    public async Task AddAsync(SkLivePaperTrade trade, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperTrades.AddAsync(trade, cancellationToken);

    public Task UpdateAsync(SkLivePaperTrade trade, CancellationToken cancellationToken = default)
    {
        _dbContext.SkLivePaperTrades.Update(trade);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class SkLivePaperEventRepository : ISkLivePaperEventRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SkLivePaperEventRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<SkLivePaperEvent>> GetBySessionAsync(long sessionId, int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperEvents
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit <= 0 ? 200 : limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SkLivePaperEvent evt, CancellationToken cancellationToken = default) =>
        await _dbContext.SkLivePaperEvents.AddAsync(evt, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
