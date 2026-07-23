using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Admin;
using MomoQuant.Application.Admin.Dtos;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/v1/admin/data-cleanup")]
public sealed class AdminDataCleanupController : ApiControllerBase
{
    private readonly IFakeMarketDataCleanupService _cleanupService;

    public AdminDataCleanupController(IFakeMarketDataCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    [HttpPost("fake-market-data/preview")]
    public async Task<IActionResult> PreviewFakeMarketData(
        [FromBody] FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _cleanupService.PreviewAsync(request ?? new FakeMarketDataCleanupRequest(), cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to preview fake market data cleanup.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("fake-market-data/execute")]
    public async Task<IActionResult> ExecuteFakeMarketData(
        [FromBody] FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return FailResponse("Request body is required.", StatusCodes.Status400BadRequest);
        }

        var result = await _cleanupService.ExecuteAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<MomoQuant.Shared.Contracts.ApiError>
                {
                    new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." }
                };

            return FailResponse(result.ErrorMessage ?? "Unable to execute fake market data cleanup.", StatusCodes.Status400BadRequest, errors);
        }

        return OkResponse(result.Data, "Fake market data cleanup completed successfully.");
    }
}
