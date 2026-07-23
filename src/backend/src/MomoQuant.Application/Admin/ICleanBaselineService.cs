using MomoQuant.Application.Admin.Dtos;
using MomoQuant.Application.Common;

namespace MomoQuant.Application.Admin;

public interface ICleanBaselineService
{
    Task<ServiceResult<CleanBaselinePreviewDto>> PreviewAsync(
        CleanBaselineRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<CleanBaselineResultDto>> ExecuteAsync(
        CleanBaselineRequest request,
        CancellationToken cancellationToken = default);
}
