using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application.MarketSituation;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MarketSituationController : ApiControllerBase
{
    private readonly IMarketSituationService _marketSituationService;

    public MarketSituationController(IMarketSituationService marketSituationService)
    {
        _marketSituationService = marketSituationService;
    }

    [HttpGet("api/v1/market-situation/current")]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] long exchangeId,
        [FromQuery] long symbolId,
        [FromQuery] string timeframe,
        CancellationToken cancellationToken)
    {
        var result = await _marketSituationService.GetCurrentAsync(exchangeId, symbolId, timeframe, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Market situation could not be determined.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
