using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Signals;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Backtesting;

public interface IBacktestReportService
{
    Task<ServiceResult<PagedResult<BacktestRunDto>>> GetRunsAsync(PagedRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<BacktestRunDto>> GetRunByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<BacktestResultDto>> GetResultsAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BacktestTradeDto>>> GetTradesAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BacktestOrderDto>>> GetOrdersAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BacktestMissedOrderDto>>> GetMissedOrdersAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BacktestEquityPointDto>>> GetEquityCurveAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BacktestStrategyBreakdownDto>>> GetStrategyBreakdownAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BacktestSymbolBreakdownDto>>> GetSymbolBreakdownAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class BacktestReportService : IBacktestReportService
{
    private readonly IBacktestRunRepository _runRepository;
    private readonly IBacktestResultRepository _resultRepository;
    private readonly IBacktestEquityPointRepository _equityPointRepository;
    private readonly IBacktestStrategyResultRepository _strategyResultRepository;
    private readonly IBacktestSymbolResultRepository _symbolResultRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;

    public BacktestReportService(
        IBacktestRunRepository runRepository,
        IBacktestResultRepository resultRepository,
        IBacktestEquityPointRepository equityPointRepository,
        IBacktestStrategyResultRepository strategyResultRepository,
        IBacktestSymbolResultRepository symbolResultRepository,
        ITradeRepository tradeRepository,
        IOrderRepository orderRepository,
        IMissedOrderRepository missedOrderRepository)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _equityPointRepository = equityPointRepository;
        _strategyResultRepository = strategyResultRepository;
        _symbolResultRepository = symbolResultRepository;
        _tradeRepository = tradeRepository;
        _orderRepository = orderRepository;
        _missedOrderRepository = missedOrderRepository;
    }

    public async Task<ServiceResult<PagedResult<BacktestRunDto>>> GetRunsAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _runRepository.GetPagedAsync(request, cancellationToken);
        return ServiceResult<PagedResult<BacktestRunDto>>.Ok(new PagedResult<BacktestRunDto>
        {
            Items = items.Select(MapRun).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Clamp(request.PageSize, 1, 100),
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<BacktestRunDto>> GetRunByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        return run is null
            ? ServiceResult<BacktestRunDto>.Fail("Backtest run was not found.")
            : ServiceResult<BacktestRunDto>.Ok(MapRun(run));
    }

    public async Task<ServiceResult<BacktestResultDto>> GetResultsAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<BacktestResultDto>.Fail("Backtest run was not found.");
        }

        var result = await _resultRepository.GetByRunIdAsync(id, cancellationToken);
        return result is null
            ? ServiceResult<BacktestResultDto>.Fail("Backtest results were not found.")
            : ServiceResult<BacktestResultDto>.Ok(MapResult(result));
    }

    public async Task<ServiceResult<IReadOnlyList<BacktestTradeDto>>> GetTradesAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(id, cancellationToken);
        if (!run.Succeeded)
        {
            return ServiceResult<IReadOnlyList<BacktestTradeDto>>.Fail(run.ErrorMessage!);
        }

        var trades = await _tradeRepository.GetByTradingSessionIdAsync(run.Data!.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<BacktestTradeDto>>.Ok(trades.Select(MapTrade).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<BacktestOrderDto>>> GetOrdersAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(id, cancellationToken);
        if (!run.Succeeded)
        {
            return ServiceResult<IReadOnlyList<BacktestOrderDto>>.Fail(run.ErrorMessage!);
        }

        var orders = await _orderRepository.GetByTradingSessionIdAsync(run.Data!.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<BacktestOrderDto>>.Ok(orders.Select(MapOrder).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<BacktestMissedOrderDto>>> GetMissedOrdersAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(id, cancellationToken);
        if (!run.Succeeded)
        {
            return ServiceResult<IReadOnlyList<BacktestMissedOrderDto>>.Fail(run.ErrorMessage!);
        }

        var missedOrders = await _missedOrderRepository.GetByTradingSessionIdAsync(run.Data!.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<BacktestMissedOrderDto>>.Ok(missedOrders.Select(MapMissedOrder).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<BacktestEquityPointDto>>> GetEquityCurveAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(id, cancellationToken);
        if (!run.Succeeded)
        {
            return ServiceResult<IReadOnlyList<BacktestEquityPointDto>>.Fail(run.ErrorMessage!);
        }

        var points = await _equityPointRepository.GetByRunIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<BacktestEquityPointDto>>.Ok(points.Select(MapEquityPoint).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<BacktestStrategyBreakdownDto>>> GetStrategyBreakdownAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(id, cancellationToken);
        if (!run.Succeeded)
        {
            return ServiceResult<IReadOnlyList<BacktestStrategyBreakdownDto>>.Fail(run.ErrorMessage!);
        }

        var results = await _strategyResultRepository.GetByRunIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<BacktestStrategyBreakdownDto>>.Ok(results.Select(MapStrategyBreakdown).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<BacktestSymbolBreakdownDto>>> GetSymbolBreakdownAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(id, cancellationToken);
        if (!run.Succeeded)
        {
            return ServiceResult<IReadOnlyList<BacktestSymbolBreakdownDto>>.Fail(run.ErrorMessage!);
        }

        var results = await _symbolResultRepository.GetByRunIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<BacktestSymbolBreakdownDto>>.Ok(results.Select(MapSymbolBreakdown).ToList());
    }

    private async Task<ServiceResult<BacktestRun>> RequireRunAsync(long id, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        return run is null
            ? ServiceResult<BacktestRun>.Fail("Backtest run was not found.")
            : ServiceResult<BacktestRun>.Ok(run);
    }

    private static BacktestRunDto MapRun(BacktestRun run) => new()
    {
        Id = run.Id,
        Name = run.Name,
        Status = run.Status.ToString(),
        ExchangeId = run.ExchangeId,
        SymbolId = run.SymbolId,
        Timeframe = TimeframeParser.ToApiString(run.Timeframe),
        FromUtc = run.StartDateUtc,
        ToUtc = run.EndDateUtc,
        InitialBalance = run.InitialBalance,
        FinalBalance = run.FinalBalance,
        RiskProfileId = run.RiskProfileId,
        ExecutionMode = run.ExecutionMode.ToString(),
        UseAiScoring = run.UseAiScoring,
        ErrorMessage = run.ErrorMessage,
        StartedAtUtc = run.StartedAtUtc,
        CompletedAtUtc = run.CompletedAtUtc,
        CreatedAtUtc = run.CreatedAtUtc
    };

    private static BacktestResultDto MapResult(BacktestResult result) => new()
    {
        BacktestRunId = result.BacktestRunId,
        InitialBalance = result.InitialBalance,
        FinalBalance = result.FinalBalance,
        NetPnl = result.NetPnl,
        NetPnlPercent = result.NetPnlPercent,
        GrossProfit = result.GrossProfit,
        GrossLoss = result.GrossLoss,
        ProfitFactor = result.ProfitFactor,
        MaxDrawdown = result.MaxDrawdown,
        MaxDrawdownPercent = result.MaxDrawdownPercent,
        TotalTrades = result.TotalTrades,
        WinningTrades = result.WinningTrades,
        LosingTrades = result.LosingTrades,
        BreakEvenTrades = result.BreakEvenTrades,
        WinRatePercent = result.WinRatePercent,
        AverageWin = result.AverageWin,
        AverageLoss = result.AverageLoss,
        LargestWin = result.LargestWin,
        LargestLoss = result.LargestLoss,
        AverageRewardRisk = result.AverageRewardRisk,
        TotalFees = result.TotalFees,
        TotalSlippage = result.TotalSlippage,
        TotalSignals = result.TotalSignals,
        ApprovedSignals = result.ApprovedSignals,
        RejectedSignals = result.RejectedSignals,
        MissedOrders = result.MissedOrders,
        FilledOrders = result.FilledOrders,
        CancelledOrders = result.CancelledOrders
    };

    private static BacktestTradeDto MapTrade(Domain.Trades.Trade trade) => new()
    {
        Id = trade.Id,
        SymbolId = trade.SymbolId,
        StrategyId = trade.StrategyId,
        Direction = trade.Direction.ToString(),
        EntryPrice = trade.EntryPrice,
        ExitPrice = trade.ExitPrice,
        Quantity = trade.Quantity,
        StopLoss = trade.StopLoss,
        TakeProfit = trade.TakeProfit,
        Status = trade.Status.ToString(),
        NetPnl = trade.NetPnl,
        Fees = trade.Fees,
        CloseReason = trade.CloseReason?.ToString(),
        OpenedAtUtc = trade.OpenedAtUtc,
        ClosedAtUtc = trade.ClosedAtUtc
    };

    private static BacktestOrderDto MapOrder(Order order) => new()
    {
        Id = order.Id,
        SymbolId = order.SymbolId,
        Mode = order.Mode.ToString(),
        Side = order.Side.ToString(),
        OrderType = order.OrderType.ToString(),
        Price = order.Price,
        Quantity = order.Quantity,
        Status = order.Status.ToString(),
        IsPostOnly = order.IsPostOnly,
        RequestedAtUtc = order.RequestedAtUtc,
        FilledAtUtc = order.FilledAtUtc
    };

    private static BacktestMissedOrderDto MapMissedOrder(MissedOrder order) => new()
    {
        Id = order.Id,
        SymbolId = order.SymbolId,
        SignalId = order.SignalId,
        RequestedPrice = order.RequestedPrice,
        Reason = order.Reason.ToString(),
        ExpiredAtUtc = order.ExpiredAtUtc,
        CreatedAtUtc = order.CreatedAtUtc
    };

    private static BacktestEquityPointDto MapEquityPoint(BacktestEquityPoint point) => new()
    {
        BacktestRunId = point.BacktestRunId,
        TimestampUtc = point.TimestampUtc,
        Balance = point.Balance,
        Equity = point.Equity,
        Drawdown = point.Drawdown,
        DrawdownPercent = point.DrawdownPercent,
        OpenPositionCount = point.OpenPositionCount
    };

    private static BacktestStrategyBreakdownDto MapStrategyBreakdown(BacktestStrategyResult result) => new()
    {
        StrategyCode = result.StrategyCode.ToCode(),
        TotalSignals = result.TotalSignals,
        ApprovedSignals = result.ApprovedSignals,
        RejectedSignals = result.RejectedSignals,
        TotalTrades = result.TotalTrades,
        WinningTrades = result.WinningTrades,
        LosingTrades = result.LosingTrades,
        NetPnl = result.NetPnl,
        WinRatePercent = result.WinRatePercent,
        ProfitFactor = result.ProfitFactor,
        MaxDrawdownPercent = result.MaxDrawdownPercent,
        AverageConfidenceScore = result.AverageConfidenceScore
    };

    private static BacktestSymbolBreakdownDto MapSymbolBreakdown(BacktestSymbolResult result) => new()
    {
        Symbol = result.Symbol,
        Timeframe = TimeframeParser.ToApiString(result.Timeframe),
        TotalTrades = result.TotalTrades,
        WinningTrades = result.WinningTrades,
        LosingTrades = result.LosingTrades,
        NetPnl = result.NetPnl,
        WinRatePercent = result.WinRatePercent,
        ProfitFactor = result.ProfitFactor,
        MaxDrawdownPercent = result.MaxDrawdownPercent,
        TotalFees = result.TotalFees,
        MissedOrders = result.MissedOrders
    };
}
