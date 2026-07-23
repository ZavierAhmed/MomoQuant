using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public interface ISkSystemAnalysisService
{
    Task<ServiceResult<SkSystemAnalysisResultDto>> AnalyzeAsync(
        SkSystemAnalyzeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Runs SK analysis without persisting — used by SK LivePaper simulation only.</summary>
    Task<ServiceResult<SkSystemAnalysisResultDto>> AnalyzeSnapshotAsync(
        SkSystemAnalyzeRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<TradingSystemAnalysisSummaryDto>>> GetRecentAnalysesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SkSystemAnalysisResultDto>> GetAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<MarketDataImportDto>>> ImportRequiredDataAsync(
        SkImportRequiredDataRequest request,
        CancellationToken cancellationToken = default);
}
