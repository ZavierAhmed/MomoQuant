using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Backtesting.Simulation;

public interface ISimulatedExecutionProvider
{
    void SubmitMarketFill(BacktestContext context, PendingMarketFill pending);

    void SubmitMakerOrder(BacktestContext context, PendingMakerOrder pending);

    void ProcessPendingMarketFills(BacktestContext context, IReadOnlyList<Candle> candles, int candleIndex);

    void ProcessPendingMakerOrders(BacktestContext context, Candle candle, int candleIndex);

    void FinalizePendingOrders(BacktestContext context, Candle lastCandle, int lastCandleIndex);

    void UpdateOpenPositions(BacktestContext context, Candle candle);
}

public sealed class SimulatedExecutionProvider : ISimulatedExecutionProvider
{
    public void SubmitMarketFill(BacktestContext context, PendingMarketFill pending) =>
        context.PendingMarketFills.Add(pending);

    public void SubmitMakerOrder(BacktestContext context, PendingMakerOrder pending) =>
        context.PendingMakerOrders.Add(pending);

    public void ProcessPendingMarketFills(BacktestContext context, IReadOnlyList<Candle> candles, int candleIndex)
    {
        var candle = candles[candleIndex];
        var dueFills = context.PendingMarketFills
            .Where(fill => fill.FillAtCandleIndex == candleIndex)
            .ToList();

        foreach (var pending in dueFills)
        {
            context.PendingMarketFills.Remove(pending);
            var fillPrice = ApplySlippage(candle.Open, pending.Direction, pending.SlippagePercent);
            OpenPosition(context, pending.SymbolId, pending.StrategyId, pending.StrategyCode, pending.Timeframe,
                pending.Direction, fillPrice, pending.Quantity, pending.StopLoss, pending.TakeProfit,
                pending.TakerFeeRate, LiquidityType.SimulatedTaker, pending.Signal, pending.AiDecisionId,
                pending.RiskDecisionId, candle.OpenTimeUtc, OrderType.Market, isPostOnly: false,
                breakevenTriggerPrice: pending.BreakevenTriggerPrice);
        }
    }

    public void ProcessPendingMakerOrders(BacktestContext context, Candle candle, int candleIndex)
    {
        var activeOrders = context.PendingMakerOrders.ToList();
        foreach (var pending in activeOrders)
        {
            if (pending.SymbolId != candle.SymbolId)
            {
                continue;
            }

            if (candleIndex <= pending.PlacedAtCandleIndex)
            {
                continue;
            }

            if (PriceTouchesLimit(candle, pending.Direction, pending.LimitPrice))
            {
                context.PendingMakerOrders.Remove(pending);
                pending.Order.Status = OrderStatus.Filled;
                pending.Order.FilledAtUtc = candle.OpenTimeUtc;
                OpenPosition(context, pending.SymbolId, pending.StrategyId, pending.StrategyCode, pending.Timeframe,
                    pending.Direction, pending.LimitPrice, pending.Quantity, pending.StopLoss, pending.TakeProfit,
                    pending.MakerFeeRate, LiquidityType.SimulatedMaker, pending.Signal, pending.AiDecisionId,
                    pending.RiskDecisionId, candle.OpenTimeUtc, OrderType.Limit, isPostOnly: true, pending.Order,
                    breakevenTriggerPrice: pending.BreakevenTriggerPrice);
                continue;
            }

            if (candleIndex >= pending.ExpiryCandleIndex)
            {
                context.PendingMakerOrders.Remove(pending);
                pending.Order.Status = pending.ExecutionMode == ExecutionMode.MakerThenCancel
                    ? OrderStatus.Cancelled
                    : OrderStatus.Expired;
                pending.Order.CancelledAtUtc = candle.CloseTimeUtc;

                if (pending.ExecutionMode == ExecutionMode.MakerThenCancel)
                {
                    context.CancelledOrders++;
                }

                context.MissedOrders++;
                var missed = new MissedOrder
                {
                    TradingSessionId = context.TradingSessionId,
                    SignalId = 0,
                    SymbolId = pending.SymbolId,
                    RequestedPrice = pending.LimitPrice,
                    BestBid = candle.Low,
                    BestAsk = candle.High,
                    Reason = MissedOrderReason.Timeout,
                    ExpiredAtUtc = candle.CloseTimeUtc,
                    CreatedAtUtc = DateTime.UtcNow
                };
                context.MissedOrderLinks.Add((missed, pending.Signal));

                var symbolStats = GetSymbolStats(context, pending.SymbolId, pending.Timeframe);
                symbolStats.MissedOrders++;
            }
        }
    }

    public void FinalizePendingOrders(BacktestContext context, Candle lastCandle, int lastCandleIndex)
    {
        foreach (var pending in context.PendingMarketFills.ToList())
        {
            context.PendingMarketFills.Remove(pending);
            context.MissedOrders++;
        }

        foreach (var pending in context.PendingMakerOrders.ToList())
        {
            context.PendingMakerOrders.Remove(pending);
            pending.Order.Status = OrderStatus.Expired;
            pending.Order.CancelledAtUtc = lastCandle.CloseTimeUtc;
            context.MissedOrders++;
            var missed = new MissedOrder
            {
                TradingSessionId = context.TradingSessionId,
                SignalId = 0,
                SymbolId = pending.SymbolId,
                RequestedPrice = pending.LimitPrice,
                BestBid = lastCandle.Low,
                BestAsk = lastCandle.High,
                Reason = MissedOrderReason.Timeout,
                ExpiredAtUtc = lastCandle.CloseTimeUtc,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.MissedOrderLinks.Add((missed, pending.Signal));
        }
    }

    public void UpdateOpenPositions(BacktestContext context, Candle candle)
    {
        var policy = ConservativeStopFirstIntrabarPolicy.Instance;
        foreach (var position in context.OpenPositions.Where(position => position.SymbolId == candle.SymbolId).ToList())
        {
            position.UnrealizedPnl = CalculateUnrealizedPnl(position.Direction, position.EntryPrice, candle.Close, position.Quantity);

            // Original stop must be evaluated before breakeven activation on the same OHLC candle.
            var originalStop = position.StopLoss;
            var beDecision = policy.EvaluateBreakevenActivation(
                position.Direction,
                originalStop,
                position.BreakevenActivated ? null : position.BreakevenTriggerPrice,
                candle);

            if (beDecision.ChosenEvent == IntrabarChosenEvent.OriginalStop && beDecision.ExitPrice is decimal stopExit)
            {
                ClosePosition(context, position, stopExit, CloseReason.StopLoss, candle.CloseTimeUtc);
                continue;
            }

            var exitDecision = policy.EvaluateProtectiveExits(
                position.Direction,
                originalStop,
                position.TakeProfit,
                candle,
                context.Settings.SameCandleExitPolicy);

            if (exitDecision.ExitPrice is decimal exitPrice
                && exitDecision.ChosenEvent is IntrabarChosenEvent.OriginalStop
                    or IntrabarChosenEvent.GapBeyondStop
                    or IntrabarChosenEvent.Target
                    or IntrabarChosenEvent.GapBeyondTarget)
            {
                var reason = exitDecision.ChosenEvent is IntrabarChosenEvent.Target or IntrabarChosenEvent.GapBeyondTarget
                    ? CloseReason.TakeProfit
                    : CloseReason.StopLoss;
                ClosePosition(context, position, exitPrice, reason, candle.CloseTimeUtc);
                continue;
            }

            if (beDecision.ActivateBreakeven && !position.BreakevenActivated)
            {
                position.BreakevenActivated = true;
                position.StopLoss = position.EntryPrice;
                position.Trade.StopLoss = position.EntryPrice;
            }
        }
    }

    private static void OpenPosition(
        BacktestContext context,
        long symbolId,
        long strategyId,
        StrategyCode strategyCode,
        Timeframe timeframe,
        TradeDirection direction,
        decimal fillPrice,
        decimal quantity,
        decimal stopLoss,
        decimal takeProfit,
        decimal feeRate,
        LiquidityType liquidityType,
        StrategySignal signal,
        long? aiDecisionId,
        long? riskDecisionId,
        DateTime fillTimeUtc,
        OrderType orderType,
        bool isPostOnly,
        Order? existingOrder = null,
        decimal? breakevenTriggerPrice = null)
    {
        if (context.OpenPositions.Any(position => position.SymbolId == symbolId))
        {
            return;
        }

        var fee = fillPrice * quantity * feeRate;
        var order = existingOrder ?? CreateOrder(context, symbolId, direction, orderType, fillPrice, quantity, isPostOnly, fillTimeUtc);
        if (existingOrder is null)
        {
            order.Status = OrderStatus.Filled;
            order.FilledAtUtc = fillTimeUtc;
            context.Orders.Add(order);
        }

        var fill = new OrderFill
        {
            FillPrice = fillPrice,
            FillQuantity = quantity,
            Fee = fee,
            FeeAsset = "USDT",
            LiquidityType = liquidityType,
            FilledAtUtc = fillTimeUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.OrderFillLinks.Add((order, fill));
        context.OrderFills.Add(fill);
        context.FilledOrders++;
        context.TotalFees += fee;
        context.Balance -= fee;

        var trade = new Trade
        {
            TradingSessionId = context.TradingSessionId,
            SymbolId = symbolId,
            StrategyId = strategyId,
            SignalId = null,
            AiDecisionId = aiDecisionId,
            RiskDecisionId = riskDecisionId,
            Direction = direction,
            EntryPrice = fillPrice,
            Quantity = quantity,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            Status = TradeStatus.Open,
            OpenedAtUtc = fillTimeUtc,
            Fees = fee,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.Trades.Add(trade);
        context.TradeEntryOrders[trade] = order;

        context.OpenPositions.Add(new SimulatedPosition
        {
            SymbolId = symbolId,
            StrategyId = strategyId,
            StrategyCode = strategyCode,
            Timeframe = timeframe,
            Direction = direction,
            EntryPrice = fillPrice,
            Quantity = quantity,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            EntryFees = fee,
            OpenedAtUtc = fillTimeUtc,
            Signal = signal,
            AiDecisionId = aiDecisionId,
            RiskDecisionId = riskDecisionId,
            EntryOrder = order,
            EntryFill = fill,
            Trade = trade,
            BreakevenTriggerPrice = breakevenTriggerPrice
        });

        var strategyStats = GetStrategyStats(context, strategyCode);
        strategyStats.TotalTrades++;

        var symbolStats = GetSymbolStats(context, symbolId, timeframe);
        symbolStats.TotalTrades++;
        symbolStats.TotalFees += fee;
    }

    private static void ClosePosition(
        BacktestContext context,
        SimulatedPosition position,
        decimal exitPrice,
        CloseReason closeReason,
        DateTime closedAtUtc)
    {
        var exitFeeRate = context.Settings.ExecutionMode == ExecutionMode.MarketFill
            ? context.Settings.TakerFeeRate
            : context.Settings.MakerFeeRate;
        var exitFee = exitPrice * position.Quantity * exitFeeRate;
        var grossPnl = CalculateUnrealizedPnl(position.Direction, position.EntryPrice, exitPrice, position.Quantity);
        var settlement = SimulatedTradeSettlement.Create(
            balanceBeforeExitCredit: context.Balance,
            grossPricePnl: grossPnl,
            entryFee: position.EntryFees,
            exitFee: exitFee,
            realizedAtUtc: closedAtUtc);

        var exitOrder = CreateOrder(context, position.SymbolId, ReverseDirection(position.Direction), OrderType.Market,
            exitPrice, position.Quantity, isPostOnly: false, closedAtUtc);
        exitOrder.Status = OrderStatus.Filled;
        exitOrder.FilledAtUtc = closedAtUtc;
        context.Orders.Add(exitOrder);
        context.TradeExitOrders[position.Trade] = exitOrder;

        context.OrderFills.Add(new OrderFill
        {
            FillPrice = exitPrice,
            FillQuantity = position.Quantity,
            Fee = exitFee,
            FeeAsset = "USDT",
            LiquidityType = LiquidityType.SimulatedTaker,
            FilledAtUtc = closedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        });
        context.OrderFillLinks.Add((exitOrder, context.OrderFills[^1]));

        position.Trade.ExitPrice = exitPrice;
        position.Trade.Status = TradeStatus.Closed;
        position.Trade.ClosedAtUtc = closedAtUtc;
        position.Trade.GrossPnl = settlement.GrossPricePnl;
        position.Trade.Fees += exitFee;
        position.Trade.NetPnl = settlement.FullyNetPnl;
        position.Trade.CloseReason = closeReason;
        position.Trade.UpdatedAtUtc = DateTime.UtcNow;

        // Entry fee already deducted at open; credit gross − exit costs only.
        context.Balance = settlement.BalanceAfter;
        context.TotalFees += exitFee;
        context.UpdatePnlTracking(closedAtUtc, settlement.FullyNetPnl);

        if (settlement.FullyNetPnl > 0)
        {
            context.ConsecutiveLosses = 0;
        }
        else if (settlement.FullyNetPnl < 0)
        {
            context.ConsecutiveLosses++;
        }

        UpdateTradeStats(context, position, settlement.FullyNetPnl, exitFee);
        context.OpenPositions.Remove(position);
    }

    private static void UpdateTradeStats(
        BacktestContext context,
        SimulatedPosition position,
        decimal fullyNetPnl,
        decimal exitFee)
    {
        var strategyStats = GetStrategyStats(context, position.StrategyCode);
        strategyStats.NetPnl += fullyNetPnl;
        if (fullyNetPnl > 0)
        {
            strategyStats.WinningTrades++;
            strategyStats.GrossProfit += fullyNetPnl;
        }
        else if (fullyNetPnl < 0)
        {
            strategyStats.LosingTrades++;
            strategyStats.GrossLoss += Math.Abs(fullyNetPnl);
        }

        var symbolStats = GetSymbolStats(context, position.SymbolId, position.Timeframe);
        symbolStats.NetPnl += fullyNetPnl;
        symbolStats.TotalFees += exitFee;
        if (fullyNetPnl > 0)
        {
            symbolStats.WinningTrades++;
            symbolStats.GrossProfit += fullyNetPnl;
        }
        else if (fullyNetPnl < 0)
        {
            symbolStats.LosingTrades++;
            symbolStats.GrossLoss += Math.Abs(fullyNetPnl);
        }
    }

    private static bool PriceTouchesLimit(Candle candle, TradeDirection direction, decimal limitPrice) =>
        ConservativeStopFirstIntrabarPolicy.Instance.LimitEntryTouched(direction, limitPrice, candle);

    private static decimal ApplySlippage(decimal price, TradeDirection direction, decimal slippagePercent)
    {
        if (slippagePercent <= 0)
        {
            return price;
        }

        var adjustment = price * slippagePercent / 100m;
        return direction == TradeDirection.Long ? price + adjustment : price - adjustment;
    }

    private static decimal CalculateUnrealizedPnl(TradeDirection direction, decimal entryPrice, decimal markPrice, decimal quantity) =>
        direction == TradeDirection.Long
            ? (markPrice - entryPrice) * quantity
            : (entryPrice - markPrice) * quantity;

    private static Order CreateOrder(
        BacktestContext context,
        long symbolId,
        TradeDirection direction,
        OrderType orderType,
        decimal price,
        decimal quantity,
        bool isPostOnly,
        DateTime requestedAtUtc) => new()
    {
        TradingSessionId = context.TradingSessionId,
        SymbolId = symbolId,
        Mode = context.SimulationMode,
        Side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
        OrderType = orderType,
        PositionSide = direction == TradeDirection.Long ? PositionSide.Long : PositionSide.Short,
        Price = price,
        Quantity = quantity,
        Status = OrderStatus.Pending,
        IsPostOnly = isPostOnly,
        TimeInForce = TimeInForce.Gtc,
        RequestedAtUtc = requestedAtUtc,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static TradeDirection ReverseDirection(TradeDirection direction) =>
        direction == TradeDirection.Long ? TradeDirection.Short : TradeDirection.Long;

    private static StrategyRuntimeStats GetStrategyStats(BacktestContext context, StrategyCode strategyCode)
    {
        var key = strategyCode.ToCode();
        if (!context.StrategyStats.TryGetValue(key, out var stats))
        {
            stats = new StrategyRuntimeStats { StrategyCode = strategyCode };
            context.StrategyStats[key] = stats;
        }

        return stats;
    }

    private static SymbolRuntimeStats GetSymbolStats(BacktestContext context, long symbolId, Timeframe timeframe)
    {
        var key = $"{symbolId}:{timeframe}";
        if (!context.SymbolStats.TryGetValue(key, out var stats))
        {
            var symbolName = context.Symbols.TryGetValue(symbolId, out var symbol) ? symbol.SymbolName : symbolId.ToString();
            stats = new SymbolRuntimeStats
            {
                SymbolId = symbolId,
                Symbol = symbolName,
                Timeframe = timeframe
            };
            context.SymbolStats[key] = stats;
        }

        return stats;
    }
}
