using MomoQuant.Application.Admin.Dtos;
using MomoQuant.Application.Common;

namespace MomoQuant.Application.Admin;

public interface IFakeMarketDataCleanupService
{
    Task<ServiceResult<FakeMarketDataCleanupPreviewDto>> PreviewAsync(
        FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<FakeMarketDataCleanupResultDto>> ExecuteAsync(
        FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken = default);
}
