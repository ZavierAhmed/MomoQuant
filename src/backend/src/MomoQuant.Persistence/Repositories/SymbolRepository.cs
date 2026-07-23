using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class SymbolRepository : ISymbolRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public SymbolRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Symbol?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Symbols.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Symbol?> GetByExchangeAndNameAsync(
        long exchangeId,
        string symbolName,
        CancellationToken cancellationToken = default) =>
        _dbContext.Symbols.FirstOrDefaultAsync(
            s => s.ExchangeId == exchangeId && s.SymbolName == symbolName,
            cancellationToken);

    public Task<bool> ExistsAsync(
        long exchangeId,
        string symbolName,
        long? excludeSymbolId = null,
        CancellationToken cancellationToken = default) =>
        _dbContext.Symbols.AnyAsync(
            s => s.ExchangeId == exchangeId
                 && s.SymbolName == symbolName
                 && (!excludeSymbolId.HasValue || s.Id != excludeSymbolId.Value),
            cancellationToken);

    public async Task<(IReadOnlyList<Symbol> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        long? exchangeId = null,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.Symbols.AsNoTracking().AsQueryable();

        if (exchangeId.HasValue)
        {
            query = query.Where(s => s.ExchangeId == exchangeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(s =>
                s.SymbolName.Contains(search) ||
                s.BaseAsset.Contains(search) ||
                s.QuoteAsset.Contains(search));
        }

        query = ApplySorting(query, request);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<string>> GetSymbolNamesByExchangeAsync(
        long exchangeId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Symbols.AsNoTracking()
            .Where(symbol => symbol.ExchangeId == exchangeId)
            .Select(symbol => symbol.SymbolName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Symbol>> GetEnabledByExchangeIdAsync(
        long exchangeId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Symbols.AsNoTracking()
            .Where(symbol => symbol.ExchangeId == exchangeId && symbol.IsActive)
            .OrderBy(symbol => symbol.SymbolName)
            .ToListAsync(cancellationToken);

    public Task AddAsync(Symbol symbol, CancellationToken cancellationToken = default)
    {
        _dbContext.Symbols.Add(symbol);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Symbol symbol, CancellationToken cancellationToken = default)
    {
        _dbContext.Symbols.Update(symbol);
        return Task.CompletedTask;
    }

    public Task<int> DeleteByExchangeIdAsync(long exchangeId, CancellationToken cancellationToken = default) =>
        _dbContext.Symbols.Where(symbol => symbol.ExchangeId == exchangeId).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private static IQueryable<Symbol> ApplySorting(IQueryable<Symbol> query, PagedRequest request)
    {
        var descending = request.SortDirection == SortDirection.Desc;
        return request.SortBy?.ToLowerInvariant() switch
        {
            "symbol" => descending ? query.OrderByDescending(s => s.SymbolName) : query.OrderBy(s => s.SymbolName),
            "baseasset" => descending ? query.OrderByDescending(s => s.BaseAsset) : query.OrderBy(s => s.BaseAsset),
            "quoteasset" => descending ? query.OrderByDescending(s => s.QuoteAsset) : query.OrderBy(s => s.QuoteAsset),
            "isactive" => descending ? query.OrderByDescending(s => s.IsActive) : query.OrderBy(s => s.IsActive),
            _ => descending ? query.OrderByDescending(s => s.CreatedAtUtc) : query.OrderBy(s => s.CreatedAtUtc)
        };
    }
}
