using System.Diagnostics;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Infrastructure.Monitoring;

public sealed class ApiHealthCheckProvider : IHealthCheckProvider
{
    public string ComponentName => "API";

    public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        sw.Stop();

        return Task.FromResult(new HealthCheckResult
        {
            Name = ComponentName,
            Subsystem = MonitoringSubsystem.Api,
            Status = SystemHealthStatus.Healthy,
            Severity = LogSeverity.Info,
            LatencyMs = (int)Math.Max(sw.ElapsedMilliseconds, 1),
            Message = "API is running"
        });
    }
}
