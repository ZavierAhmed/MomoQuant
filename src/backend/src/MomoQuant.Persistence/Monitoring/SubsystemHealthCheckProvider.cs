using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Monitoring;

public sealed class SubsystemHealthCheckProvider : ISubsystemHealthCheckProvider
{
    private readonly MomoQuantDbContext _dbContext;
    private readonly IMonitoringDataRepository _monitoringDataRepository;
    private readonly ILogger<SubsystemHealthCheckProvider> _logger;

    public SubsystemHealthCheckProvider(
        MomoQuantDbContext dbContext,
        IMonitoringDataRepository monitoringDataRepository,
        ILogger<SubsystemHealthCheckProvider> logger)
    {
        _dbContext = dbContext;
        _monitoringDataRepository = monitoringDataRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HealthCheckResult>> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return new[]
            {
                await CheckMarketDataAsync(cancellationToken),
                await CheckIndicatorsAsync(cancellationToken),
                await CheckStrategiesAsync(cancellationToken),
                await CheckRiskAsync(cancellationToken),
                await CheckBacktestingAsync(cancellationToken),
                CheckReplay(),
                await CheckPaperTradingAsync(cancellationToken)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subsystem health checks failed.");
            return
            [
                new HealthCheckResult
                {
                    Name = "Subsystems",
                    Subsystem = MonitoringSubsystem.Unknown,
                    Status = SystemHealthStatus.Unhealthy,
                    Severity = LogSeverity.Error,
                    Message = "Subsystem health checks failed."
                }
            ];
        }
    }

    private async Task<HealthCheckResult> CheckMarketDataAsync(CancellationToken cancellationToken)
    {
        var hasData = await _monitoringDataRepository.HasMarketDataAsync(cancellationToken);
        return new HealthCheckResult
        {
            Name = "Market Data",
            Subsystem = MonitoringSubsystem.MarketData,
            Status = hasData ? SystemHealthStatus.Healthy : SystemHealthStatus.Degraded,
            Severity = hasData ? LogSeverity.Info : LogSeverity.Warning,
            Message = hasData ? "Market data is available" : "No market data found"
        };
    }

    private async Task<HealthCheckResult> CheckIndicatorsAsync(CancellationToken cancellationToken)
    {
        var hasData = await _monitoringDataRepository.HasIndicatorsAsync(cancellationToken);
        return new HealthCheckResult
        {
            Name = "Indicators",
            Subsystem = MonitoringSubsystem.Indicators,
            Status = hasData ? SystemHealthStatus.Healthy : SystemHealthStatus.Degraded,
            Severity = hasData ? LogSeverity.Info : LogSeverity.Warning,
            Message = hasData ? "Indicator snapshots are available" : "No indicator snapshots found"
        };
    }

    private async Task<HealthCheckResult> CheckStrategiesAsync(CancellationToken cancellationToken)
    {
        var enabledCount = await _monitoringDataRepository.CountEnabledStrategiesAsync(cancellationToken);
        return new HealthCheckResult
        {
            Name = "Strategies",
            Subsystem = MonitoringSubsystem.Strategies,
            Status = enabledCount > 0 ? SystemHealthStatus.Healthy : SystemHealthStatus.Degraded,
            Severity = enabledCount > 0 ? LogSeverity.Info : LogSeverity.Warning,
            Message = enabledCount > 0 ? $"{enabledCount} strategies enabled" : "No enabled strategies"
        };
    }

    private async Task<HealthCheckResult> CheckRiskAsync(CancellationToken cancellationToken)
    {
        var profileCount = await _monitoringDataRepository.CountRiskProfilesAsync(cancellationToken);
        var emergencyStop = await _monitoringDataRepository.GetEmergencyStopEnabledAsync(cancellationToken);
        return new HealthCheckResult
        {
            Name = "Risk",
            Subsystem = MonitoringSubsystem.Risk,
            Status = profileCount > 0 ? SystemHealthStatus.Healthy : SystemHealthStatus.Degraded,
            Severity = emergencyStop ? LogSeverity.Warning : LogSeverity.Info,
            Message = profileCount > 0
                ? emergencyStop ? "Risk profiles available; emergency stop enabled" : "Risk profiles available"
                : "No risk profiles configured"
        };
    }

    private async Task<HealthCheckResult> CheckBacktestingAsync(CancellationToken cancellationToken)
    {
        var failedRecent = await _dbContext.BacktestRuns.AsNoTracking()
            .CountAsync(
                run => run.Status == BacktestRunStatus.Failed &&
                       (run.UpdatedAtUtc ?? run.CompletedAtUtc ?? run.CreatedAtUtc) >= DateTime.UtcNow.AddDays(-1),
                cancellationToken);

        return new HealthCheckResult
        {
            Name = "Backtesting",
            Subsystem = MonitoringSubsystem.Backtesting,
            Status = failedRecent > 0 ? SystemHealthStatus.Degraded : SystemHealthStatus.Healthy,
            Severity = failedRecent > 0 ? LogSeverity.Warning : LogSeverity.Info,
            Message = failedRecent > 0 ? $"{failedRecent} recent backtest failures" : "Backtesting subsystem available"
        };
    }

    private static HealthCheckResult CheckReplay() => new()
    {
        Name = "Replay",
        Subsystem = MonitoringSubsystem.Replay,
        Status = SystemHealthStatus.Healthy,
        Severity = LogSeverity.Info,
        Message = "Replay subsystem available"
    };

    private async Task<HealthCheckResult> CheckPaperTradingAsync(CancellationToken cancellationToken)
    {
        var activeSessions = await _monitoringDataRepository.CountActivePaperSessionsAsync(cancellationToken);
        return new HealthCheckResult
        {
            Name = "Paper Trading",
            Subsystem = MonitoringSubsystem.PaperTrading,
            Status = SystemHealthStatus.Healthy,
            Severity = LogSeverity.Info,
            Message = activeSessions > 0 ? $"{activeSessions} paper sessions running" : "Paper trading subsystem available"
        };
    }
}
