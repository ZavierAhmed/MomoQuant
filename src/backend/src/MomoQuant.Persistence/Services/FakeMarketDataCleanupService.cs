using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Admin;
using MomoQuant.Application.Admin.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Services;

public sealed class FakeMarketDataCleanupService : IFakeMarketDataCleanupService
{
    private static readonly TradingMode[] SimulationModes =
    [
        TradingMode.Backtest,
        TradingMode.Replay,
        TradingMode.Paper
    ];

    private readonly MomoQuantDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly ILogger<FakeMarketDataCleanupService> _logger;

    public FakeMarketDataCleanupService(
        MomoQuantDbContext dbContext,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ILogger<FakeMarketDataCleanupService> logger)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<ServiceResult<FakeMarketDataCleanupPreviewDto>> PreviewAsync(
        FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = await BuildPreviewItemsAsync(request, cancellationToken);
        var warnings = BuildWarnings(request);

        await _auditService.LogAsync(
            "FakeMarketDataCleanupPreviewed",
            "FakeMarketDataCleanup",
            userId: _currentUserService.UserId,
            newValueJson: BuildAuditSummary(request),
            cancellationToken: cancellationToken);

        return ServiceResult<FakeMarketDataCleanupPreviewDto>.Ok(new FakeMarketDataCleanupPreviewDto
        {
            Items = items,
            Warnings = warnings,
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<ServiceResult<FakeMarketDataCleanupResultDto>> ExecuteAsync(
        FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Confirmation, FakeMarketDataCleanupRequest.RequiredConfirmation, StringComparison.Ordinal))
        {
            await _auditService.LogAsync(
                "FakeMarketDataCleanupFailed",
                "FakeMarketDataCleanup",
                userId: _currentUserService.UserId,
                newValueJson: "{\"reason\":\"Invalid confirmation text.\"}",
                cancellationToken: cancellationToken);

            return ServiceResult<FakeMarketDataCleanupResultDto>.Fail(
                "Confirmation text is invalid.",
                "confirmation");
        }

        var warnings = BuildWarnings(request);
        var results = new List<FakeMarketDataCleanupResultItemDto>();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            results.Add(await DeleteSimulationOrderFillsAsync(cancellationToken));
            results.Add(await DeleteAllAsync("MissedOrders", _dbContext.MissedOrders, cancellationToken));

            await BreakOrderTradeReferencesAsync(cancellationToken);

            results.Add(await DeleteSimulationTradesAsync(cancellationToken));
            results.Add(await DeleteSimulationOrdersAsync(cancellationToken));
            results.Add(await DeleteSimulationPositionsAsync(cancellationToken));

            if (request.IncludeRiskDecisions)
            {
                results.Add(await DeleteAllAsync("RiskDecisions", _dbContext.RiskDecisions, cancellationToken));
            }

            if (request.IncludeAiDecisions)
            {
                results.Add(await DeleteAllAsync("AiDecisions", _dbContext.AiDecisions, cancellationToken));
            }

            results.Add(await DeleteAllAsync("StrategySignals", _dbContext.StrategySignals, cancellationToken));
            results.Add(await DeleteAllAsync("IndicatorSnapshots", _dbContext.IndicatorSnapshots, cancellationToken));

            if (request.IncludeBacktests)
            {
                results.Add(await DeleteAllAsync("BacktestEquityPoints", _dbContext.BacktestEquityPoints, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestStrategyResults", _dbContext.BacktestStrategyResults, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestSymbolResults", _dbContext.BacktestSymbolResults, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestResults", _dbContext.BacktestResults, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestRuns", _dbContext.BacktestRuns, cancellationToken));
            }

            if (request.IncludeReplay)
            {
                results.Add(await DeleteAllAsync("ReplayFrames", _dbContext.ReplayFrames, cancellationToken));
                await NullReplayCurrentCandleReferencesAsync(cancellationToken);
                results.Add(await DeleteAllAsync("ReplaySessions", _dbContext.ReplaySessions, cancellationToken));
            }

            if (request.IncludePaperTrading)
            {
                results.Add(await DeleteAllAsync("PaperAccountSnapshots", _dbContext.PaperAccountSnapshots, cancellationToken));
                results.Add(await DeleteAllAsync("PaperTradingSessions", _dbContext.PaperTradingSessions, cancellationToken));

                if (request.ResetPaperAccounts)
                {
                    results.Add(await DeleteAllAsync("PaperAccounts", _dbContext.PaperAccounts, cancellationToken));
                }
            }

            await NullReplayCurrentCandleReferencesAsync(cancellationToken);
            results.Add(await DeleteAllAsync("Candles", _dbContext.Candles, cancellationToken));
            results.Add(await DeleteAllAsync("MarketDataImports", _dbContext.MarketDataImports, cancellationToken));

            if (request.IncludeAuditLogs)
            {
                results.Add(await DeleteAllAsync("AuditLogs", _dbContext.AuditLogs, cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);

            var completedAtUtc = DateTime.UtcNow;
            await _auditService.LogAsync(
                "FakeMarketDataCleanupExecuted",
                "FakeMarketDataCleanup",
                userId: _currentUserService.UserId,
                newValueJson: BuildAuditSummary(request),
                cancellationToken: cancellationToken);

            return ServiceResult<FakeMarketDataCleanupResultDto>.Ok(new FakeMarketDataCleanupResultDto
            {
                Items = results,
                Warnings = warnings,
                CompletedAtUtc = completedAtUtc
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Fake market data cleanup failed.");

            await _auditService.LogAsync(
                "FakeMarketDataCleanupFailed",
                "FakeMarketDataCleanup",
                userId: _currentUserService.UserId,
                newValueJson: "{\"reason\":\"Execution failed.\"}",
                cancellationToken: cancellationToken);

            return ServiceResult<FakeMarketDataCleanupResultDto>.Fail("Fake market data cleanup failed.");
        }
    }

    private async Task<List<FakeMarketDataCleanupPreviewItemDto>> BuildPreviewItemsAsync(
        FakeMarketDataCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var simulationOrderIds = SimulationOrderIdsQuery();
        var simulationSessionIds = SimulationSessionIdsQuery();

        return
        [
            await PreviewCountAsync("OrderFills", _dbContext.OrderFills.Where(fill => simulationOrderIds.Contains(fill.OrderId)), true, cancellationToken),
            await PreviewCountAsync("MissedOrders", _dbContext.MissedOrders, true, cancellationToken),
            await PreviewCountAsync("Trades", _dbContext.Trades.Where(trade => simulationSessionIds.Contains(trade.TradingSessionId)), true, cancellationToken),
            await PreviewCountAsync("Orders", _dbContext.Orders.Where(order => SimulationModes.Contains(order.Mode)), true, cancellationToken),
            await PreviewCountAsync("Positions", _dbContext.Positions.Where(position => simulationSessionIds.Contains(position.TradingSessionId)), true, cancellationToken),
            await PreviewCountAsync("RiskDecisions", _dbContext.RiskDecisions, request.IncludeRiskDecisions, cancellationToken),
            await PreviewCountAsync("AiDecisions", _dbContext.AiDecisions, request.IncludeAiDecisions, cancellationToken),
            await PreviewCountAsync("StrategySignals", _dbContext.StrategySignals, true, cancellationToken),
            await PreviewCountAsync("IndicatorSnapshots", _dbContext.IndicatorSnapshots, true, cancellationToken),
            await PreviewCountAsync("BacktestEquityPoints", _dbContext.BacktestEquityPoints, request.IncludeBacktests, cancellationToken),
            await PreviewCountAsync("BacktestStrategyResults", _dbContext.BacktestStrategyResults, request.IncludeBacktests, cancellationToken),
            await PreviewCountAsync("BacktestSymbolResults", _dbContext.BacktestSymbolResults, request.IncludeBacktests, cancellationToken),
            await PreviewCountAsync("BacktestResults", _dbContext.BacktestResults, request.IncludeBacktests, cancellationToken),
            await PreviewCountAsync("BacktestRuns", _dbContext.BacktestRuns, request.IncludeBacktests, cancellationToken),
            await PreviewCountAsync("ReplayFrames", _dbContext.ReplayFrames, request.IncludeReplay, cancellationToken),
            await PreviewCountAsync("ReplaySessions", _dbContext.ReplaySessions, request.IncludeReplay, cancellationToken),
            await PreviewCountAsync("PaperAccountSnapshots", _dbContext.PaperAccountSnapshots, request.IncludePaperTrading, cancellationToken),
            await PreviewCountAsync("PaperTradingSessions", _dbContext.PaperTradingSessions, request.IncludePaperTrading, cancellationToken),
            await PreviewCountAsync("PaperAccounts", _dbContext.PaperAccounts, request.IncludePaperTrading && request.ResetPaperAccounts, cancellationToken),
            await PreviewCountAsync("Candles", _dbContext.Candles, true, cancellationToken),
            await PreviewCountAsync("MarketDataImports", _dbContext.MarketDataImports, true, cancellationToken),
            await PreviewCountAsync("AuditLogs", _dbContext.AuditLogs, request.IncludeAuditLogs, cancellationToken)
        ];
    }

    private IQueryable<long> SimulationOrderIdsQuery() =>
        _dbContext.Orders
            .Where(order => SimulationModes.Contains(order.Mode))
            .Select(order => order.Id);

    private IQueryable<long> SimulationSessionIdsQuery() =>
        _dbContext.TradingSessions
            .Where(session => SimulationModes.Contains(session.Mode))
            .Select(session => session.Id);

    private static async Task<FakeMarketDataCleanupPreviewItemDto> PreviewCountAsync<TEntity>(
        string entityName,
        IQueryable<TEntity> query,
        bool willDelete,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var count = await query.CountAsync(cancellationToken);
        return new FakeMarketDataCleanupPreviewItemDto
        {
            EntityName = entityName,
            Count = count,
            WillDelete = willDelete
        };
    }

    private static async Task<FakeMarketDataCleanupResultItemDto> DeleteAllAsync<TEntity>(
        string entityName,
        DbSet<TEntity> dbSet,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var before = await dbSet.CountAsync(cancellationToken);
        var deleted = await dbSet.ExecuteDeleteAsync(cancellationToken);
        return new FakeMarketDataCleanupResultItemDto
        {
            EntityName = entityName,
            CountBefore = before,
            CountDeleted = deleted,
            CountAfter = before - deleted
        };
    }

    private async Task<FakeMarketDataCleanupResultItemDto> DeleteSimulationOrderFillsAsync(CancellationToken cancellationToken)
    {
        var simulationOrderIds = SimulationOrderIdsQuery();
        var query = _dbContext.OrderFills.Where(fill => simulationOrderIds.Contains(fill.OrderId));
        var before = await query.CountAsync(cancellationToken);
        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        return new FakeMarketDataCleanupResultItemDto
        {
            EntityName = "OrderFills",
            CountBefore = before,
            CountDeleted = deleted,
            CountAfter = before - deleted
        };
    }

    private async Task<FakeMarketDataCleanupResultItemDto> DeleteSimulationTradesAsync(CancellationToken cancellationToken)
    {
        var simulationSessionIds = SimulationSessionIdsQuery();
        var query = _dbContext.Trades.Where(trade => simulationSessionIds.Contains(trade.TradingSessionId));
        var before = await query.CountAsync(cancellationToken);
        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        return new FakeMarketDataCleanupResultItemDto
        {
            EntityName = "Trades",
            CountBefore = before,
            CountDeleted = deleted,
            CountAfter = before - deleted
        };
    }

    private async Task<FakeMarketDataCleanupResultItemDto> DeleteSimulationOrdersAsync(CancellationToken cancellationToken)
    {
        var query = _dbContext.Orders.Where(order => SimulationModes.Contains(order.Mode));
        var before = await query.CountAsync(cancellationToken);
        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        return new FakeMarketDataCleanupResultItemDto
        {
            EntityName = "Orders",
            CountBefore = before,
            CountDeleted = deleted,
            CountAfter = before - deleted
        };
    }

    private async Task<FakeMarketDataCleanupResultItemDto> DeleteSimulationPositionsAsync(CancellationToken cancellationToken)
    {
        var simulationSessionIds = SimulationSessionIdsQuery();
        var query = _dbContext.Positions.Where(position => simulationSessionIds.Contains(position.TradingSessionId));
        var before = await query.CountAsync(cancellationToken);
        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        return new FakeMarketDataCleanupResultItemDto
        {
            EntityName = "Positions",
            CountBefore = before,
            CountDeleted = deleted,
            CountAfter = before - deleted
        };
    }

    private async Task BreakOrderTradeReferencesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Orders
            .Where(order => SimulationModes.Contains(order.Mode) && order.TradeId != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(order => order.TradeId, (long?)null),
                cancellationToken);

        var simulationSessionIds = SimulationSessionIdsQuery();
        await _dbContext.Trades
            .Where(trade =>
                simulationSessionIds.Contains(trade.TradingSessionId) &&
                (trade.EntryOrderId != null || trade.ExitOrderId != null))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(trade => trade.EntryOrderId, (long?)null)
                    .SetProperty(trade => trade.ExitOrderId, (long?)null),
                cancellationToken);
    }

    private Task NullReplayCurrentCandleReferencesAsync(CancellationToken cancellationToken) =>
        _dbContext.ReplaySessions
            .Where(session => session.CurrentCandleId != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.CurrentCandleId, (long?)null),
                cancellationToken);

    private static IReadOnlyList<string> BuildWarnings(FakeMarketDataCleanupRequest request)
    {
        var warnings = new List<string>
        {
            "This operation removes fake/demo market data and generated simulation results.",
            "Users, roles, exchanges, symbols, strategies, strategy parameters, risk profiles, and risk rules are preserved."
        };

        if (!request.ResetPaperAccounts && request.IncludePaperTrading)
        {
            warnings.Add("Paper accounts are preserved unless reset paper accounts is enabled.");
        }

        if (!request.IncludeAuditLogs)
        {
            warnings.Add("Audit logs are preserved unless explicitly included.");
        }

        return warnings;
    }

    private static string BuildAuditSummary(FakeMarketDataCleanupRequest request) =>
        $"{{\"includeBacktests\":{request.IncludeBacktests.ToString().ToLowerInvariant()},\"includeReplay\":{request.IncludeReplay.ToString().ToLowerInvariant()},\"includePaperTrading\":{request.IncludePaperTrading.ToString().ToLowerInvariant()},\"includeAiDecisions\":{request.IncludeAiDecisions.ToString().ToLowerInvariant()},\"includeRiskDecisions\":{request.IncludeRiskDecisions.ToString().ToLowerInvariant()},\"includeAuditLogs\":{request.IncludeAuditLogs.ToString().ToLowerInvariant()},\"resetPaperAccounts\":{request.ResetPaperAccounts.ToString().ToLowerInvariant()}}}";
}
