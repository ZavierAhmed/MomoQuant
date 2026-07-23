using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies.Optimization;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/strategy-research")]
public sealed class StrategyResearchController : ApiControllerBase
{
    private readonly IStrategyValidationService _validationService;
    private readonly IParameterOptimizationService _optimizationService;
    private readonly ITargetParameterOptimizationService _targetOptimizationService;
    private readonly IStrategyParameterSetService _parameterSetService;
    private readonly IStrategyParameterDefinitionProvider _definitionProvider;
    private readonly ICurrentUserService _currentUserService;

    public StrategyResearchController(
        IStrategyValidationService validationService,
        IParameterOptimizationService optimizationService,
        ITargetParameterOptimizationService targetOptimizationService,
        IStrategyParameterSetService parameterSetService,
        IStrategyParameterDefinitionProvider definitionProvider,
        ICurrentUserService currentUserService)
    {
        _validationService = validationService;
        _optimizationService = optimizationService;
        _targetOptimizationService = targetOptimizationService;
        _parameterSetService = parameterSetService;
        _definitionProvider = definitionProvider;
        _currentUserService = currentUserService;
    }

    [HttpPost("validation/run")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> RunValidation([FromBody] RunStrategyValidationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationFailResponse();
        try
        {
            request = TimeframeRequestNormalizer.NormalizeValidation(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }
        if (request.FromUtc == default || request.ToUtc == default || request.FromUtc >= request.ToUtc)
        {
            return FailResponse("A valid date range is required.", StatusCodes.Status400BadRequest);
        }

        var result = await _validationService.RunAsync(request, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Validation completed.") : FailResponse(result.ErrorMessage ?? "Validation failed.");
    }

    [HttpPost("optimization/run")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> RunOptimization([FromBody] RunParameterOptimizationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationFailResponse();
        try
        {
            request = TimeframeRequestNormalizer.NormalizeOptimization(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }
        if (request.FromUtc == default || request.ToUtc == default || request.FromUtc >= request.ToUtc)
        {
            return FailResponse("A valid date range is required.", StatusCodes.Status400BadRequest);
        }

        var result = await _optimizationService.RunAsync(request, _currentUserService.UserId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Optimization completed.") : FailResponse(result.ErrorMessage ?? "Optimization failed.");
    }

    [HttpGet("optimization/{runId:long}")]
    public async Task<IActionResult> GetOptimization(long runId, CancellationToken cancellationToken)
    {
        var result = await _optimizationService.GetAsync(runId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data) : FailResponse(result.ErrorMessage ?? "Optimization run not found.", StatusCodes.Status404NotFound);
    }

    [HttpPost("optimization/{runId:long}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> CancelOptimization(long runId, CancellationToken cancellationToken)
    {
        var result = await _optimizationService.CancelAsync(runId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Cancellation requested.") : FailResponse(result.ErrorMessage ?? "Could not cancel.");
    }

    [HttpGet("parameter-sets")]
    public async Task<IActionResult> ListParameterSets(
        [FromQuery] string? strategyCode,
        [FromQuery] long? symbolId,
        [FromQuery] string? timeframe,
        CancellationToken cancellationToken)
    {
        var result = await _parameterSetService.ListAsync(strategyCode, symbolId, timeframe, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data) : FailResponse(result.ErrorMessage ?? "Failed to list parameter sets.");
    }

    [HttpPost("parameter-sets")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> SaveParameterSet([FromBody] SaveStrategyParameterSetRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationFailResponse();
        var result = await _parameterSetService.SaveAsync(request, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Parameter set saved.") : FailResponse(result.ErrorMessage ?? "Failed to save parameter set.");
    }

    [HttpPost("parameter-sets/{id:long}/approve")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> ApproveParameterSet(long id, CancellationToken cancellationToken)
    {
        var result = await _parameterSetService.ApproveAsync(id, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Parameter set approved.") : FailResponse(result.ErrorMessage ?? "Failed to approve parameter set.");
    }

    [HttpGet("parameter-definitions/{strategyCode}")]
    public IActionResult GetParameterDefinitions(string strategyCode)
    {
        var defs = _definitionProvider.GetDefinitions(strategyCode);
        return OkResponse(defs);
    }

    [HttpPost("target-optimization/run")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> RunTargetOptimization([FromBody] TargetOptimizationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationFailResponse();
        try
        {
            request = TimeframeRequestNormalizer.NormalizeTargetOptimization(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }
        if (request.FromUtc == default || request.ToUtc == default || request.FromUtc >= request.ToUtc)
        {
            return FailResponse("A valid date range is required.", StatusCodes.Status400BadRequest);
        }

        var result = await _targetOptimizationService.RunTargetOptimizationAsync(request, _currentUserService.UserId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Target optimization completed.") : FailResponse(result.ErrorMessage ?? "Target optimization failed.");
    }

    [HttpGet("target-optimization/{runId:long}")]
    public async Task<IActionResult> GetTargetOptimization(long runId, CancellationToken cancellationToken)
    {
        var result = await _targetOptimizationService.GetAsync(runId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data) : FailResponse(result.ErrorMessage ?? "Target optimization run not found.", StatusCodes.Status404NotFound);
    }

    [HttpPost("target-optimization/{runId:long}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> CancelTargetOptimization(long runId, CancellationToken cancellationToken)
    {
        var result = await _targetOptimizationService.CancelAsync(runId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Cancellation requested.") : FailResponse(result.ErrorMessage ?? "Could not cancel.");
    }

    [HttpPost("target-optimization/{runId:long}/save-best")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> SaveTargetOptimizationBest(long runId, [FromBody] SaveTargetOptimizationBestRequest request, CancellationToken cancellationToken)
    {
        var result = await _targetOptimizationService.SaveBestAsync(runId, request, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Parameter set saved.") : FailResponse(result.ErrorMessage ?? "Failed to save parameter set.");
    }

    [HttpPost("target-optimization/{runId:long}/approve-best")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> ApproveTargetOptimizationBest(long runId, CancellationToken cancellationToken)
    {
        var result = await _targetOptimizationService.ApproveBestAsync(runId, cancellationToken);
        return result.Succeeded ? OkResponse(result.Data, "Parameter set approved.") : FailResponse(result.ErrorMessage ?? "Failed to approve parameter set.");
    }

    [HttpGet("target-optimization/{runId:long}/export/json")]
    public async Task<IActionResult> ExportTargetOptimizationJson(long runId, CancellationToken cancellationToken)
    {
        var result = await _targetOptimizationService.GetAsync(runId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Target optimization run not found.", StatusCodes.Status404NotFound);
        }

        var json = JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"target-optimization-{runId}.json");
    }
}
