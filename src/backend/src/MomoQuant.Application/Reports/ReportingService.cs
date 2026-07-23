using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Reports;

public interface IReportingService
{
    Task<ServiceResult<OverviewReportDto>> GetOverviewAsync(ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class ReportingService : IReportingService
{
    private readonly IReportQueryValidator _queryValidator;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyRepository _strategyRepository;

    public ReportingService(
        IReportQueryValidator queryValidator,
        IReportDataRepository reportDataRepository,
        ISymbolRepository symbolRepository,
        IStrategyRepository strategyRepository)
    {
        _queryValidator = queryValidator;
        _reportDataRepository = reportDataRepository;
        _symbolRepository = symbolRepository;
        _strategyRepository = strategyRepository;
    }

    public async Task<ServiceResult<OverviewReportDto>> GetOverviewAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<OverviewReportDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        var filter = validation.Data;
        var trades = await _reportDataRepository.GetTradesAsync(filter, cancellationToken);
        var orders = await _reportDataRepository.GetOrdersAsync(filter, cancellationToken);
        var signals = await _reportDataRepository.GetSignalsAsync(filter, cancellationToken);
        var riskDecisions = await _reportDataRepository.GetRiskDecisionsAsync(filter, cancellationToken);
        var aiDecisions = await _reportDataRepository.GetAiDecisionsAsync(filter, cancellationToken);
        var missedOrders = await _reportDataRepository.GetMissedOrdersAsync(filter, cancellationToken);

        var closedTrades = trades.Where(trade => trade.Status == TradeStatus.Closed).ToList();
        var tradeAnalysis = ReportMetrics.AnalyzeTrades(closedTrades);
        var totalNetPnl = closedTrades.Sum(trade => trade.NetPnl);
        var totalFees = closedTrades.Sum(trade => trade.Fees);

        var strategyStats = await BuildStrategyRankingsAsync(trades, cancellationToken);
        var symbolStats = await BuildSymbolRankingsAsync(trades, cancellationToken);

        var profitFactors = strategyStats.Values
            .Select(stat => ReportMetrics.CalculateProfitFactor(stat.GrossProfit, stat.GrossLoss))
            .Where(value => value > 0)
            .ToList();

        return ServiceResult<OverviewReportDto>.Ok(new OverviewReportDto
        {
            TotalBacktests = await _reportDataRepository.CountBacktestRunsAsync(filter, cancellationToken),
            TotalPaperSessions = await _reportDataRepository.CountPaperSessionsAsync(filter, cancellationToken),
            TotalTrades = closedTrades.Count,
            TotalOrders = orders.Count,
            TotalSignals = signals.Count,
            TotalRiskDecisions = riskDecisions.Count,
            TotalAiDecisions = aiDecisions.Count,
            TotalMissedOrders = missedOrders.Count,
            BestStrategy = strategyStats.Count > 0 ? strategyStats.OrderByDescending(pair => pair.Value.NetPnl).First().Key : null,
            WorstStrategy = strategyStats.Count > 0 ? strategyStats.OrderBy(pair => pair.Value.NetPnl).First().Key : null,
            BestSymbol = symbolStats.Count > 0 ? symbolStats.OrderByDescending(pair => pair.Value).First().Key : null,
            WorstSymbol = symbolStats.Count > 0 ? symbolStats.OrderBy(pair => pair.Value).First().Key : null,
            TotalNetPnl = totalNetPnl,
            TotalFees = totalFees,
            AverageWinRate = ReportMetrics.CalculateWinRate(tradeAnalysis.Winning, tradeAnalysis.Losing, tradeAnalysis.BreakEven),
            AverageProfitFactor = profitFactors.Count > 0 ? profitFactors.Average() : 0m,
            MaxDrawdownPercent = 0m,
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<Dictionary<string, (decimal NetPnl, decimal GrossProfit, decimal GrossLoss)>> BuildStrategyRankingsAsync(
        IReadOnlyList<Trade> trades,
        CancellationToken cancellationToken)
    {
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var lookup = strategies.ToDictionary(strategy => strategy.Id, strategy => strategy.Code.ToCode());
        var stats = new Dictionary<string, (decimal NetPnl, decimal GrossProfit, decimal GrossLoss)>();

        foreach (var group in trades.Where(trade => trade.StrategyId.HasValue).GroupBy(trade => trade.StrategyId!.Value))
        {
            if (!lookup.TryGetValue(group.Key, out var code))
            {
                continue;
            }

            var analysis = ReportMetrics.AnalyzeTrades(group);
            stats[code] = (group.Sum(trade => trade.NetPnl), analysis.GrossProfit, analysis.GrossLoss);
        }

        return stats;
    }

    private async Task<Dictionary<string, decimal>> BuildSymbolRankingsAsync(
        IReadOnlyList<Trade> trades,
        CancellationToken cancellationToken)
    {
        var stats = new Dictionary<string, decimal>();

        foreach (var group in trades.GroupBy(trade => trade.SymbolId))
        {
            var symbol = await _symbolRepository.GetByIdAsync(group.Key, cancellationToken);
            if (symbol is null)
            {
                continue;
            }

            stats[symbol.SymbolName] = group.Sum(trade => trade.NetPnl);
        }

        return stats;
    }
}
