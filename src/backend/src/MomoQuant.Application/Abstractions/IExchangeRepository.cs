using MomoQuant.Domain.Exchanges;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IExchangeRepository
{
    Task<Exchange?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Exchange?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, long? excludeExchangeId = null, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Exchange> Items, int TotalCount)> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task AddAsync(Exchange exchange, CancellationToken cancellationToken = default);
    Task UpdateAsync(Exchange exchange, CancellationToken cancellationToken = default);
    Task<bool> HasBlockingDependenciesAsync(long exchangeId, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(long exchangeId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
