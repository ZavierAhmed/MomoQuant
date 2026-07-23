using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/indicators")]
public sealed class IndicatorsController : ApiControllerBase
{
    private readonly IIndicatorQueryService _indicatorQueryService;
    private readonly IIndicatorCalculationService _indicatorCalculationService;

    public IndicatorsController(
        IIndicatorQueryService indicatorQueryService,
        IIndicatorCalculationService indicatorCalculationService)
    {
        _indicatorQueryService = indicatorQueryService;
        _indicatorCalculationService = indicatorCalculationService;
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot(
        [FromQuery] long symbolId,
        [FromQuery] string timeframe,
        [FromQuery] long candleId,
        CancellationToken cancellationToken)
    {
        if (symbolId <= 0)
        {
            return FailResponse("Symbol is required.", StatusCodes.Status400BadRequest, new List<ApiError>
            {
                new() { Field = "symbolId", Message = "Symbol is required." }
            });
        }

        if (string.IsNullOrWhiteSpace(timeframe))
        {
            return FailResponse("Timeframe is required.", StatusCodes.Status400BadRequest, new List<ApiError>
            {
                new() { Field = "timeframe", Message = "Timeframe is required." }
            });
        }

        if (candleId <= 0)
        {
            return FailResponse("Candle is required.", StatusCodes.Status400BadRequest, new List<ApiError>
            {
                new() { Field = "candleId", Message = "Candle is required." }
            });
        }

        var result = await _indicatorQueryService.GetSnapshotAsync(symbolId, timeframe, candleId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Indicator snapshot was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to get indicator snapshot.", statusCode, errors);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("recalculate")]
    public async Task<IActionResult> Recalculate(
        [FromBody] RecalculateIndicatorsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        try
        {
            request = TimeframeRequestNormalizer.NormalizeRecalculate(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _indicatorCalculationService.RecalculateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to recalculate indicators.", statusCode, errors);
        }

        return OkResponse(result.Data, "Indicator recalculation completed successfully.");
    }
}
