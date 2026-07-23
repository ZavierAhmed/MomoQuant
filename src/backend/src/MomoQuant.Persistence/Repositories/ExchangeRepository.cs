using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class ExchangeRepository : IExchangeRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public ExchangeRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Exchange?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Exchanges.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Exchange?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        _dbContext.Exchanges.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        long? excludeExchangeId = null,
        CancellationToken cancellationToken = default) =>
        _dbContext.Exchanges.AnyAsync(
            e => e.Code == code && (!excludeExchangeId.HasValue || e.Id != excludeExchangeId.Value),
            cancellationToken);

    public async Task<(IReadOnlyList<Exchange> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.Exchanges.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.Name.ToLower().Contains(search) ||
                e.Code.ToLower().Contains(search));
        }

        query = ApplySorting(query, request);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(Exchange exchange, CancellationToken cancellationToken = default)
    {
        _dbContext.Exchanges.Add(exchange);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Exchange exchange, CancellationToken cancellationToken = default)
    {
        _dbContext.Exchanges.Update(exchange);
        return Task.CompletedTask;
    }

    public async Task<bool> HasBlockingDependenciesAsync(long exchangeId, CancellationToken cancellationToken = default)
    {
        if (await _dbContext.Candles.AnyAsync(candle => candle.ExchangeId == exchangeId, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.MarketDataImports.AnyAsync(import => import.ExchangeId == exchangeId, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.TradingSessions.AnyAsync(session => session.ExchangeId == exchangeId, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.BacktestRuns.AnyAsync(run => run.ExchangeId == exchangeId, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.ReplaySessions.AnyAsync(session => session.ExchangeId == exchangeId, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.PaperTradingSessions.AnyAsync(session => session.ExchangeId == exchangeId, cancellationToken))
        {
            return true;
        }

        var symbolIds = await _dbContext.Symbols.AsNoTracking()
            .Where(symbol => symbol.ExchangeId == exchangeId)
            .Select(symbol => symbol.Id)
            .ToListAsync(cancellationToken);

        if (symbolIds.Count == 0)
        {
            return false;
        }

        if (await _dbContext.IndicatorSnapshots.AnyAsync(snapshot => symbolIds.Contains(snapshot.SymbolId), cancellationToken))
        {
            return true;
        }

        if (await _dbContext.StrategySignals.AnyAsync(signal => symbolIds.Contains(signal.SymbolId), cancellationToken))
        {
            return true;
        }

        if (await _dbContext.Orders.AnyAsync(order => symbolIds.Contains(order.SymbolId), cancellationToken))
        {
            return true;
        }

        if (await _dbContext.Trades.AnyAsync(trade => symbolIds.Contains(trade.SymbolId), cancellationToken))
        {
            return true;
        }

        if (await _dbContext.Positions.AnyAsync(position => symbolIds.Contains(position.SymbolId), cancellationToken))
        {
            return true;
        }

        if (await _dbContext.RiskDecisions.AnyAsync(decision => symbolIds.Contains(decision.SymbolId), cancellationToken))
        {
            return true;
        }

        return false;
    }

    public Task<int> DeleteAsync(long exchangeId, CancellationToken cancellationToken = default) =>
        _dbContext.Exchanges.Where(exchange => exchange.Id == exchangeId).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private static IQueryable<Exchange> ApplySorting(IQueryable<Exchange> query, PagedRequest request)
    {
        var descending = request.SortDirection == SortDirection.Desc;
        return request.SortBy?.ToLowerInvariant() switch
        {
            "name" => descending ? query.OrderByDescending(e => e.Name) : query.OrderBy(e => e.Name),
            "code" => descending ? query.OrderByDescending(e => e.Code) : query.OrderBy(e => e.Code),
            "isactive" => descending ? query.OrderByDescending(e => e.IsActive) : query.OrderBy(e => e.IsActive),
            _ => descending ? query.OrderByDescending(e => e.CreatedAtUtc) : query.OrderBy(e => e.CreatedAtUtc)
        };
    }
}
