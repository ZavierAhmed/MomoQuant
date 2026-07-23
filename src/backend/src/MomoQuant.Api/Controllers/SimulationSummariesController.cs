using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application.Simulation;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/simulation-summaries")]
public sealed class SimulationSummariesController : ApiControllerBase
{
    private readonly ISimulationRunSummaryService _summaryService;

    public SimulationSummariesController(ISimulationRunSummaryService summaryService)
    {
        _summaryService = summaryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSummaries(
        [FromQuery] PagedRequest request,
        [FromQuery] SimulationRunSourceType? sourceType,
        CancellationToken cancellationToken)
    {
        var result = await _summaryService.GetSummariesAsync(request, sourceType, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("{sourceType}/{sourceId:long}")]
    public async Task<IActionResult> GetSummary(
        SimulationRunSourceType sourceType,
        long sourceId,
        CancellationToken cancellationToken)
    {
        var result = await _summaryService.GetSummaryAsync(sourceType, sourceId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Summary was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }
}
