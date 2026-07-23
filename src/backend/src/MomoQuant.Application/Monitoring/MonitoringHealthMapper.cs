using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring;

public static class MonitoringHealthMapper
{
    public static string ToStatusString(SystemHealthStatus status) => status switch
    {
        SystemHealthStatus.Healthy => "Healthy",
        SystemHealthStatus.Degraded => "Degraded",
        SystemHealthStatus.Unhealthy => "Unhealthy",
        SystemHealthStatus.Unknown => "Unknown",
        _ => "Unknown"
    };

    public static string ToSeverityString(LogSeverity severity) => severity switch
    {
        LogSeverity.Info => "Info",
        LogSeverity.Warning => "Warning",
        LogSeverity.Error => "Error",
        LogSeverity.Critical => "Critical",
        _ => "Info"
    };

    public static string ToSubsystemString(MonitoringSubsystem subsystem) => subsystem.ToString();

    public static SystemHealthStatus AggregateStatus(IEnumerable<SystemHealthStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Any(status => status == SystemHealthStatus.Unhealthy))
        {
            return SystemHealthStatus.Unhealthy;
        }

        if (list.Any(status => status == SystemHealthStatus.Degraded))
        {
            return SystemHealthStatus.Degraded;
        }

        if (list.All(status => status == SystemHealthStatus.Unknown))
        {
            return SystemHealthStatus.Unknown;
        }

        return SystemHealthStatus.Healthy;
    }

    public static SystemHealthStatus AggregateCoreHealth(
        SystemHealthStatus databaseStatus,
        IEnumerable<SystemHealthStatus> optionalStatuses)
    {
        if (databaseStatus == SystemHealthStatus.Unhealthy)
        {
            return SystemHealthStatus.Unhealthy;
        }

        var statuses = optionalStatuses.ToList();
        if (statuses.Any(status => status == SystemHealthStatus.Unhealthy))
        {
            return SystemHealthStatus.Degraded;
        }

        if (statuses.Any(status => status == SystemHealthStatus.Degraded))
        {
            return SystemHealthStatus.Degraded;
        }

        return databaseStatus == SystemHealthStatus.Healthy
            ? SystemHealthStatus.Healthy
            : SystemHealthStatus.Degraded;
    }
}
