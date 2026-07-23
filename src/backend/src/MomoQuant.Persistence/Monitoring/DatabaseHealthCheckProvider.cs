using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Monitoring;

public sealed class DatabaseHealthCheckProvider : IHealthCheckProvider
{
    private readonly MomoQuantDbContext _dbContext;
    private readonly ILogger<DatabaseHealthCheckProvider> _logger;

    public DatabaseHealthCheckProvider(MomoQuantDbContext dbContext, ILogger<DatabaseHealthCheckProvider> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public string ComponentName => "Database";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            sw.Stop();

            if (!canConnect)
            {
                _logger.LogWarning("Database health check failed: connection could not be established.");
                return Unhealthy(sw, "Database connection failed.");
            }

            return new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = MonitoringSubsystem.Database,
                Status = SystemHealthStatus.Healthy,
                Severity = LogSeverity.Info,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = "Database connection successful"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Database health check failed.");
            return Unhealthy(sw, "Database connection failed.");
        }
    }

    private static HealthCheckResult Unhealthy(Stopwatch sw, string message) => new()
    {
        Name = "Database",
        Subsystem = MonitoringSubsystem.Database,
        Status = SystemHealthStatus.Unhealthy,
        Severity = LogSeverity.Critical,
        LatencyMs = (int)sw.ElapsedMilliseconds,
        Message = message
    };
}
