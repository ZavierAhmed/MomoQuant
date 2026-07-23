using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Admin;
using MomoQuant.Application.Admin.Dtos;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/v1/admin/system-cleanup")]
public sealed class AdminSystemCleanupController : ApiControllerBase
{
    private readonly ICleanBaselineService _cleanBaselineService;

    public AdminSystemCleanupController(ICleanBaselineService cleanBaselineService)
    {
        _cleanBaselineService = cleanBaselineService;
    }

    [HttpPost("preview-clean-baseline")]
    public async Task<IActionResult> PreviewCleanBaseline(
        [FromBody] CleanBaselineRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _cleanBaselineService.PreviewAsync(request ?? new CleanBaselineRequest(), cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to preview clean baseline.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("execute-clean-baseline")]
    public async Task<IActionResult> ExecuteCleanBaseline(
        [FromBody] CleanBaselineRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return FailResponse("Request body is required.", StatusCodes.Status400BadRequest);
        }

        var result = await _cleanBaselineService.ExecuteAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<MomoQuant.Shared.Contracts.ApiError>
                {
                    new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." }
                };

            return FailResponse(result.ErrorMessage ?? "Unable to execute clean baseline.", StatusCodes.Status400BadRequest, errors);
        }

        return OkResponse(result.Data, "Clean baseline reset completed successfully.");
    }
}
