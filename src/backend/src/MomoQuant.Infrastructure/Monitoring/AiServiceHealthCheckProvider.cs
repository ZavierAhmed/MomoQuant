using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Infrastructure.Monitoring;

public sealed class AiServiceHealthCheckProvider : IHealthCheckProvider
{
    private readonly IAiServiceClient _aiServiceClient;
    private readonly ILogger<AiServiceHealthCheckProvider> _logger;

    public AiServiceHealthCheckProvider(IAiServiceClient aiServiceClient, ILogger<AiServiceHealthCheckProvider> logger)
    {
        _aiServiceClient = aiServiceClient;
        _logger = logger;
    }

    public string ComponentName => "AI Service";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _aiServiceClient.GetHealthAsync(cancellationToken);
            sw.Stop();

            if (!result.Succeeded)
            {
                _logger.LogWarning("AI service health check failed: {Message}", result.ErrorMessage);
                return new HealthCheckResult
                {
                    Name = ComponentName,
                    Subsystem = MonitoringSubsystem.AiService,
                    Status = SystemHealthStatus.Degraded,
                    Severity = LogSeverity.Warning,
                    LatencyMs = null,
                    Message = "AI service unavailable"
                };
            }

            return new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = MonitoringSubsystem.AiService,
                Status = SystemHealthStatus.Healthy,
                Severity = LogSeverity.Info,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = "AI service is reachable"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "AI service health check failed.");
            return new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = MonitoringSubsystem.AiService,
                Status = SystemHealthStatus.Degraded,
                Severity = LogSeverity.Warning,
                LatencyMs = null,
                Message = "AI service unavailable"
            };
        }
    }
}
