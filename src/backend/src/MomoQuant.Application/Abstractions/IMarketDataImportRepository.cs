using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Abstractions;

public interface IMarketDataImportRepository
{
    Task<MarketDataImport?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(MarketDataImport import, CancellationToken cancellationToken = default);

    Task UpdateAsync(MarketDataImport import, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketDataImport>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
