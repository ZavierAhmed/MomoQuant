using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Exchanges;
using MomoQuant.Application.Exchanges.Dtos;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/exchanges/binance-futures")]
public sealed class BinanceFuturesSymbolsController : ApiControllerBase
{
    private readonly IBinanceFuturesSymbolService _symbolService;

    public BinanceFuturesSymbolsController(IBinanceFuturesSymbolService symbolService)
    {
        _symbolService = symbolService;
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpGet("discover-symbols")]
    public async Task<IActionResult> DiscoverSymbols([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var result = await _symbolService.DiscoverAsync(limit, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to discover symbols.", StatusCodes.Status502BadGateway);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("add-symbols")]
    public async Task<IActionResult> AddSymbols(
        [FromBody] AddBinanceFuturesSymbolsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _symbolService.AddSymbolsAsync(request ?? new AddBinanceFuturesSymbolsRequest(), cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<MomoQuant.Shared.Contracts.ApiError>
                {
                    new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." }
                };

            return FailResponse(result.ErrorMessage ?? "Unable to add symbols.", StatusCodes.Status400BadRequest, errors);
        }

        return OkResponse(result.Data, "Symbols added successfully.");
    }
}
