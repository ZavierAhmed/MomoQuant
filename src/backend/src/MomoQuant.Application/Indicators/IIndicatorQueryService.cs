using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators.Dtos;

namespace MomoQuant.Application.Indicators;

public interface IIndicatorQueryService
{
    Task<ServiceResult<IndicatorSnapshotDto>> GetSnapshotAsync(
        long symbolId,
        string timeframe,
        long candleId,
        CancellationToken cancellationToken = default);
}
