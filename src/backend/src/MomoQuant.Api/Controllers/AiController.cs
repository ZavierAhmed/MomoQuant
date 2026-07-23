using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/ai")]
public sealed class AiController : ApiControllerBase
{
    private readonly IAiIntegrationService _aiIntegrationService;
    private readonly IAiDecisionService _aiDecisionService;
    private readonly IAiSetupAdvisorService _aiSetupAdvisorService;

    public AiController(
        IAiIntegrationService aiIntegrationService,
        IAiDecisionService aiDecisionService,
        IAiSetupAdvisorService aiSetupAdvisorService)
    {
        _aiIntegrationService = aiIntegrationService;
        _aiDecisionService = aiDecisionService;
        _aiSetupAdvisorService = aiSetupAdvisorService;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var result = await _aiIntegrationService.GetHealthAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "AI service is unavailable.", StatusCodes.Status503ServiceUnavailable);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("regime/detect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> DetectRegime(
        [FromBody] DetectRegimeRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _aiIntegrationService.DetectRegimeAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Regime detection failed.", StatusCodes.Status503ServiceUnavailable);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("confidence/score")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> ScoreConfidence(
        [FromBody] ScoreConfidenceRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _aiIntegrationService.ScoreConfidenceAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Confidence scoring failed.", StatusCodes.Status503ServiceUnavailable);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("anomaly/detect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> DetectAnomaly(
        [FromBody] DetectAnomalyRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _aiIntegrationService.DetectAnomalyAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Anomaly detection failed.", StatusCodes.Status503ServiceUnavailable);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("explain/trade")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> ExplainTrade(
        [FromBody] ExplainTradeRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _aiIntegrationService.ExplainTradeAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Trade explanation failed.", StatusCodes.Status503ServiceUnavailable);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("evaluate-signal")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> EvaluateSignal(
        [FromBody] EvaluateSignalRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _aiIntegrationService.EvaluateSignalAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorField is not null
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status503ServiceUnavailable;

            return FailResponse(result.ErrorMessage ?? "AI signal evaluation failed.", statusCode);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("setup-advisor")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> SetupAdvisor(
        [FromBody] AiSetupAdvisorRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _aiSetupAdvisorService.GetSetupAdviceAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "AI setup advisor failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("decisions")]
    public async Task<IActionResult> GetDecisions(
        [FromQuery] PagedRequest request,
        [FromQuery] long? symbolId,
        CancellationToken cancellationToken)
    {
        var result = await _aiDecisionService.GetDecisionsAsync(request, symbolId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load AI decisions.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("decisions/{id:long}")]
    public async Task<IActionResult> GetDecision(long id, CancellationToken cancellationToken)
    {
        var result = await _aiDecisionService.GetDecisionByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "AI decision was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }
}
