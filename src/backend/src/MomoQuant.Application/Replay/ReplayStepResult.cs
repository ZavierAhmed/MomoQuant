using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Replay;

public sealed class ReplayStepResult
{
    public required Candle Candle { get; init; }
    public IndicatorSnapshot? IndicatorSnapshot { get; init; }
    public MarketRegime MarketRegime { get; init; }
    public required IReadOnlyList<StrategyEvaluationResult> StrategyResults { get; init; }
    public AiDecision? AiDecision { get; init; }
    public RiskDecision? RiskDecision { get; init; }
    public Order? SimulatedOrder { get; init; }
    public OrderFill? SimulatedFill { get; init; }
    public Trade? ClosedTrade { get; init; }
    public MissedOrder? MissedOrder { get; init; }
    public SimulatedPositionSnapshot? OpenPosition { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
    public required string Explanation { get; init; }
}

public sealed class SimulatedPositionSnapshot
{
    public required TradeDirection Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal StopLoss { get; init; }
    public required decimal TakeProfit { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required StrategyCode StrategyCode { get; init; }
}
