using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Persistence.Repositories;

public sealed class MonitoringDataRepository : IMonitoringDataRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public MonitoringDataRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<int> CountActivePaperSessionsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.PaperTradingSessions.AsNoTracking()
            .CountAsync(session => session.Status == PaperSessionStatus.Running, cancellationToken);

    public Task<int> CountRunningBacktestsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.BacktestRuns.AsNoTracking()
            .CountAsync(run => run.Status == BacktestRunStatus.Running, cancellationToken);

    public Task<int> CountRunningReplaySessionsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.ReplaySessions.AsNoTracking()
            .CountAsync(session => session.Status == ReplaySessionStatus.Running, cancellationToken);

    public Task<int> CountRecentRiskRejectionsAsync(DateTime fromUtc, CancellationToken cancellationToken = default) =>
        _dbContext.RiskDecisions.AsNoTracking()
            .CountAsync(
                decision => decision.CreatedAtUtc >= fromUtc &&
                            (decision.Decision == RiskDecisionType.Rejected ||
                             decision.Decision == RiskDecisionType.EmergencyBlocked),
                cancellationToken);

    public Task<int> CountRecentAiFailuresAsync(DateTime fromUtc, CancellationToken cancellationToken = default) =>
        _dbContext.AuditLogs.AsNoTracking()
            .CountAsync(
                log => log.CreatedAtUtc >= fromUtc &&
                       (log.Action == "AI_SERVICE_UNAVAILABLE" || log.Action == AuditActions.AiServiceUnavailable),
                cancellationToken);

    public Task<DateTime?> GetLastCandleImportUtcAsync(CancellationToken cancellationToken = default) =>
        _dbContext.MarketDataImports.AsNoTracking()
            .Where(import => import.CompletedAtUtc != null)
            .OrderByDescending(import => import.CompletedAtUtc)
            .Select(import => import.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<DateTime?> GetLastIndicatorRecalculationUtcAsync(CancellationToken cancellationToken = default) =>
        _dbContext.AuditLogs.AsNoTracking()
            .Where(log => log.Action == "INDICATOR_RECALCULATED" || log.Action == AuditActions.IndicatorRecalculated)
            .OrderByDescending(log => log.CreatedAtUtc)
            .Select(log => (DateTime?)log.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<DateTime?> GetLatestCandleTimeUtcAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Candles.AsNoTracking()
            .OrderByDescending(candle => candle.OpenTimeUtc)
            .Select(candle => (DateTime?)candle.OpenTimeUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<DateTime?> GetLatestIndicatorSnapshotTimeUtcAsync(CancellationToken cancellationToken = default) =>
        _dbContext.IndicatorSnapshots.AsNoTracking()
            .OrderByDescending(snapshot => snapshot.CalculatedAtUtc)
            .Select(snapshot => (DateTime?)snapshot.CalculatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountEnabledStrategiesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Strategies.AsNoTracking().CountAsync(strategy => strategy.IsEnabled, cancellationToken);

    public Task<int> CountRiskProfilesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.RiskProfiles.AsNoTracking().CountAsync(cancellationToken);

    public async Task<int> CountOpenPaperPositionsAsync(CancellationToken cancellationToken = default)
    {
        var paperSessionIds = await _dbContext.PaperTradingSessions.AsNoTracking()
            .Select(session => session.TradingSessionId)
            .ToListAsync(cancellationToken);

        if (paperSessionIds.Count == 0)
        {
            return 0;
        }

        return await _dbContext.Positions.AsNoTracking()
            .CountAsync(
                position => paperSessionIds.Contains(position.TradingSessionId) &&
                            position.Status == PositionStatus.Open,
                cancellationToken);
    }

    public async Task<bool> GetEmergencyStopEnabledAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _dbContext.RiskRules.AsNoTracking()
            .Where(rule => rule.RuleKey == RiskRuleKeys.EmergencyStopEnabled)
            .Select(rule => rule.RuleValue)
            .ToListAsync(cancellationToken);

        return rules.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }

    public Task<bool> HasMarketDataAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Candles.AsNoTracking().AnyAsync(cancellationToken);

    public Task<bool> HasIndicatorsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.IndicatorSnapshots.AsNoTracking().AnyAsync(cancellationToken);
}
