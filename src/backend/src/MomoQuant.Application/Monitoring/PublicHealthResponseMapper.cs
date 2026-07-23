using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring;

/// <summary>
/// Maps core component health into the anonymous public /api/health payload shape.
/// Never includes messages, connection strings, or stack traces.
/// </summary>
public static class PublicHealthResponseMapper
{
    public sealed class PublicHealthComponentDto
    {
        public required string Status { get; init; }
        public int? DurationMs { get; init; }
    }

    public sealed class PublicHealthComponentsDto
    {
        public required PublicHealthComponentDto Mysql { get; init; }
        public required PublicHealthComponentDto Redis { get; init; }
    }

    public sealed class PublicHealthResponseDto
    {
        public required string Status { get; init; }
        public required string Application { get; init; }
        public DateTime CheckedAtUtc { get; init; }
        public DateTime TimestampUtc { get; init; }
        public required PublicHealthComponentsDto Components { get; init; }
    }

    public static PublicHealthResponseDto Map(
        string applicationName,
        ComponentHealthDto? mysql,
        ComponentHealthDto? redis,
        DateTime? checkedAtUtc = null)
    {
        var mysqlStatus = ParseStatus(mysql?.Status);
        var redisStatus = ParseStatus(redis?.Status);
        var overall = MonitoringHealthMapper.AggregateCoreHealth(mysqlStatus, [redisStatus]);
        var at = checkedAtUtc ?? DateTime.UtcNow;

        return new PublicHealthResponseDto
        {
            Status = MonitoringHealthMapper.ToStatusString(overall),
            Application = applicationName,
            CheckedAtUtc = at,
            TimestampUtc = at,
            Components = new PublicHealthComponentsDto
            {
                Mysql = new PublicHealthComponentDto
                {
                    Status = MonitoringHealthMapper.ToStatusString(mysqlStatus),
                    DurationMs = mysql?.LatencyMs
                },
                Redis = new PublicHealthComponentDto
                {
                    Status = MonitoringHealthMapper.ToStatusString(redisStatus),
                    DurationMs = redis?.LatencyMs
                }
            }
        };
    }

    private static SystemHealthStatus ParseStatus(string? status) =>
        Enum.TryParse<SystemHealthStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : SystemHealthStatus.Unknown;
}
