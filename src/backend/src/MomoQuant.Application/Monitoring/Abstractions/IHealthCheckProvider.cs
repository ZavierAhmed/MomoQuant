using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring.Abstractions;

public sealed class HealthCheckResult
{
    public required string Name { get; init; }
    public MonitoringSubsystem Subsystem { get; init; }
    public SystemHealthStatus Status { get; init; }
    public LogSeverity Severity { get; init; } = LogSeverity.Info;
    public int? LatencyMs { get; init; }
    public required string Message { get; init; }
    public string? DetailsJson { get; init; }
}

public interface IHealthCheckProvider
{
    string ComponentName { get; }
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public interface ISubsystemHealthCheckProvider
{
    Task<IReadOnlyList<HealthCheckResult>> CheckAllAsync(CancellationToken cancellationToken = default);
}
