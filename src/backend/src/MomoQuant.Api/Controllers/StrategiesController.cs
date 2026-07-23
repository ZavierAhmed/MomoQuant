using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/strategies")]
public sealed class StrategiesController : ApiControllerBase
{
    private readonly IStrategyService _strategyService;
    private readonly IStrategyDataRequirementService _requirementService;

    public StrategiesController(
        IStrategyService strategyService,
        IStrategyDataRequirementService requirementService)
    {
        _strategyService = strategyService;
        _requirementService = requirementService;
    }

    [HttpGet]
    public async Task<IActionResult> GetStrategies(CancellationToken cancellationToken)
    {
        var result = await _strategyService.GetStrategiesAsync(cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("data-requirements")]
    public async Task<IActionResult> GetDataRequirements(CancellationToken cancellationToken)
    {
        var result = await _requirementService.GetAllAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load strategy data requirements.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/data-requirements")]
    public async Task<IActionResult> GetDataRequirementByStrategy(long id, CancellationToken cancellationToken)
    {
        var result = await _requirementService.GetByStrategyIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy data requirement was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("resolve-benchmark-requirements")]
    public async Task<IActionResult> ResolveBenchmarkRequirements(
        [FromBody] ResolveStrategyRequirementsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _requirementService.ResolveAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to resolve strategy requirements.", StatusCodes.Status400BadRequest);
        }

        if (result.Data.BlockingIssues.Count > 0)
        {
            var errors = result.Data.BlockingIssues
                .Select(issue => new ApiError { Field = "strategyIds", Message = issue })
                .ToList();
            return FailResponse("Strategy requirements contain blocking issues.", StatusCodes.Status400BadRequest, errors);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("code/{strategyCode}")]
    public async Task<IActionResult> GetStrategyByCode(string strategyCode, CancellationToken cancellationToken)
    {
        var result = await _strategyService.GetStrategyByCodeAsync(strategyCode, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetStrategy(long id, CancellationToken cancellationToken)
    {
        var result = await _strategyService.GetStrategyByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("{id:long}/enable")]
    public async Task<IActionResult> EnableStrategy(long id, CancellationToken cancellationToken)
    {
        var result = await _strategyService.EnableStrategyAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data, "Strategy enabled successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("{id:long}/disable")]
    public async Task<IActionResult> DisableStrategy(long id, CancellationToken cancellationToken)
    {
        var result = await _strategyService.DisableStrategyAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data, "Strategy disabled successfully.");
    }

    [HttpGet("{id:long}/parameters")]
    public async Task<IActionResult> GetParameters(long id, CancellationToken cancellationToken)
    {
        var result = await _strategyService.GetParametersAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPut("{id:long}/parameters")]
    public async Task<IActionResult> UpdateParameters(
        long id,
        [FromBody] UpdateStrategyParametersRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _strategyService.UpdateParametersAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Strategy was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to update strategy parameters.", statusCode, errors);
        }

        return OkResponse(result.Data, "Strategy parameters updated successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate(
        [FromBody] StrategyEvaluationRequest request,
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

        var result = await _strategyService.EvaluateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage is "Symbol was not found."
                or "Selected candle was not found for this symbol and timeframe."
                or "No candles found for this symbol and timeframe. Import candles first from Market Watch."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to evaluate strategies.", statusCode, errors);
        }

        return OkResponse(result.Data, "Strategy evaluation completed successfully.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("evaluate-latest")]
    public async Task<IActionResult> EvaluateLatest(
        [FromBody] StrategyEvaluateLatestRequest request,
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

        var result = await _strategyService.EvaluateLatestAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage is "Symbol was not found."
                or "No candles found for this symbol and timeframe. Import candles first from Market Watch."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to evaluate strategies.", statusCode, errors);
        }

        return OkResponse(result.Data, "Latest candle strategy evaluation completed successfully.");
    }
}
