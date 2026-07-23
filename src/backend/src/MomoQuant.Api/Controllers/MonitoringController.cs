using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Application.Monitoring.Services;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/monitoring")]
public sealed class MonitoringController : ApiControllerBase
{
    private readonly ISystemHealthService _systemHealthService;
    private readonly IMonitoringService _monitoringService;
    private readonly ISystemHealthLogService _systemHealthLogService;
    private readonly IRecentErrorService _recentErrorService;
    private readonly ITradingPipelineStatusService _tradingPipelineStatusService;

    public MonitoringController(
        ISystemHealthService systemHealthService,
        IMonitoringService monitoringService,
        ISystemHealthLogService systemHealthLogService,
        IRecentErrorService recentErrorService,
        ITradingPipelineStatusService tradingPipelineStatusService)
    {
        _systemHealthService = systemHealthService;
        _monitoringService = monitoringService;
        _systemHealthLogService = systemHealthLogService;
        _recentErrorService = recentErrorService;
        _tradingPipelineStatusService = tradingPipelineStatusService;
    }

    [HttpGet("health")]
    public Task<IActionResult> GetHealth(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthService.GetOverallHealthAsync(cancellationToken));

    [HttpGet("health/database")]
    public Task<IActionResult> GetDatabaseHealth(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthService.GetDatabaseHealthAsync(cancellationToken));

    [HttpGet("health/redis")]
    public Task<IActionResult> GetRedisHealth(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthService.GetRedisHealthAsync(cancellationToken));

    [HttpGet("health/ai")]
    public Task<IActionResult> GetAiHealth(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthService.GetAiHealthAsync(cancellationToken));

    [HttpGet("health/subsystems")]
    public Task<IActionResult> GetSubsystemsHealth(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthService.GetSubsystemsHealthAsync(cancellationToken));

    [HttpGet("status")]
    public Task<IActionResult> GetStatus(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _monitoringService.GetSystemStatusAsync(cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpGet("system-health-logs")]
    public Task<IActionResult> GetSystemHealthLogs([FromQuery] MonitoringQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthLogService.GetPagedAsync(query, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpGet("system-health-logs/{id:long}")]
    public Task<IActionResult> GetSystemHealthLog(long id, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _systemHealthLogService.GetByIdAsync(id, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpGet("recent-errors")]
    public Task<IActionResult> GetRecentErrors([FromQuery] MonitoringQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _recentErrorService.GetRecentErrorsAsync(query, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpGet("recent-events")]
    public Task<IActionResult> GetRecentEvents([FromQuery] MonitoringQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _recentErrorService.GetRecentEventsAsync(query, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpGet("safety-events")]
    public Task<IActionResult> GetSafetyEvents([FromQuery] MonitoringQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _recentErrorService.GetSafetyEventsAsync(query, cancellationToken));

    [HttpGet("trading-pipeline-status")]
    public Task<IActionResult> GetTradingPipelineStatus(CancellationToken cancellationToken) =>
        ExecuteAsync(() => _tradingPipelineStatusService.GetStatusAsync(cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<Application.Common.ServiceResult<T>>> action)
    {
        var result = await action();
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Monitoring request failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
