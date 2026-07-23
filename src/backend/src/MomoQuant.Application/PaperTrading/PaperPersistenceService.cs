using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.PaperTrading;

public interface IPaperPersistenceService
{
    Task PersistCandleAsync(
        PaperSessionState state,
        CandleProcessResult result,
        CancellationToken cancellationToken = default);

    Task SyncAccountAsync(PaperSessionState state, CancellationToken cancellationToken = default);
}

public sealed class PaperPersistenceService : IPaperPersistenceService
{
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderFillRepository _orderFillRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly IPaperAccountRepository _accountRepository;
    private readonly IPaperAccountSnapshotRepository _snapshotRepository;
    private readonly IPositionRepository _positionRepository;

    public PaperPersistenceService(
        IStrategySignalRepository signalRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        IOrderRepository orderRepository,
        IOrderFillRepository orderFillRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IPaperAccountRepository accountRepository,
        IPaperAccountSnapshotRepository snapshotRepository,
        IPositionRepository positionRepository)
    {
        _signalRepository = signalRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _orderRepository = orderRepository;
        _orderFillRepository = orderFillRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
        _accountRepository = accountRepository;
        _snapshotRepository = snapshotRepository;
        _positionRepository = positionRepository;
    }

    public async Task PersistCandleAsync(
        PaperSessionState state,
        CandleProcessResult result,
        CancellationToken cancellationToken = default)
    {
        var context = state.Context;
        await PersistEntitiesAsync(context, result, cancellationToken);
        await SyncPositionsAsync(state, result.Candle, cancellationToken);
        await CreateSnapshotAsync(state, result.Candle.CloseTimeUtc, cancellationToken);
        await SyncAccountAsync(state, cancellationToken);
    }

    public async Task SyncAccountAsync(PaperSessionState state, CancellationToken cancellationToken = default)
    {
        var context = state.Context;
        var equity = context.CalculateEquity();
        var unrealized = context.OpenPositions.Sum(position => position.UnrealizedPnl);
        var realized = context.Trades.Where(trade => trade.Status == TradeStatus.Closed).Sum(trade => trade.NetPnl);

        state.Account.CurrentBalance = context.Balance;
        state.Account.CurrentEquity = equity;
        state.Account.TotalRealizedPnl = realized;
        state.Account.TotalUnrealizedPnl = unrealized;
        state.Account.TotalFees = context.TotalFees;
        state.Account.MaxDrawdown = context.MaxDrawdown;
        state.Account.MaxDrawdownPercent = context.MaxDrawdownPercent;
        state.Account.UpdatedAtUtc = DateTime.UtcNow;

        await _accountRepository.UpdateAsync(state.Account, cancellationToken);
        await _accountRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistEntitiesAsync(
        BacktestContext context,
        CandleProcessResult result,
        CancellationToken cancellationToken)
    {
        var newSignals = context.Signals.Skip(result.SignalCountBefore).ToList();
        if (newSignals.Count > 0)
        {
            await _signalRepository.AddRangeAsync(newSignals, cancellationToken);
            await _signalRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var pair in context.SignalAiDecisions.Where(pair => pair.Key.Id == 0 && newSignals.Contains(pair.Key)))
        {
            var persisted = newSignals.First(signal => ReferenceEquals(signal, pair.Key) || signal == pair.Key);
            pair.Value.SignalId = persisted.Id;
        }

        foreach (var pair in context.SignalRiskDecisions.Where(pair => pair.Key.Id == 0 && newSignals.Contains(pair.Key)))
        {
            var persisted = newSignals.First(signal => ReferenceEquals(signal, pair.Key) || signal == pair.Key);
            pair.Value.SignalId = persisted.Id;
            if (context.SignalAiDecisions.TryGetValue(pair.Key, out var aiDecision))
            {
                pair.Value.AiDecisionId = aiDecision.Id;
            }
        }

        var newAiDecisions = context.AiDecisions.Skip(result.AiCountBefore).ToList();
        foreach (var decision in newAiDecisions)
        {
            await _aiDecisionRepository.AddAsync(decision, cancellationToken);
        }

        if (newAiDecisions.Count > 0)
        {
            await _aiDecisionRepository.SaveChangesAsync(cancellationToken);
        }

        var newRiskDecisions = context.RiskDecisions.Skip(result.RiskCountBefore).ToList();
        foreach (var decision in newRiskDecisions)
        {
            await _riskDecisionRepository.AddAsync(decision, cancellationToken);
        }

        if (newRiskDecisions.Count > 0)
        {
            await _riskDecisionRepository.SaveChangesAsync(cancellationToken);
        }

        var newOrders = context.Orders.Skip(result.OrderCountBefore).ToList();
        if (newOrders.Count > 0)
        {
            await _orderRepository.AddRangeAsync(newOrders, cancellationToken);
            await _orderRepository.SaveChangesAsync(cancellationToken);
        }

        var newFillLinks = context.OrderFillLinks.Skip(result.FillCountBefore).ToList();
        foreach (var (order, fill) in newFillLinks)
        {
            fill.OrderId = order.Id;
        }

        var newFills = context.OrderFills.Skip(result.FillCountBefore).ToList();
        if (newFills.Count > 0)
        {
            await _orderFillRepository.AddRangeAsync(newFills, cancellationToken);
            await _orderFillRepository.SaveChangesAsync(cancellationToken);
        }

        var newTrades = context.Trades.Skip(result.TradeCountBefore).ToList();
        foreach (var trade in newTrades)
        {
            if (context.TradeEntryOrders.TryGetValue(trade, out var entryOrder))
            {
                trade.EntryOrderId = entryOrder.Id;
            }

            if (context.TradeExitOrders.TryGetValue(trade, out var exitOrder))
            {
                trade.ExitOrderId = exitOrder.Id;
            }
        }

        if (newTrades.Count > 0)
        {
            await _tradeRepository.AddRangeAsync(newTrades, cancellationToken);
            await _tradeRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var trade in newTrades)
        {
            if (context.TradeEntryOrders.TryGetValue(trade, out var entryOrder))
            {
                entryOrder.TradeId = trade.Id;
            }

            if (context.TradeExitOrders.TryGetValue(trade, out var exitOrder))
            {
                exitOrder.TradeId = trade.Id;
            }
        }

        if (newTrades.Count > 0)
        {
            await _orderRepository.SaveChangesAsync(cancellationToken);
        }

        var newMissedLinks = context.MissedOrderLinks.Skip(result.MissedCountBefore).ToList();
        foreach (var (missedOrder, signal) in newMissedLinks)
        {
            missedOrder.SignalId = signal.Id;
        }

        if (newMissedLinks.Count > 0)
        {
            await _missedOrderRepository.AddRangeAsync(newMissedLinks.Select(link => link.MissedOrder).ToList(), cancellationToken);
            await _missedOrderRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SyncPositionsAsync(
        PaperSessionState state,
        Domain.MarketData.Candle candle,
        CancellationToken cancellationToken)
    {
        var context = state.Context;
        var existing = await _positionRepository.GetTrackedByTradingSessionIdAsync(context.TradingSessionId, cancellationToken);
        var openExisting = existing.Where(position => position.Status == PositionStatus.Open).ToDictionary(position => position.SymbolId);

        foreach (var simulated in context.OpenPositions)
        {
            if (openExisting.TryGetValue(simulated.SymbolId, out var position))
            {
                position.MarkPrice = candle.Close;
                position.UnrealizedPnl = simulated.UnrealizedPnl;
                position.Quantity = simulated.Quantity;
                position.UpdatedAtUtc = candle.CloseTimeUtc;
                openExisting.Remove(simulated.SymbolId);
            }
            else
            {
                await _positionRepository.AddAsync(new Position
                {
                    TradingSessionId = context.TradingSessionId,
                    SymbolId = simulated.SymbolId,
                    Direction = simulated.Direction,
                    Quantity = simulated.Quantity,
                    AverageEntryPrice = simulated.EntryPrice,
                    MarkPrice = candle.Close,
                    UnrealizedPnl = simulated.UnrealizedPnl,
                    RealizedPnl = 0m,
                    Leverage = 1m,
                    MarginUsed = simulated.EntryPrice * simulated.Quantity,
                    Status = PositionStatus.Open,
                    OpenedAtUtc = simulated.OpenedAtUtc,
                    UpdatedAtUtc = candle.CloseTimeUtc
                }, cancellationToken);
            }
        }

        foreach (var closed in openExisting.Values)
        {
            closed.Status = PositionStatus.Closed;
            closed.ClosedAtUtc = candle.CloseTimeUtc;
            closed.UpdatedAtUtc = candle.CloseTimeUtc;
        }

        await _positionRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateSnapshotAsync(
        PaperSessionState state,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        var context = state.Context;
        var equity = context.CalculateEquity();
        var drawdown = context.PeakEquity - equity;
        var drawdownPercent = context.PeakEquity > 0 ? drawdown / context.PeakEquity * 100m : 0m;
        var realized = context.Trades.Where(trade => trade.Status == TradeStatus.Closed).Sum(trade => trade.NetPnl);
        var unrealized = context.OpenPositions.Sum(position => position.UnrealizedPnl);

        await _snapshotRepository.AddAsync(new PaperAccountSnapshot
        {
            PaperAccountId = state.Account.Id,
            PaperSessionId = state.Session.Id,
            TradingSessionId = context.TradingSessionId,
            TimestampUtc = timestampUtc,
            Balance = context.Balance,
            Equity = equity,
            RealizedPnl = realized,
            UnrealizedPnl = unrealized,
            TotalFees = context.TotalFees,
            Drawdown = drawdown,
            DrawdownPercent = drawdownPercent,
            OpenPositionCount = context.OpenPositions.Count,
            MarginUsed = context.OpenPositions.Sum(position => position.EntryPrice * position.Quantity),
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);

        await _snapshotRepository.SaveChangesAsync(cancellationToken);
    }
}
