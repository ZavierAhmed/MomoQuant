using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application;
using MomoQuant.Application.Security;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ResearchRead)]
[Route("api/v1/validation-lab")]
public sealed class ValidationLabController : ControllerBase
{
    private readonly IValidationLabService _service;
    private readonly IValidationLaboratoryReadinessService _readiness;
    private readonly IValidationLaboratoryCloseoutService _closeout;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MomoQuant.Application.Research.IResearchOperationStatusService _operationStatus;

    public ValidationLabController(
        IValidationLabService service,
        IValidationLaboratoryReadinessService readiness,
        IValidationLaboratoryCloseoutService closeout,
        IServiceScopeFactory scopeFactory,
        MomoQuant.Application.Research.IResearchOperationStatusService operationStatus)
    {
        _service = service;
        _readiness = readiness;
        _closeout = closeout;
        _scopeFactory = scopeFactory;
        _operationStatus = operationStatus;
    }

    [HttpGet("readiness")]
    public async Task<ActionResult<ApiResponse<ValidationLaboratoryReadinessReport>>> GetReadiness(
        CancellationToken cancellationToken)
    {
        var result = await _readiness.GetReadinessAsync(cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationLaboratoryReadinessReport>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationLaboratoryReadinessReport>.Fail(result.ErrorMessage!));
    }

    /// <summary>
    /// Development-only hosting security status. Always Blocked until a future milestone closes auth gaps.
    /// </summary>
    [HttpGet("hosting-security")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<HostingSecurityReadinessDto>> GetHostingSecurity(
        [FromServices] IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound(ApiResponse<HostingSecurityReadinessDto>.Fail(
                "Hosting security status is available in Development only."));
        }

        return Ok(ApiResponse<HostingSecurityReadinessDto>.Ok(HostingSecurityReadinessDto.CreateBlocked()));
    }

    [HttpPost("closeout/milestone-223")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationLaboratoryCloseoutReport>>> RunMilestone223Closeout(
        CancellationToken cancellationToken)
    {
        var result = await _closeout.RunMilestone223CloseoutAsync(cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationLaboratoryCloseoutReport>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationLaboratoryCloseoutReport>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/closeout-audit")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ExperimentCloseoutAuditResult>>> AuditExperimentCloseout(
        long id,
        [FromQuery] bool verifyExports = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _closeout.AuditExperimentAsync(id, verifyExports, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ExperimentCloseoutAuditResult>.Ok(result.Data!))
            : BadRequest(ApiResponse<ExperimentCloseoutAuditResult>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> CreateExperiment(
        [FromBody] CreateValidationExperimentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateExperimentAsync(request, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ValidationExperimentDto>>>> GetExperiments(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetRecentAsync(limit, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<ValidationExperimentDto>>.Ok(result.Data!))
            : BadRequest(ApiResponse<IReadOnlyList<ValidationExperimentDto>>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}")]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDetailDto>>> GetExperiment(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetExperimentDetailAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDetailDto>.Ok(result.Data!))
            : NotFound(ApiResponse<ValidationExperimentDetailDto>.Fail(result.ErrorMessage!));
    }

    [HttpPut("experiments/{id:long}")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> UpdateExperiment(
        long id,
        [FromBody] UpdateValidationExperimentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateExperimentAsync(id, request, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/prepare-data")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> PrepareData(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.PrepareDataAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/run-training")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> RunTraining(
        long id,
        CancellationToken cancellationToken)
    {
        // Run training in an isolated DI scope so client disconnect cannot dispose
        // the request DbContext mid-trial (long-range C2 can take hours).
        var expId = id;
        var trainTask = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            return await svc.RunTrainingAsync(expId, CancellationToken.None);
        });

        // Wait until status leaves DataReady (accepted) or training finishes quickly.
        for (var i = 0; i < 60; i++)
        {
            if (trainTask.IsCompleted)
            {
                var result = await trainTask;
                return result.Succeeded
                    ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
                    : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
            }

            var cur = await _service.GetExperimentAsync(id, CancellationToken.None);
            if (cur.Succeeded && cur.Data is not null &&
                cur.Data.Status != MomoQuant.Domain.Enums.ValidationExperimentStatus.DataReady &&
                cur.Data.Status != MomoQuant.Domain.Enums.ValidationExperimentStatus.Draft)
            {
                // Accepted and running (or terminal). Return current snapshot; clients poll.
                return Ok(ApiResponse<ValidationExperimentDto>.Ok(cur.Data));
            }

            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client gave up waiting for accept — background trainTask continues.
                break;
            }
        }

        var latest = await _service.GetExperimentAsync(id, CancellationToken.None);
        if (latest.Succeeded && latest.Data is not null)
        {
            return Ok(ApiResponse<ValidationExperimentDto>.Ok(latest.Data));
        }

        return Accepted(ApiResponse<ValidationExperimentDto>.Ok(latest.Data!));
    }

    [HttpGet("experiments/{id:long}/training-trials")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ValidationParameterTrialDto>>>> GetTrainingTrials(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetTrainingTrialsAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<ValidationParameterTrialDto>>.Ok(result.Data!))
            : NotFound(ApiResponse<IReadOnlyList<ValidationParameterTrialDto>>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/resume-training")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> ResumeTraining(
        long id,
        CancellationToken cancellationToken)
    {
        var expId = id;
        var trainTask = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            return await svc.ResumeTrainingAsync(expId, CancellationToken.None);
        });

        for (var i = 0; i < 60; i++)
        {
            if (trainTask.IsCompleted)
            {
                var result = await trainTask;
                return result.Succeeded
                    ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
                    : Conflict(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
            }

            var cur = await _service.GetExperimentAsync(id, CancellationToken.None);
            if (cur.Succeeded && cur.Data is not null &&
                ValidationLifecycleGate.IsTrainingInProgress(cur.Data.Status))
            {
                return Ok(ApiResponse<ValidationExperimentDto>.Ok(cur.Data));
            }

            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var latest = await _service.GetExperimentAsync(id, CancellationToken.None);
        return latest.Succeeded && latest.Data is not null
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(latest.Data))
            : Accepted(ApiResponse<ValidationExperimentDto>.Ok(latest.Data!));
    }

    [HttpPost("experiments/{id:long}/recover-trials")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationTrialRecoveryReport>>> RecoverTrials(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.RecoverTrialsAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationTrialRecoveryReport>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationTrialRecoveryReport>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/training-progress")]
    public async Task<ActionResult<ApiResponse<ValidationTrainingProgressDto>>> GetTrainingProgress(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetTrainingProgressAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationTrainingProgressDto>.Ok(result.Data!))
            : NotFound(ApiResponse<ValidationTrainingProgressDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/operation-status")]
    public async Task<ActionResult<ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>>> GetOperationStatus(
        long id,
        CancellationToken cancellationToken)
    {
        var durable = await _operationStatus.GetForValidationExperimentAsync(id, cancellationToken);
        if (durable is not null)
        {
            return Ok(ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>.Ok(durable));
        }

        var progress = await _service.GetTrainingProgressAsync(id, cancellationToken);
        if (!progress.Succeeded || progress.Data is null)
        {
            return NotFound(ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>.Fail(
                progress.ErrorMessage ?? "Experiment not found."));
        }

        var detail = await _service.GetExperimentAsync(id, cancellationToken);
        var status = detail.Data?.Status.ToString() ?? "Unknown";
        var stage = detail.Data?.CurrentStage ?? "Unknown";
        var op = await _operationStatus.SyncFromValidationTrainingAsync(
            id,
            status,
            stage,
            progress.Data,
            cancellationToken: cancellationToken);
        return Ok(ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>.Ok(op));
    }

    [HttpPost("experiments/{id:long}/operation-status/cancel")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>>> CancelOperationStatus(
        long id,
        CancellationToken cancellationToken)
    {
        var operationId = $"vl-train-{id}";
        var caller = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.Identity?.Name
            ?? string.Empty;
        var isAdmin = User.IsInRole(Domain.Enums.UserRole.Admin.ToString());
        var result = await _operationStatus.CancelAsync(operationId, caller, isAdmin, cancellationToken);
        if (!result.Succeeded)
        {
            if (string.Equals(
                    result.ErrorField,
                    MomoQuant.Application.Research.ResearchOperationStatusCodes.CancelForbidden,
                    StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>.Fail(result.ErrorMessage!));
            }

            return BadRequest(ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>.Fail(result.ErrorMessage!));
        }

        return Ok(ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>.Ok(result.Data!));
    }

    [HttpPost("experiments/{id:long}/freeze")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> Freeze(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.FreezeAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/run-validation")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDetailDto>>> RunValidation(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.RunValidationAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDetailDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDetailDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/comparison")]
    public async Task<ActionResult<ApiResponse<object>>> GetComparison(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetComparisonAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/confidence-analysis")]
    public async Task<ActionResult<ApiResponse<object>>> GetConfidenceAnalysis(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetConfidenceAnalysisAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/risk-analysis")]
    public async Task<ActionResult<ApiResponse<object>>> GetRiskAnalysis(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetRiskAnalysisAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/candidates")]
    public async Task<ActionResult<ApiResponse<PagedResultDto<object>>>> GetCandidates(
        long id,
        [FromQuery] ValidationCandidateQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetCandidatesAsync(id, query, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<PagedResultDto<object>>.Ok(result.Data!))
            : BadRequest(ApiResponse<PagedResultDto<object>>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/diagnostics")]
    public async Task<ActionResult<ApiResponse<object>>> GetDiagnostics(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetDiagnosticsAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/reconciliation")]
    public async Task<ActionResult<ApiResponse<object>>> GetReconciliation(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetReconciliationAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/leakage-audit")]
    public async Task<ActionResult<ApiResponse<object>>> GetLeakageAudit(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetLeakageAuditAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/exclusivity")]
    public async Task<ActionResult<ApiResponse<object>>> GetExclusivity(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetExclusivityAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<object>.Ok(result.Data!))
            : NotFound(ApiResponse<object>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/selection-integrity")]
    public async Task<ActionResult<ApiResponse<ValidationSelectionIntegrityReportDto>>> GetSelectionIntegrity(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetSelectionIntegrityAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationSelectionIntegrityReportDto>.Ok(result.Data!))
            : NotFound(ApiResponse<ValidationSelectionIntegrityReportDto>.Fail(result.ErrorMessage!));
    }

    [HttpGet("experiments/{id:long}/metric-basis-audit")]
    public async Task<ActionResult<ApiResponse<ValidationMetricBasisAuditReportDto>>> GetMetricBasisAudit(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetMetricBasisAuditAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationMetricBasisAuditReportDto>.Ok(result.Data!))
            : NotFound(ApiResponse<ValidationMetricBasisAuditReportDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/recalculate-metrics")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<RecalculateValidationMetricsResultDto>>> RecalculateMetrics(
        long id,
        [FromBody] RecalculateValidationMetricsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RecalculateMetricsAsync(id, request, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<RecalculateValidationMetricsResultDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<RecalculateValidationMetricsResultDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/recalculate-verdict")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDetailDto>>> RecalculateVerdict(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.RecalculateVerdictAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDetailDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDetailDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/clone")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> Clone(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.CloneAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
    }

    [HttpPost("experiments/{id:long}/rerun-exactly")]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ValidationExperimentDto>>> RerunExactly(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _service.RerunExactlyAsync(id, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ValidationExperimentDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ValidationExperimentDto>.Fail(result.ErrorMessage!));
    }
}