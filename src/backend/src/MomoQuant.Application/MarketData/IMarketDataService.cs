using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.MarketData;

public interface IMarketDataService
{
    Task<ServiceResult<IReadOnlyList<CandleDto>>> GetCandlesAsync(
        long symbolId,
        string timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDataImportDto>> ImportCandlesAsync(
        ImportCandlesRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDataImportDto>> GetImportStatusAsync(
        long importId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<MarketDataImportDto>>> GetRecentImportsAsync(
        int? limit,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketSnapshotDto>> GetMarketSnapshotAsync(
        long symbolId,
        string timeframe,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDataQualityDto>> GetDataQualityAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDataSettingsDto>> GetSettingsAsync(
        CancellationToken cancellationToken = default);
}
