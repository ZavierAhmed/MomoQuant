using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Application.Monitoring.Services;
using MomoQuant.Shared.Constants;

namespace MomoQuant.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ISystemHealthService _systemHealthService;

    public HealthController(ISystemHealthService systemHealthService)
    {
        _systemHealthService = systemHealthService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var mysql = await SafeComponentAsync(
            () => _systemHealthService.GetDatabaseHealthAsync(cancellationToken));
        var redis = await SafeComponentAsync(
            () => _systemHealthService.GetRedisHealthAsync(cancellationToken));

        var response = PublicHealthResponseMapper.Map(
            AppConstants.ApplicationName,
            mysql,
            redis);

        return Ok(response);
    }

    /// <summary>
    /// Resolves component health without leaking provider errors, messages, or connection details.
    /// </summary>
    private static async Task<ComponentHealthDto?> SafeComponentAsync(
        Func<Task<ServiceResult<ComponentHealthDto>>> check)
    {
        try
        {
            var result = await check();
            if (!result.Succeeded || result.Data is null)
            {
                return new ComponentHealthDto
                {
                    Name = "Unknown",
                    Status = "Unhealthy",
                    LatencyMs = null,
                    Message = string.Empty
                };
            }

            // Strip message so public payload never surfaces diagnostics.
            return new ComponentHealthDto
            {
                Name = result.Data.Name,
                Status = result.Data.Status,
                LatencyMs = result.Data.LatencyMs,
                Message = string.Empty
            };
        }
        catch
        {
            return new ComponentHealthDto
            {
                Name = "Unknown",
                Status = "Unhealthy",
                LatencyMs = null,
                Message = string.Empty
            };
        }
    }
}
