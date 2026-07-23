using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators.Dtos;

namespace MomoQuant.Application.Indicators;

public interface IIndicatorCalculationService
{
    Task<ServiceResult<RecalculateIndicatorsResponse>> RecalculateAsync(
        RecalculateIndicatorsRequest request,
        CancellationToken cancellationToken = default);
}
