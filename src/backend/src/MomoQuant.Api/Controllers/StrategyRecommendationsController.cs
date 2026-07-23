using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application.StrategyRecommendations;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
public sealed class StrategyRecommendationsController : ApiControllerBase
{
    private readonly IStrategyRecommendationService _recommendationService;

    public StrategyRecommendationsController(IStrategyRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    [HttpGet("api/v1/strategy-recommendations/current")]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] long exchangeId,
        [FromQuery] long symbolId,
        [FromQuery] string timeframe,
        [FromQuery] string mode,
        [FromQuery] bool showDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _recommendationService.GetCurrentAsync(
            exchangeId,
            symbolId,
            timeframe,
            mode,
            showDisabled,
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy recommendations could not be generated.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
