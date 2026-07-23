using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Risk;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class RiskProfileRepository : IRiskProfileRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public RiskProfileRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RiskProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.RiskProfiles.AsNoTracking()
            .OrderBy(profile => profile.Name)
            .ToListAsync(cancellationToken);

    public Task<RiskProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.RiskProfiles.AsNoTracking().FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);

    public Task<RiskProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        _dbContext.RiskProfiles.AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Name == name, cancellationToken);

    public Task<RiskProfile?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.RiskProfiles.FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);

    public Task AddAsync(RiskProfile profile, CancellationToken cancellationToken = default)
    {
        _dbContext.RiskProfiles.Add(profile);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RiskProfile profile, CancellationToken cancellationToken = default)
    {
        _dbContext.RiskProfiles.Update(profile);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class RiskRuleRepository : IRiskRuleRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public RiskRuleRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RiskRule>> GetByProfileIdAsync(long profileId, CancellationToken cancellationToken = default) =>
        await _dbContext.RiskRules.AsNoTracking()
            .Where(rule => rule.RiskProfileId == profileId)
            .OrderBy(rule => rule.RuleKey)
            .ToListAsync(cancellationToken);

    public Task<RiskRule?> GetByKeyAsync(long profileId, string ruleKey, CancellationToken cancellationToken = default) =>
        _dbContext.RiskRules.FirstOrDefaultAsync(
            rule => rule.RiskProfileId == profileId && rule.RuleKey == ruleKey,
            cancellationToken);

    public Task AddAsync(RiskRule rule, CancellationToken cancellationToken = default)
    {
        _dbContext.RiskRules.Add(rule);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RiskRule rule, CancellationToken cancellationToken = default)
    {
        _dbContext.RiskRules.Update(rule);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class RiskDecisionRepository : IRiskDecisionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public RiskDecisionRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<RiskDecision?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.RiskDecisions.AsNoTracking().FirstOrDefaultAsync(decision => decision.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<RiskDecision> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.RiskDecisions.AsNoTracking();
        if (symbolId.HasValue)
        {
            query = query.Where(decision => decision.SymbolId == symbolId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(decision => decision.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(RiskDecision decision, CancellationToken cancellationToken = default)
    {
        _dbContext.RiskDecisions.Add(decision);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<RiskDecision>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        await _dbContext.RiskDecisions.AsNoTracking()
            .Where(decision => decision.TradingSessionId == tradingSessionId)
            .OrderBy(decision => decision.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
