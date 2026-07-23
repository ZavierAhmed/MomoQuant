using MomoQuant.Domain.Exchanges;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface ISymbolRepository
{
    Task<Symbol?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Symbol?> GetByExchangeAndNameAsync(long exchangeId, string symbolName, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(long exchangeId, string symbolName, long? excludeSymbolId = null, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Symbol> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        long? exchangeId = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Symbol>> GetEnabledByExchangeIdAsync(long exchangeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSymbolNamesByExchangeAsync(long exchangeId, CancellationToken cancellationToken = default);
    Task AddAsync(Symbol symbol, CancellationToken cancellationToken = default);
    Task UpdateAsync(Symbol symbol, CancellationToken cancellationToken = default);
    Task<int> DeleteByExchangeIdAsync(long exchangeId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
