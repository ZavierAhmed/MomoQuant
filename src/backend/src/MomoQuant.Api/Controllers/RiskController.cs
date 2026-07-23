using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Risk.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/risk")]
public sealed class RiskController : ApiControllerBase
{
    private readonly IRiskProfileService _profileService;
    private readonly IRiskRuleService _ruleService;
    private readonly IRiskDecisionService _decisionService;
    private readonly IRiskEvaluationService _evaluationService;

    public RiskController(
        IRiskProfileService profileService,
        IRiskRuleService ruleService,
        IRiskDecisionService decisionService,
        IRiskEvaluationService evaluationService)
    {
        _profileService = profileService;
        _ruleService = ruleService;
        _decisionService = decisionService;
        _evaluationService = evaluationService;
    }

    [HttpGet("profiles")]
    public async Task<IActionResult> GetProfiles(CancellationToken cancellationToken)
    {
        var result = await _profileService.GetProfilesAsync(cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("profiles/{id:long}")]
    public async Task<IActionResult> GetProfile(long id, CancellationToken cancellationToken)
    {
        var result = await _profileService.GetProfileByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Risk profile was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("profiles")]
    public async Task<IActionResult> CreateProfile(
        [FromBody] CreateRiskProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _profileService.CreateProfileAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to create risk profile.", StatusCodes.Status400BadRequest);
        }

        return StatusCode(StatusCodes.Status201Created, ApiResponse<RiskProfileDto>.Ok(result.Data, "Risk profile created successfully."));
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPut("profiles/{id:long}")]
    public async Task<IActionResult> UpdateProfile(
        long id,
        [FromBody] UpdateRiskProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _profileService.UpdateProfileAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "Risk profile was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return FailResponse(result.ErrorMessage ?? "Unable to update risk profile.", statusCode);
        }

        return OkResponse(result.Data, "Risk profile updated successfully.");
    }

    [HttpGet("profiles/{id:long}/rules")]
    public async Task<IActionResult> GetRules(long id, CancellationToken cancellationToken)
    {
        var result = await _ruleService.GetRulesAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Risk profile was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPut("profiles/{id:long}/rules")]
    public async Task<IActionResult> UpdateRules(
        long id,
        [FromBody] UpdateRiskRulesRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _ruleService.UpdateRulesAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to update risk rules.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data, "Risk rules updated successfully.");
    }

    [HttpGet("decisions")]
    public async Task<IActionResult> GetDecisions(
        [FromQuery] PagedRequest request,
        [FromQuery] long? symbolId,
        CancellationToken cancellationToken)
    {
        var result = await _decisionService.GetDecisionsAsync(request, symbolId, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("decisions/{id:long}")]
    public async Task<IActionResult> GetDecision(long id, CancellationToken cancellationToken)
    {
        var result = await _decisionService.GetDecisionByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Risk decision was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate(
        [FromBody] RiskEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _evaluationService.EvaluateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage is "Risk profile was not found." or "Symbol was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to evaluate risk.", statusCode, errors);
        }

        return OkResponse(result.Data, "Risk evaluation completed successfully.");
    }
}
