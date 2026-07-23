using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Reports;

public interface IStrategyPerformanceReportService
{
    Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetForPaperSessionAsync(long paperSessionId, CancellationToken cancellationToken = default);
}

public sealed class StrategyPerformanceReportService : IStrategyPerformanceReportService
{
    private readonly IReportQueryValidator _queryValidator;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly IPaperTradingSessionRepository _paperSessionRepository;
    private readonly IStrategyRepository _strategyRepository;

    public StrategyPerformanceReportService(
        IReportQueryValidator queryValidator,
        IReportDataRepository reportDataRepository,
        IPaperTradingSessionRepository paperSessionRepository,
        IStrategyRepository strategyRepository)
    {
        _queryValidator = queryValidator;
        _reportDataRepository = reportDataRepository;
        _paperSessionRepository = paperSessionRepository;
        _strategyRepository = strategyRepository;
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        return ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>.Ok(
            await BuildAsync(validation.Data, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetForPaperSessionAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _paperSessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>.Fail("Paper session was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = session.FromUtc ?? DateTime.MinValue,
            ToUtc = session.ToUtc ?? DateTime.UtcNow,
            TradingSessionId = session.TradingSessionId,
            Mode = TradingMode.Paper,
            Limit = 500
        };

        return ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>.Ok(await BuildAsync(filter, cancellationToken));
    }

    private async Task<IReadOnlyList<StrategyPerformanceReportDto>> BuildAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var trades = await _reportDataRepository.GetTradesAsync(filter, cancellationToken);
        var signals = await _reportDataRepository.GetSignalsAsync(filter, cancellationToken);
        var riskDecisions = await _reportDataRepository.GetRiskDecisionsAsync(filter, cancellationToken);
        var aiDecisions = await _reportDataRepository.GetAiDecisionsAsync(filter, cancellationToken);
        var missedOrders = await _reportDataRepository.GetMissedOrdersAsync(filter, cancellationToken);
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var mode = filter.Mode?.ToString() ?? "All";

        return strategies.Select(strategy =>
        {
            var strategyTrades = trades.Where(trade => trade.StrategyId == strategy.Id).ToList();
            var strategySignals = signals.Where(signal => signal.StrategyId == strategy.Id).ToList();
            var analysis = ReportMetrics.AnalyzeTrades(strategyTrades);
            var approved = riskDecisions.Count(decision => decision.Decision == RiskDecisionType.Approved);
            var rejected = riskDecisions.Count(decision => decision.Decision == RiskDecisionType.Rejected);
            var avgConfidence = aiDecisions.Count > 0 ? aiDecisions.Average(decision => decision.ConfidenceScore) : 0m;

            return new StrategyPerformanceReportDto
            {
                StrategyId = strategy.Id,
                StrategyCode = strategy.Code.ToCode(),
                StrategyName = strategy.Name,
                Mode = mode,
                TotalSignals = strategySignals.Count,
                EntrySignals = strategySignals.Count(signal => signal.SignalType == SignalType.Entry),
                NoTradeSignals = strategySignals.Count(signal => signal.SignalType == SignalType.NoTrade),
                ApprovedSignals = approved,
                RejectedSignals = rejected,
                TotalTrades = analysis.Winning + analysis.Losing + analysis.BreakEven,
                WinningTrades = analysis.Winning,
                LosingTrades = analysis.Losing,
                WinRatePercent = ReportMetrics.CalculateWinRate(analysis.Winning, analysis.Losing, analysis.BreakEven),
                NetPnl = strategyTrades.Sum(trade => trade.NetPnl),
                GrossProfit = analysis.GrossProfit,
                GrossLoss = analysis.GrossLoss,
                ProfitFactor = ReportMetrics.CalculateProfitFactor(analysis.GrossProfit, analysis.GrossLoss),
                AveragePnl = strategyTrades.Count > 0 ? strategyTrades.Sum(trade => trade.NetPnl) / strategyTrades.Count : 0m,
                AverageConfidenceScore = avgConfidence,
                AverageRewardRisk = ReportMetrics.CalculateAverageRewardRisk(strategyTrades),
                MaxDrawdownPercent = 0m,
                TotalFees = strategyTrades.Sum(trade => trade.Fees),
                MissedOrders = missedOrders.Count
            };
        }).Where(dto => dto.TotalSignals > 0 || dto.TotalTrades > 0).ToList();
    }
}

public interface ISymbolPerformanceReportService
{
    Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetForPaperSessionAsync(long paperSessionId, CancellationToken cancellationToken = default);
}

public sealed class SymbolPerformanceReportService : ISymbolPerformanceReportService
{
    private readonly IReportQueryValidator _queryValidator;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly IPaperTradingSessionRepository _paperSessionRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyRepository _strategyRepository;

    public SymbolPerformanceReportService(
        IReportQueryValidator queryValidator,
        IReportDataRepository reportDataRepository,
        IPaperTradingSessionRepository paperSessionRepository,
        ISymbolRepository symbolRepository,
        IStrategyRepository strategyRepository)
    {
        _queryValidator = queryValidator;
        _reportDataRepository = reportDataRepository;
        _paperSessionRepository = paperSessionRepository;
        _symbolRepository = symbolRepository;
        _strategyRepository = strategyRepository;
    }

    public async Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        return ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>.Ok(await BuildAsync(validation.Data, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetForPaperSessionAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _paperSessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>.Fail("Paper session was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = session.FromUtc ?? DateTime.MinValue,
            ToUtc = session.ToUtc ?? DateTime.UtcNow,
            TradingSessionId = session.TradingSessionId,
            Mode = TradingMode.Paper,
            Limit = 500
        };

        return ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>.Ok(await BuildAsync(filter, cancellationToken));
    }

    private async Task<IReadOnlyList<SymbolPerformanceReportDto>> BuildAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var trades = await _reportDataRepository.GetTradesAsync(filter, cancellationToken);
        var signals = await _reportDataRepository.GetSignalsAsync(filter, cancellationToken);
        var missedOrders = await _reportDataRepository.GetMissedOrdersAsync(filter, cancellationToken);
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var mode = filter.Mode?.ToString() ?? "All";
        var results = new List<SymbolPerformanceReportDto>();

        foreach (var group in signals.GroupBy(signal => new { signal.SymbolId, signal.Timeframe }))
        {
            var symbol = await _symbolRepository.GetByIdAsync(group.Key.SymbolId, cancellationToken);
            if (symbol is null)
            {
                continue;
            }

            var symbolTrades = trades.Where(trade => trade.SymbolId == group.Key.SymbolId).ToList();
            var analysis = ReportMetrics.AnalyzeTrades(symbolTrades);
            var strategyPnls = symbolTrades
                .Where(trade => trade.StrategyId.HasValue)
                .GroupBy(trade => trade.StrategyId!.Value)
                .Select(g => new
                {
                    Code = strategies.FirstOrDefault(s => s.Id == g.Key)?.Code.ToCode() ?? g.Key.ToString(),
                    NetPnl = g.Sum(trade => trade.NetPnl)
                })
                .ToList();

            results.Add(new SymbolPerformanceReportDto
            {
                SymbolId = group.Key.SymbolId,
                Symbol = symbol.SymbolName,
                Timeframe = TimeframeParser.ToApiString(group.Key.Timeframe),
                Mode = mode,
                TotalSignals = group.Count(),
                TotalTrades = analysis.Winning + analysis.Losing + analysis.BreakEven,
                WinningTrades = analysis.Winning,
                LosingTrades = analysis.Losing,
                WinRatePercent = ReportMetrics.CalculateWinRate(analysis.Winning, analysis.Losing, analysis.BreakEven),
                NetPnl = symbolTrades.Sum(trade => trade.NetPnl),
                GrossProfit = analysis.GrossProfit,
                GrossLoss = analysis.GrossLoss,
                ProfitFactor = ReportMetrics.CalculateProfitFactor(analysis.GrossProfit, analysis.GrossLoss),
                AveragePnl = symbolTrades.Count > 0 ? symbolTrades.Sum(trade => trade.NetPnl) / symbolTrades.Count : 0m,
                MaxDrawdownPercent = 0m,
                TotalFees = symbolTrades.Sum(trade => trade.Fees),
                MissedOrders = missedOrders.Count(order => order.SymbolId == group.Key.SymbolId),
                BestStrategy = strategyPnls.OrderByDescending(item => item.NetPnl).FirstOrDefault()?.Code,
                WorstStrategy = strategyPnls.OrderBy(item => item.NetPnl).FirstOrDefault()?.Code
            });
        }

        return results;
    }
}
