using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Validation.Dtos;

namespace MomoQuant.Application.Optimization;

public interface IStrategyResearchCandleCoverageService
{
    Task<ServiceResult<IReadOnlyList<CandleCoverageDto>>> EnsureCoverageAsync(
        long exchangeId,
        long symbolId,
        string strategyCode,
        string executionTimeframe,
        DateTime fromUtc,
        DateTime toUtc,
        bool autoImport,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyResearchCandleCoverageService : IStrategyResearchCandleCoverageService
{
    private readonly IMarketDataCoverageService _coverageService;

    public StrategyResearchCandleCoverageService(IMarketDataCoverageService coverageService)
    {
        _coverageService = coverageService;
    }

    public Task<ServiceResult<IReadOnlyList<CandleCoverageDto>>> EnsureCoverageAsync(
        long exchangeId,
        long symbolId,
        string strategyCode,
        string executionTimeframe,
        DateTime fromUtc,
        DateTime toUtc,
        bool autoImport,
        CancellationToken cancellationToken = default) =>
        _coverageService.EnsureCoverageAsync(
            exchangeId,
            symbolId,
            strategyCode,
            executionTimeframe,
            fromUtc,
            toUtc,
            autoImport,
            cancellationToken);
}
