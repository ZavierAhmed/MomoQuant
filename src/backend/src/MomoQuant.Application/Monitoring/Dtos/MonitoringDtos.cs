using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring.Dtos;

public sealed class MonitoringQuery
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Severity { get; init; }
    public string? Subsystem { get; init; }
    public string? EventType { get; init; }
    public long? UserId { get; init; }
    public string? Mode { get; init; }
    public int? Limit { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed class ComponentHealthDto
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public int? LatencyMs { get; init; }
    public required string Message { get; init; }
}

public sealed class HealthResponseDto
{
    public required string Status { get; init; }
    public DateTime CheckedAtUtc { get; init; }
    public required IReadOnlyList<ComponentHealthDto> Components { get; init; }
}

public sealed class SystemStatusDto
{
    public required string ApiStatus { get; init; }
    public required string DatabaseStatus { get; init; }
    public required string RedisStatus { get; init; }
    public required string AiServiceStatus { get; init; }
    public int ActivePaperSessions { get; init; }
    public int RunningBacktests { get; init; }
    public int RunningReplaySessions { get; init; }
    public int RecentCriticalErrors { get; init; }
    public int RecentAiFailures { get; init; }
    public int RecentRiskRejections { get; init; }
    public DateTime? LastCandleImportUtc { get; init; }
    public DateTime? LastIndicatorRecalculationUtc { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class SystemHealthLogDto
{
    public long Id { get; init; }
    public required string Subsystem { get; init; }
    public required string Status { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? DetailsJson { get; init; }
    public int? LatencyMs { get; init; }
    public DateTime CheckedAtUtc { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class RecentErrorDto
{
    public long Id { get; init; }
    public required string Source { get; init; }
    public required string Subsystem { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

public sealed class RecentEventDto
{
    public long Id { get; init; }
    public required string EventType { get; init; }
    public required string Subsystem { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

public sealed class SafetyEventDto
{
    public long Id { get; init; }
    public required string EventType { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public long? UserId { get; init; }
    public string? UserEmail { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

public sealed class TradingPipelineStatusDto
{
    public bool MarketDataAvailable { get; init; }
    public bool IndicatorsAvailable { get; init; }
    public int StrategiesEnabled { get; init; }
    public bool RiskProfilesAvailable { get; init; }
    public bool AiServiceAvailable { get; init; }
    public bool BacktestingAvailable { get; init; }
    public bool ReplayAvailable { get; init; }
    public bool PaperTradingAvailable { get; init; }
    public DateTime? LatestCandleTimeUtc { get; init; }
    public DateTime? LatestIndicatorSnapshotTimeUtc { get; init; }
    public int OpenPaperPositions { get; init; }
    public bool EmergencyStopEnabled { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class MonitoringQueryFilter
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public LogSeverity? Severity { get; init; }
    public MonitoringSubsystem? Subsystem { get; init; }
    public string? EventType { get; init; }
    public long? UserId { get; init; }
    public TradingMode? Mode { get; init; }
    public int Limit { get; init; } = 50;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
