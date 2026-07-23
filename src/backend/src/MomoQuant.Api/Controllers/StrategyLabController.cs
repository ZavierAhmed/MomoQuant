using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ResearchRead)]
[Route("api/v1/strategy-lab")]
public sealed class StrategyLabController : ControllerBase
{
    private readonly IStrategyLabService _service;

    public StrategyLabController(IStrategyLabService service)
    {
        _service = service;
    }

    [HttpGet("strategies")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyLabStrategyDto>>>> GetStrategies(CancellationToken cancellationToken)
    {
        var result = await _service.GetLabStrategiesAsync(cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<StrategyLabStrategyDto>>.Ok(result.Data!))
            : BadRequest(ApiResponse<IReadOnlyList<StrategyLabStrategyDto>>.Fail(result.ErrorMessage!));
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<StrategyLabStartupHealthDto>>> GetModuleHealth(CancellationToken cancellationToken)
    {
        var result = await _service.GetStartupHealthAsync(cancellationToken);
        return Ok(ApiResponse<StrategyLabStartupHealthDto>.Ok(result.Data!));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyLabRunDto>>>> GetRuns(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetRecentRunsAsync(limit, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<StrategyLabRunDto>>.Ok(result.Data!))
            : BadRequest(ApiResponse<IReadOnlyList<StrategyLabRunDto>>.Fail(result.ErrorMessage!));
    }

    [HttpPost("runs")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<StrategyLabRunDto>>> CreateRun(
        [FromBody] CreateStrategyLabRunRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateRunAsync(request, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabRunDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<StrategyLabRunDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("runs/{id:long}/rerun")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<StrategyLabRunDto>>> Rerun(long id, CancellationToken cancellationToken)
    {
        var config = await _service.GetRerunConfigAsync(id, cancellationToken);
        if (!config.Succeeded || config.Data is null)
        {
            return NotFound(ApiResponse<StrategyLabRunDto>.Fail(config.ErrorMessage ?? "Strategy lab run not found."));
        }

        var result = await _service.CreateRunAsync(config.Data, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabRunDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<StrategyLabRunDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}")]
    public async Task<ActionResult<ApiResponse<StrategyLabRunDto>>> GetRun(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetRunAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabRunDto>.Ok(result.Data!))
            : NotFound(ApiResponse<StrategyLabRunDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/detail")]
    public async Task<ActionResult<ApiResponse<StrategyLabRunDetailDto>>> GetRunDetail(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetRunDetailAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabRunDetailDto>.Ok(result.Data!))
            : NotFound(ApiResponse<StrategyLabRunDetailDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/candidates")]
    public async Task<ActionResult<ApiResponse<PagedResultDto<StrategyResearchCandidateDto>>>> GetCandidates(
        long id,
        [FromQuery] StrategyLabCandidateQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetCandidatesAsync(id, query, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<PagedResultDto<StrategyResearchCandidateDto>>.Ok(result.Data!))
            : NotFound(ApiResponse<PagedResultDto<StrategyResearchCandidateDto>>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/candidates/{candidateId:long}")]
    public async Task<ActionResult<ApiResponse<StrategyLabCandidateDetailDto>>> GetCandidateDetail(
        long id,
        long candidateId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetCandidateDetailAsync(id, candidateId, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabCandidateDetailDto>.Ok(result.Data!))
            : NotFound(ApiResponse<StrategyLabCandidateDetailDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/portfolio-path-comparison")]
    public async Task<ActionResult<ApiResponse<PortfolioPathComparisonDto>>> GetPortfolioPathComparison(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetPortfolioPathComparisonAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<PortfolioPathComparisonDto>.Ok(result.Data!))
            : NotFound(ApiResponse<PortfolioPathComparisonDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/gate-analysis")]
    public async Task<ActionResult<ApiResponse<StrategyLabGateAnalysisDto>>> GetGateAnalysis(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetGateAnalysisAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabGateAnalysisDto>.Ok(result.Data!))
            : NotFound(ApiResponse<StrategyLabGateAnalysisDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/risk-analysis")]
    public async Task<ActionResult<ApiResponse<StrategyLabRiskAnalysisDto>>> GetRiskAnalysis(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetRiskAnalysisAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabRiskAnalysisDto>.Ok(result.Data!))
            : NotFound(ApiResponse<StrategyLabRiskAnalysisDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/risk-profile-comparison/{otherRunId:long}")]
    public async Task<ActionResult<ApiResponse<StrategyLabRiskProfileComparisonDto>>> CompareRiskProfiles(
        long id,
        long otherRunId,
        CancellationToken cancellationToken)
    {
        var result = await _service.CompareRiskProfilesAsync(id, otherRunId, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyLabRiskProfileComparisonDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<StrategyLabRiskProfileComparisonDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/by-strategy/{strategyCode}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyLabRunDto>>>> GetRunsByStrategy(
        string strategyCode,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetRunsByStrategyAsync(strategyCode, limit, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<StrategyLabRunDto>>.Ok(result.Data!))
            : BadRequest(ApiResponse<IReadOnlyList<StrategyLabRunDto>>.Fail(result.ErrorMessage!));
    }

    [HttpGet("runs/{id:long}/rerun-config")]
    public async Task<ActionResult<ApiResponse<CreateStrategyLabRunRequest>>> GetRerunConfig(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetRerunConfigAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<CreateStrategyLabRunRequest>.Ok(result.Data!))
            : NotFound(ApiResponse<CreateStrategyLabRunRequest>.Fail(result.ErrorMessage!));
    }

    [HttpPost("strategies/{strategyCode}/synthetic-tests")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SyntheticTestResultDto>>>> RunSyntheticTests(
        string strategyCode,
        CancellationToken cancellationToken)
    {
        var result = await _service.RunSyntheticTestsAsync(strategyCode, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<SyntheticTestResultDto>>.Ok(result.Data!))
            : BadRequest(ApiResponse<IReadOnlyList<SyntheticTestResultDto>>.Fail(result.ErrorMessage!));
    }

    [HttpGet("strategies/{strategyCode}/health")]
    public async Task<ActionResult<ApiResponse<StrategyHealthDto>>> GetStrategyHealth(
        string strategyCode,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetStrategyHealthAsync(strategyCode, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<StrategyHealthDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<StrategyHealthDto>.Fail(result.ErrorMessage!));
    }
}
