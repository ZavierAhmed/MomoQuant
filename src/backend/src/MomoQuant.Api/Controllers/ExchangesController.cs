using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Exchanges;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/exchanges")]
public sealed class ExchangesController : ApiControllerBase
{
    private readonly IExchangeService _exchangeService;

    public ExchangesController(IExchangeService exchangeService)
    {
        _exchangeService = exchangeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetExchanges([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _exchangeService.GetExchangesAsync(request, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetExchange(long id, CancellationToken cancellationToken)
    {
        var result = await _exchangeService.GetExchangeByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Exchange was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/symbols")]
    public async Task<IActionResult> GetExchangeSymbols(
        long id,
        [FromQuery] bool enabledOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (!enabledOnly)
        {
            return FailResponse("Only enabled symbols are supported on this endpoint.", StatusCodes.Status400BadRequest);
        }

        var result = await _exchangeService.GetEnabledSymbolsAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Exchange was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return FailResponse(result.ErrorMessage ?? "Unable to load exchange symbols.", statusCode);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    public async Task<IActionResult> CreateExchange([FromBody] CreateExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _exchangeService.CreateExchangeAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to create exchange.", StatusCodes.Status400BadRequest, errors);
        }

        return StatusCode(StatusCodes.Status201Created, ApiResponse<ExchangeDto>.Ok(result.Data, "Exchange created successfully."));
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> UpdateExchange(long id, [FromBody] UpdateExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _exchangeService.UpdateExchangeAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Exchange was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to update exchange.", statusCode, errors);
        }

        return OkResponse(result.Data, "Exchange updated successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteExchange(long id, CancellationToken cancellationToken)
    {
        var result = await _exchangeService.DeleteExchangeAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Exchange was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return FailResponse(result.ErrorMessage ?? "Unable to delete exchange.", statusCode);
        }

        return OkResponse(result.Data, "Exchange deleted successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{id:long}/test-connection")]
    public async Task<IActionResult> TestConnection(long id, CancellationToken cancellationToken)
    {
        var result = await _exchangeService.TestConnectionAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Exchange was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return FailResponse(result.ErrorMessage ?? "Unable to test exchange connectivity.", statusCode);
        }

        return OkResponse(result.Data, "Exchange connectivity test completed.");
    }
}
