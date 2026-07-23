using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Audit.Dtos;
using MomoQuant.Application.Audit.Services;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/v1/audit")]
public sealed class AuditController : ApiControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditController(IAuditLogService auditLogService) => _auditLogService = auditLogService;

    [HttpGet("logs")]
    public Task<IActionResult> GetLogs([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _auditLogService.GetLogsAsync(query, cancellationToken));

    [HttpGet("logs/{id:long}")]
    public Task<IActionResult> GetLog(long id, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _auditLogService.GetLogByIdAsync(id, cancellationToken));

    [HttpGet("summary")]
    public Task<IActionResult> GetSummary([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _auditLogService.GetSummaryAsync(query, cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<Application.Common.ServiceResult<T>>> action)
    {
        var result = await action();
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Audit request failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
