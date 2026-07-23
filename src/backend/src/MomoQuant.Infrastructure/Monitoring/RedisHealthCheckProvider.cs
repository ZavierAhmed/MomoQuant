using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Domain.Enums;
using StackExchange.Redis;

namespace MomoQuant.Infrastructure.Monitoring;

public sealed class RedisHealthCheckProvider : IHealthCheckProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedisHealthCheckProvider> _logger;

    public RedisHealthCheckProvider(IConfiguration configuration, ILogger<RedisHealthCheckProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string ComponentName => "Redis";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("Redis")
            ?? _configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = MonitoringSubsystem.Redis,
                Status = SystemHealthStatus.Unknown,
                Severity = LogSeverity.Info,
                LatencyMs = null,
                Message = "Redis is not configured"
            };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
            var database = connection.GetDatabase();
            await database.PingAsync();
            sw.Stop();

            return new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = MonitoringSubsystem.Redis,
                Status = SystemHealthStatus.Healthy,
                Severity = LogSeverity.Info,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = "Redis connection successful"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Redis health check failed.");
            return new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = MonitoringSubsystem.Redis,
                Status = SystemHealthStatus.Unhealthy,
                Severity = LogSeverity.Error,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = $"Redis connection failed: {GetSafeErrorSummary(ex)}"
            };
        }
    }

    private static string GetSafeErrorSummary(Exception ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return ex.GetType().Name;
        }

        var sanitized = message
            .Replace("password=", "password=***", StringComparison.OrdinalIgnoreCase)
            .Replace("pwd=", "pwd=***", StringComparison.OrdinalIgnoreCase);

        if (sanitized.Length > 160)
        {
            sanitized = sanitized[..160] + "…";
        }

        return sanitized;
    }
}
