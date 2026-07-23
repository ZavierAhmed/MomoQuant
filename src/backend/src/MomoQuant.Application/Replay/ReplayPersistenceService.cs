using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;

namespace MomoQuant.Application.Replay;

public interface IReplayPersistenceService
{
    Task PersistStepEntitiesAsync(
        BacktestContext context,
        int signalCountBefore,
        int aiCountBefore,
        int riskCountBefore,
        int orderCountBefore,
        int fillCountBefore,
        int tradeCountBefore,
        int missedCountBefore,
        CancellationToken cancellationToken = default);
}

public sealed class ReplayPersistenceService : IReplayPersistenceService
{
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderFillRepository _orderFillRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;

    public ReplayPersistenceService(
        IStrategySignalRepository signalRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        IOrderRepository orderRepository,
        IOrderFillRepository orderFillRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository)
    {
        _signalRepository = signalRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _orderRepository = orderRepository;
        _orderFillRepository = orderFillRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
    }

    public async Task PersistStepEntitiesAsync(
        BacktestContext context,
        int signalCountBefore,
        int aiCountBefore,
        int riskCountBefore,
        int orderCountBefore,
        int fillCountBefore,
        int tradeCountBefore,
        int missedCountBefore,
        CancellationToken cancellationToken = default)
    {
        var newSignals = context.Signals.Skip(signalCountBefore).ToList();
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

        var newAiDecisions = context.AiDecisions.Skip(aiCountBefore).ToList();
        foreach (var decision in newAiDecisions)
        {
            await _aiDecisionRepository.AddAsync(decision, cancellationToken);
        }

        if (newAiDecisions.Count > 0)
        {
            await _aiDecisionRepository.SaveChangesAsync(cancellationToken);
        }

        var newRiskDecisions = context.RiskDecisions.Skip(riskCountBefore).ToList();
        foreach (var decision in newRiskDecisions)
        {
            await _riskDecisionRepository.AddAsync(decision, cancellationToken);
        }

        if (newRiskDecisions.Count > 0)
        {
            await _riskDecisionRepository.SaveChangesAsync(cancellationToken);
        }

        var newOrders = context.Orders.Skip(orderCountBefore).ToList();
        if (newOrders.Count > 0)
        {
            await _orderRepository.AddRangeAsync(newOrders, cancellationToken);
            await _orderRepository.SaveChangesAsync(cancellationToken);
        }

        var newFillLinks = context.OrderFillLinks.Skip(fillCountBefore).ToList();
        foreach (var (order, fill) in newFillLinks)
        {
            fill.OrderId = order.Id;
        }

        var newFills = context.OrderFills.Skip(fillCountBefore).ToList();
        if (newFills.Count > 0)
        {
            await _orderFillRepository.AddRangeAsync(newFills, cancellationToken);
            await _orderFillRepository.SaveChangesAsync(cancellationToken);
        }

        var newTrades = context.Trades.Skip(tradeCountBefore).ToList();
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

        var newMissedLinks = context.MissedOrderLinks.Skip(missedCountBefore).ToList();
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
}
