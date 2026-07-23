using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Application.Abstractions;

public interface ITradingSystemAnalysisRepository
{
    Task<TradingSystemAnalysis?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradingSystemAnalysis>> GetRecentAsync(
        string? systemCode,
        int limit,
        CancellationToken cancellationToken = default);

    Task AddAsync(TradingSystemAnalysis analysis, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
