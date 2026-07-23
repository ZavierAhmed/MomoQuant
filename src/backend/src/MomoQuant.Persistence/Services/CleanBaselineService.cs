using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Admin;
using MomoQuant.Application.Admin.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.Persistence.Services;

public sealed class CleanBaselineService : ICleanBaselineService
{
    public const string BinanceFuturesCode = "BINANCE_FUTURES";
    private const string BinanceFuturesName = "Binance Futures";
    private const string BinanceFuturesBaseUrl = "https://fapi.binance.com";
    private const string BinanceFuturesWebSocketUrl = "wss://fstream.binance.com";

    private readonly MomoQuantDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly ILogger<CleanBaselineService> _logger;

    public CleanBaselineService(
        MomoQuantDbContext dbContext,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ILogger<CleanBaselineService> logger)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<ServiceResult<CleanBaselinePreviewDto>> PreviewAsync(
        CleanBaselineRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = await BuildPreviewItemsAsync(request, cancellationToken);

        await _auditService.LogAsync(
            "CleanBaselinePreviewed",
            "CleanBaseline",
            userId: _currentUserService.UserId,
            newValueJson: BuildAuditSummary(request),
            cancellationToken: cancellationToken);

        return ServiceResult<CleanBaselinePreviewDto>.Ok(new CleanBaselinePreviewDto
        {
            Items = items,
            Warnings = BuildWarnings(request),
            Preserved = BuildPreservedList(request),
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<ServiceResult<CleanBaselineResultDto>> ExecuteAsync(
        CleanBaselineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Confirmation, CleanBaselineRequest.RequiredConfirmation, StringComparison.Ordinal))
        {
            await _auditService.LogAsync(
                "CleanBaselineFailed",
                "CleanBaseline",
                userId: _currentUserService.UserId,
                newValueJson: "{\"reason\":\"Invalid confirmation text.\"}",
                cancellationToken: cancellationToken);

            return ServiceResult<CleanBaselineResultDto>.Fail(
                $"Confirmation text must be exactly '{CleanBaselineRequest.RequiredConfirmation}'.",
                "confirmation");
        }

        if (request.RemoveMarketData && !request.RemoveSimulationData)
        {
            return ServiceResult<CleanBaselineResultDto>.Fail(
                "Removing market data requires removing simulation data first (candles are referenced by simulation records).",
                "removeMarketData");
        }

        if (request.RemoveSymbols && !request.RemoveMarketData)
        {
            return ServiceResult<CleanBaselineResultDto>.Fail(
                "Removing symbols requires removing market data first (candles reference symbols).",
                "removeSymbols");
        }

        var results = new List<CleanBaselineResultItemDto>();
        string exchangeAction;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (request.RemoveSimulationData)
            {
                await NullCircularReferencesAsync(cancellationToken);

                results.Add(await DeleteAllAsync("OrderFills", _dbContext.OrderFills, cancellationToken));
                results.Add(await DeleteAllAsync("MissedOrders", _dbContext.MissedOrders, cancellationToken));
                results.Add(await DeleteAllAsync("Trades", _dbContext.Trades, cancellationToken));
                results.Add(await DeleteAllAsync("Orders", _dbContext.Orders, cancellationToken));
                results.Add(await DeleteAllAsync("Positions", _dbContext.Positions, cancellationToken));
                results.Add(await DeleteAllAsync("RiskDecisions", _dbContext.RiskDecisions, cancellationToken));
                results.Add(await DeleteAllAsync("AiDecisions", _dbContext.AiDecisions, cancellationToken));
                results.Add(await DeleteAllAsync("StrategySignals", _dbContext.StrategySignals, cancellationToken));
                results.Add(await DeleteAllAsync("IndicatorSnapshots", _dbContext.IndicatorSnapshots, cancellationToken));

                results.Add(await DeleteAllAsync("BacktestEquityPoints", _dbContext.BacktestEquityPoints, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestStrategyResults", _dbContext.BacktestStrategyResults, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestSymbolResults", _dbContext.BacktestSymbolResults, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestResults", _dbContext.BacktestResults, cancellationToken));
                results.Add(await DeleteAllAsync("BacktestRuns", _dbContext.BacktestRuns, cancellationToken));

                results.Add(await DeleteAllAsync("StrategyBenchmarkRunItems", _dbContext.StrategyBenchmarkRunItems, cancellationToken));
                results.Add(await DeleteAllAsync("StrategyBenchmarkResults", _dbContext.StrategyBenchmarkResults, cancellationToken));
                results.Add(await DeleteAllAsync("StrategyBenchmarkRuns", _dbContext.StrategyBenchmarkRuns, cancellationToken));

                results.Add(await DeleteAllAsync("ReplayFrames", _dbContext.ReplayFrames, cancellationToken));
                results.Add(await DeleteAllAsync("ReplaySessions", _dbContext.ReplaySessions, cancellationToken));

                results.Add(await DeleteAllAsync("PaperAccountSnapshots", _dbContext.PaperAccountSnapshots, cancellationToken));
                results.Add(await DeleteAllAsync("PaperTradingSessions", _dbContext.PaperTradingSessions, cancellationToken));
                results.Add(await DeleteAllAsync("PaperAccounts", _dbContext.PaperAccounts, cancellationToken));

                results.Add(await DeleteAllAsync("TradingSessionSymbols", _dbContext.TradingSessionSymbols, cancellationToken));
                results.Add(await DeleteAllAsync("TradingSessions", _dbContext.TradingSessions, cancellationToken));
            }

            if (request.RemoveMarketData)
            {
                results.Add(await DeleteAllAsync("Candles", _dbContext.Candles, cancellationToken));
                results.Add(await DeleteAllAsync("MarketDataImports", _dbContext.MarketDataImports, cancellationToken));
            }

            if (request.RemoveReports)
            {
                results.Add(await DeleteAllAsync("SimulationRunSummaries", _dbContext.SimulationRunSummaries, cancellationToken));
            }

            if (request.RemoveStrategies)
            {
                results.Add(await DeleteAllAsync("StrategyParameters", _dbContext.StrategyParameters, cancellationToken));
                results.Add(await DeleteAllAsync("Strategies", _dbContext.Strategies, cancellationToken));
            }

            if (request.RemoveSymbols)
            {
                results.Add(await DeleteAllAsync("Symbols", _dbContext.Symbols, cancellationToken));
            }

            exchangeAction = await EnsureSingleBinanceFuturesExchangeAsync(request, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Clean baseline reset failed and was rolled back.");

            await _auditService.LogAsync(
                "CleanBaselineFailed",
                "CleanBaseline",
                userId: _currentUserService.UserId,
                newValueJson: "{\"reason\":\"Execution failed and was rolled back.\"}",
                cancellationToken: cancellationToken);

            return ServiceResult<CleanBaselineResultDto>.Fail(
                "Clean baseline reset failed and was rolled back. No data was changed.");
        }

        var completedAtUtc = DateTime.UtcNow;

        await _auditService.LogAsync(
            "CleanBaselineExecuted",
            "CleanBaseline",
            userId: _currentUserService.UserId,
            newValueJson: BuildAuditSummary(request),
            cancellationToken: cancellationToken);

        _logger.LogWarning(
            "Clean baseline reset executed by user {UserId}. Exchange action: {ExchangeAction}.",
            _currentUserService.UserId,
            exchangeAction);

        return ServiceResult<CleanBaselineResultDto>.Ok(new CleanBaselineResultDto
        {
            Items = results,
            Warnings = BuildWarnings(request),
            BinanceFuturesExchangeAction = exchangeAction,
            CompletedAtUtc = completedAtUtc
        });
    }

    private async Task<string> EnsureSingleBinanceFuturesExchangeAsync(
        CleanBaselineRequest request,
        CancellationToken cancellationToken)
    {
        var exchanges = await _dbContext.Exchanges
            .OrderBy(exchange => exchange.Id)
            .ToListAsync(cancellationToken);

        var canonical = exchanges
            .FirstOrDefault(exchange => string.Equals(exchange.Code, BinanceFuturesCode, StringComparison.OrdinalIgnoreCase));

        var now = DateTime.UtcNow;

        // Remove any exchange that is not the canonical Binance Futures record.
        foreach (var exchange in exchanges)
        {
            if (canonical is not null && exchange.Id == canonical.Id)
            {
                continue;
            }

            _dbContext.Exchanges.Remove(exchange);
        }

        string action;

        if (canonical is null)
        {
            canonical = new Exchange
            {
                Name = BinanceFuturesName,
                Code = BinanceFuturesCode,
                BaseUrl = BinanceFuturesBaseUrl,
                WebSocketUrl = BinanceFuturesWebSocketUrl,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _dbContext.Exchanges.Add(canonical);
            action = "Created Binance Futures exchange.";
        }
        else
        {
            canonical.Name = BinanceFuturesName;
            canonical.BaseUrl = BinanceFuturesBaseUrl;
            canonical.WebSocketUrl = BinanceFuturesWebSocketUrl;
            canonical.IsActive = true;
            canonical.UpdatedAtUtc = now;
            _dbContext.Exchanges.Update(canonical);
            action = exchanges.Count > 1
                ? "Updated Binance Futures exchange and removed duplicate exchanges."
                : "Updated Binance Futures exchange.";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return action;
    }

    private async Task NullCircularReferencesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Orders
            .Where(order => order.TradeId != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.TradeId, (long?)null), cancellationToken);

        await _dbContext.Trades
            .Where(trade => trade.EntryOrderId != null || trade.ExitOrderId != null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(trade => trade.EntryOrderId, (long?)null)
                    .SetProperty(trade => trade.ExitOrderId, (long?)null),
                cancellationToken);

        await _dbContext.ReplaySessions
            .Where(session => session.CurrentCandleId != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.CurrentCandleId, (long?)null),
                cancellationToken);
    }

    private async Task<List<CleanBaselinePreviewItemDto>> BuildPreviewItemsAsync(
        CleanBaselineRequest request,
        CancellationToken cancellationToken)
    {
        return
        [
            await PreviewCountAsync("OrderFills", _dbContext.OrderFills, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("MissedOrders", _dbContext.MissedOrders, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("Trades", _dbContext.Trades, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("Orders", _dbContext.Orders, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("Positions", _dbContext.Positions, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("RiskDecisions", _dbContext.RiskDecisions, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("AiDecisions", _dbContext.AiDecisions, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("StrategySignals", _dbContext.StrategySignals, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("IndicatorSnapshots", _dbContext.IndicatorSnapshots, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("BacktestRuns", _dbContext.BacktestRuns, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("BacktestResults", _dbContext.BacktestResults, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("StrategyBenchmarkRuns", _dbContext.StrategyBenchmarkRuns, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("StrategyBenchmarkResults", _dbContext.StrategyBenchmarkResults, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("StrategyBenchmarkRunItems", _dbContext.StrategyBenchmarkRunItems, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("ReplaySessions", _dbContext.ReplaySessions, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("ReplayFrames", _dbContext.ReplayFrames, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("PaperTradingSessions", _dbContext.PaperTradingSessions, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("PaperAccounts", _dbContext.PaperAccounts, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("PaperAccountSnapshots", _dbContext.PaperAccountSnapshots, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("TradingSessions", _dbContext.TradingSessions, request.RemoveSimulationData, cancellationToken),
            await PreviewCountAsync("Candles", _dbContext.Candles, request.RemoveMarketData, cancellationToken),
            await PreviewCountAsync("MarketDataImports", _dbContext.MarketDataImports, request.RemoveMarketData, cancellationToken),
            await PreviewCountAsync("SimulationRunSummaries", _dbContext.SimulationRunSummaries, request.RemoveReports, cancellationToken),
            await PreviewCountAsync("StrategyParameters", _dbContext.StrategyParameters, request.RemoveStrategies, cancellationToken),
            await PreviewCountAsync("Strategies", _dbContext.Strategies, request.RemoveStrategies, cancellationToken),
            await PreviewCountAsync("Symbols", _dbContext.Symbols, request.RemoveSymbols, cancellationToken)
        ];
    }

    private static async Task<CleanBaselinePreviewItemDto> PreviewCountAsync<TEntity>(
        string entityName,
        IQueryable<TEntity> query,
        bool willDelete,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var count = await query.CountAsync(cancellationToken);
        return new CleanBaselinePreviewItemDto
        {
            EntityName = entityName,
            Count = count,
            WillDelete = willDelete
        };
    }

    private static async Task<CleanBaselineResultItemDto> DeleteAllAsync<TEntity>(
        string entityName,
        DbSet<TEntity> dbSet,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var before = await dbSet.CountAsync(cancellationToken);
        var deleted = await dbSet.ExecuteDeleteAsync(cancellationToken);
        return new CleanBaselineResultItemDto
        {
            EntityName = entityName,
            CountBefore = before,
            CountDeleted = deleted,
            CountAfter = before - deleted
        };
    }

    private static IReadOnlyList<string> BuildWarnings(CleanBaselineRequest request)
    {
        var warnings = new List<string>
        {
            "This operation permanently deletes experimental data and cannot be undone.",
            "Simulation only — this does not affect any real exchange account.",
            "The Admin user, roles, user-role links, application settings, and risk profiles are preserved."
        };

        if (request.PreserveBinanceFuturesExchange)
        {
            warnings.Add("Exactly one Binance Futures exchange will remain after cleanup. Duplicate/other exchanges are removed.");
        }

        if (request.RemoveSymbols)
        {
            warnings.Add("All symbols will be removed. Use Binance Futures symbol discovery to add symbols again.");
        }

        if (request.RemoveStrategies)
        {
            warnings.Add("All strategies will be removed. Automatic strategy seeding is disabled.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildPreservedList(CleanBaselineRequest request) =>
    [
        "Admin user",
        "Roles and user-role links",
        "Application settings (AppSettings)",
        "Risk profiles and risk rules",
        "Audit logs",
        request.PreserveBinanceFuturesExchange
            ? "One Binance Futures exchange"
            : "No exchange preserved"
    ];

    private static string BuildAuditSummary(CleanBaselineRequest request) =>
        $"{{\"preserveAdminUser\":{Lower(request.PreserveAdminUser)},\"preserveBinanceFuturesExchange\":{Lower(request.PreserveBinanceFuturesExchange)},\"removeStrategies\":{Lower(request.RemoveStrategies)},\"removeSymbols\":{Lower(request.RemoveSymbols)},\"removeSimulationData\":{Lower(request.RemoveSimulationData)},\"removeReports\":{Lower(request.RemoveReports)},\"removeMarketData\":{Lower(request.RemoveMarketData)}}}";

    private static string Lower(bool value) => value.ToString().ToLowerInvariant();
}
