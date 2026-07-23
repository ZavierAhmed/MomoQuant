using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Symbols;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/symbols")]
public sealed class SymbolsController : ApiControllerBase
{
    private readonly ISymbolService _symbolService;

    public SymbolsController(ISymbolService symbolService)
    {
        _symbolService = symbolService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSymbols(
        [FromQuery] PagedRequest request,
        [FromQuery] long? exchangeId,
        CancellationToken cancellationToken)
    {
        var result = await _symbolService.GetSymbolsAsync(request, exchangeId, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetSymbol(long id, CancellationToken cancellationToken)
    {
        var result = await _symbolService.GetSymbolByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Symbol was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("sync")]
    public async Task<IActionResult> SyncSymbols([FromBody] SyncSymbolsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _symbolService.SyncSymbolsAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Exchange was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to sync symbols.", statusCode, errors);
        }

        return OkResponse(result.Data, "Symbols synced successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateSymbolStatus(
        long id,
        [FromBody] UpdateSymbolStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _symbolService.UpdateSymbolStatusAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return FailResponse(result.ErrorMessage ?? "Unable to update symbol status.", statusCode);
        }

        return OkResponse(result.Data, "Symbol status updated successfully.");
    }
}
