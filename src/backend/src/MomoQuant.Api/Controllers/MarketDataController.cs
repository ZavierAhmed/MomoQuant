using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/market-data")]
public sealed class MarketDataController : ApiControllerBase
{
    private readonly IMarketDataService _marketDataService;

    public MarketDataController(IMarketDataService marketDataService)
    {
        _marketDataService = marketDataService;
    }

    [HttpGet("candles")]
    public async Task<IActionResult> GetCandles(
        [FromQuery] long symbolId,
        [FromQuery] string timeframe,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int? limit,
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

        try
        {
            timeframe = TimeframeRequestNormalizer.NormalizeQueryTimeframe(timeframe);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _marketDataService.GetCandlesAsync(
            symbolId,
            timeframe,
            fromUtc,
            toUtc,
            limit,
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to query candles.", statusCode, errors);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("candles/import")]
    public async Task<IActionResult> ImportCandles(
        [FromBody] ImportCandlesRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        try
        {
            request = TimeframeRequestNormalizer.Normalize(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _marketDataService.ImportCandlesAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage is "Exchange was not found." or "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to import candles.", statusCode, errors);
        }

        return StatusCode(StatusCodes.Status201Created, ApiResponse<MarketDataImportDto>.Ok(result.Data, "Candle import completed successfully."));
    }

    [HttpGet("imports/{importId:long}")]
    public async Task<IActionResult> GetImportStatus(long importId, CancellationToken cancellationToken)
    {
        var result = await _marketDataService.GetImportStatusAsync(importId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Import was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("imports")]
    public async Task<IActionResult> GetRecentImports([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var result = await _marketDataService.GetRecentImportsAsync(limit, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to load recent imports.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var result = await _marketDataService.GetSettingsAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to load market data settings.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("quality")]
    public async Task<IActionResult> GetDataQuality(
        [FromQuery] long exchangeId,
        [FromQuery] long symbolId,
        [FromQuery] string timeframe,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (exchangeId <= 0)
        {
            return FailResponse("Exchange is required.", StatusCodes.Status400BadRequest, new List<ApiError>
            {
                new() { Field = "exchangeId", Message = "Exchange is required." }
            });
        }

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

        try
        {
            timeframe = TimeframeRequestNormalizer.NormalizeQueryTimeframe(timeframe);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _marketDataService.GetDataQualityAsync(
            exchangeId,
            symbolId,
            timeframe,
            fromUtc,
            toUtc,
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage is "Exchange was not found." or "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to evaluate data quality.", statusCode, errors);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot(
        [FromQuery] long symbolId,
        [FromQuery] string timeframe,
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

        try
        {
            timeframe = TimeframeRequestNormalizer.NormalizeQueryTimeframe(timeframe);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _marketDataService.GetMarketSnapshotAsync(symbolId, timeframe, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to get market snapshot.", statusCode, errors);
        }

        return OkResponse(result.Data);
    }
}
