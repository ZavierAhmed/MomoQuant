using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Backtesting;

public sealed class BacktestContext
{
    public required long BacktestRunId { get; init; }
    public TradingMode SimulationMode { get; init; } = TradingMode.Backtest;
    public required long TradingSessionId { get; init; }
    public required long ExchangeId { get; init; }
    public required long RiskProfileId { get; init; }
    public required RunBacktestSettings Settings { get; init; }
    public required IReadOnlyList<RiskRule> RiskRules { get; init; }
    public required IReadOnlyList<Strategy> Strategies { get; init; }
    public required IReadOnlyDictionary<long, Domain.Exchanges.Symbol> Symbols { get; init; }
    public long? BenchmarkRunId { get; init; }
    public long? BenchmarkRunItemId { get; init; }
    public string? BenchmarkStrategyCode { get; init; }
    public string? BenchmarkSymbol { get; init; }
    public string? BenchmarkTimeframe { get; init; }

    public bool EmergencyStopEnabled { get; set; }

    public decimal Balance { get; set; }
    public decimal PeakEquity { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public int ConsecutiveLosses { get; set; }
    public DateTime? CurrentDayUtc { get; set; }
    public DateTime? CurrentWeekUtc { get; set; }
    public decimal DailyPnl { get; set; }
    public decimal WeeklyPnl { get; set; }

    public int TotalSignals { get; set; }
    public int ApprovedSignals { get; set; }
    public int RejectedSignals { get; set; }
    public int StrategiesEvaluated { get; set; }
    public int NoTradeSignals { get; set; }
    public int EntrySignals { get; set; }
    public int WarningSignals { get; set; }
    public int InvalidSignals { get; set; }
    public int RiskEvaluations { get; set; }
    public int RiskApproved { get; set; }
    public int RiskRejected { get; set; }
    public int ConfidenceEvaluations { get; set; }
    public int ConfidenceApproved { get; set; }
    public int ConfidenceRejected { get; set; }
    public int RejectedByBoth { get; set; }
    public int MissedOrders { get; set; }
    public int FilledOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal TotalFees { get; set; }
    public decimal TotalSlippage { get; set; }

    public List<SimulatedPosition> OpenPositions { get; } = [];
    public List<PendingMarketFill> PendingMarketFills { get; } = [];
    public List<PendingMakerOrder> PendingMakerOrders { get; } = [];

    public List<StrategySignal> Signals { get; } = [];
    public Dictionary<StrategySignal, AiDecision> SignalAiDecisions { get; } = new();
    public Dictionary<StrategySignal, RiskDecision> SignalRiskDecisions { get; } = new();
    public List<RiskDecision> RiskDecisions { get; } = [];
    public List<AiDecision> AiDecisions { get; } = [];
    public List<Order> Orders { get; } = [];
    public List<OrderFill> OrderFills { get; } = [];
    public List<Trade> Trades { get; } = [];
    public Dictionary<Trade, Order> TradeEntryOrders { get; } = new();
    public Dictionary<Trade, Order> TradeExitOrders { get; } = new();
    public List<(Order Order, OrderFill Fill)> OrderFillLinks { get; } = [];
    public List<(MissedOrder MissedOrder, StrategySignal Signal)> MissedOrderLinks { get; } = [];
    public List<BacktestEquityPoint> EquityPoints { get; } = [];

    public Dictionary<string, StrategyRuntimeStats> StrategyStats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SymbolRuntimeStats> SymbolStats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string StrategyCode, string Reason)> NoTradeReasonEvents { get; } = [];
    public List<CandidateTradeRecord> CandidateTrades { get; } = [];
    public List<ShadowTradeRecord> ShadowTrades { get; } = [];
    public BbLiquiditySweepFunnelCounts? BbLiquiditySweepFunnel { get; set; }
    public IReadOnlyList<BbLiquiditySweepSampleEvaluation> BbLiquiditySweepSampleRejections { get; set; } = [];
    public VolatilityGatedSuperTrendFunnelCounts? VgSupertrendFunnel { get; set; }

    public decimal CalculateEquity()
    {
        var unrealized = OpenPositions.Sum(position => position.UnrealizedPnl);
        return Balance + unrealized;
    }

    public void RecordEquityPoint(DateTime timestampUtc, long backtestRunId)
    {
        var equity = CalculateEquity();
        if (equity > PeakEquity)
        {
            PeakEquity = equity;
        }

        var drawdown = PeakEquity - equity;
        var drawdownPercent = PeakEquity > 0 ? drawdown / PeakEquity * 100m : 0m;
        if (drawdown > MaxDrawdown)
        {
            MaxDrawdown = drawdown;
            MaxDrawdownPercent = drawdownPercent;
        }

        EquityPoints.Add(new BacktestEquityPoint
        {
            BacktestRunId = backtestRunId,
            TimestampUtc = timestampUtc,
            Balance = Balance,
            Equity = equity,
            Drawdown = drawdown,
            DrawdownPercent = drawdownPercent,
            OpenPositionCount = OpenPositions.Count,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public void UpdatePnlTracking(DateTime candleTimeUtc, decimal realizedPnl)
    {
        var day = candleTimeUtc.Date;
        var week = day.AddDays(-(int)day.DayOfWeek);

        if (CurrentDayUtc != day)
        {
            CurrentDayUtc = day;
            DailyPnl = 0m;
        }

        if (CurrentWeekUtc != week)
        {
            CurrentWeekUtc = week;
            WeeklyPnl = 0m;
        }

        DailyPnl += realizedPnl;
        WeeklyPnl += realizedPnl;
    }
}

public sealed class RunBacktestSettings
{
    public required string Name { get; init; }
    public required IReadOnlyList<long> SymbolIds { get; init; }
    public required IReadOnlyList<Timeframe> Timeframes { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required decimal InitialBalance { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public required decimal MakerFeeRate { get; init; }
    public required decimal TakerFeeRate { get; init; }
    public required int OrderExpiryCandles { get; init; }
    public required bool UseAiScoring { get; init; }
    public bool StrictAiRequired { get; init; }
    public required decimal MinConfidenceScore { get; init; }
    public required decimal SlippagePercent { get; init; }
    public BenchmarkEvaluationMode EvaluationMode { get; init; } = BenchmarkEvaluationMode.FullValidation;
    public bool EnableShadowTradeAnalysis { get; init; } = true;
    public SameCandleExitPolicy SameCandleExitPolicy { get; init; } = SameCandleExitPolicy.ConservativeStopFirst;
    public ShadowEntryModel ShadowEntryModel { get; init; } = ShadowEntryModel.MarketNextOpen;
}

public sealed class StrategyRuntimeStats
{
    public required StrategyCode StrategyCode { get; init; }
    public int TotalSignals { get; set; }
    public int ApprovedSignals { get; set; }
    public int RejectedSignals { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal NetPnl { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal PeakEquity { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal ConfidenceTotal { get; set; }
    public int ConfidenceCount { get; set; }
}

public sealed class SymbolRuntimeStats
{
    public required long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required Timeframe Timeframe { get; init; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal NetPnl { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal TotalFees { get; set; }
    public int MissedOrders { get; set; }
    public decimal PeakEquity { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
}

public enum CandidateTradeFinalDecision
{
    Executed = 1,
    RejectedByConfidence = 2,
    RejectedByRisk = 3,
    RejectedByBoth = 4,
    RejectedInvalidTrade = 5,
    MissedExecution = 6,
    SkippedByMode = 7
}

public enum ShadowRejectedBy
{
    Confidence = 1,
    Risk = 2,
    Both = 3,
    InvalidTrade = 4
}

public enum ShadowOutcomeClassification
{
    WouldHaveWon = 1,
    WouldHaveLost = 2,
    BreakEven = 3,
    NotEnoughFutureData = 4,
    NotTriggered = 5
}

public enum SameCandleExitPolicy
{
    ConservativeStopFirst = 1,
    TargetFirst = 2,
    OpenHighLowCloseHeuristic = 3
}

/// <summary>Versioned shadow / raw entry model for Milestone 23.0.</summary>
public enum ShadowEntryModel
{
    MarketNextOpen = 1,
    MarketNextClose = 2,
    LimitAtPrice = 3,
    StopAtPrice = 4,
    MakerLimit = 5,
    NotApplicable = 6
}

public enum ShadowEntryStatus
{
    Triggered = 1,
    NotTriggered = 2,
    NotEnoughFutureData = 3,
    NotApplicable = 4
}

public sealed class CandidateTradeRecord
{
    public required string SourceMode { get; init; }
    public long? SourceRunId { get; init; }
    public long? BenchmarkRunId { get; init; }
    public long? BenchmarkRunItemId { get; init; }
    public long BacktestRunId { get; init; }
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public long? CandleId { get; init; }
    public DateTime SignalTimeUtc { get; init; }
    public string Direction { get; init; } = string.Empty;
    public string SignalType { get; init; } = "Entry";
    public decimal EntryPrice { get; init; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal Quantity { get; set; }
    public decimal Leverage { get; set; }
    public decimal MarginUsed { get; set; }
    public decimal NotionalValue { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal RiskPercent { get; set; }
    public decimal RewardRiskRatio { get; set; }
    public decimal StrategyConfidence { get; init; }
    public decimal? AiConfidence { get; init; }
    public decimal CombinedConfidence { get; init; }
    public string MarketRegime { get; init; } = string.Empty;
    public string EvaluationMode { get; init; } = string.Empty;
    public bool ConfidenceGateEnabled { get; init; }
    public bool ConfidenceApproved { get; set; }
    public string? ConfidenceRejectionReason { get; set; }
    public bool RiskEvaluated { get; set; }
    public bool RiskApproved { get; set; }
    public string? RiskRejectionReason { get; set; }
    public CandidateTradeFinalDecision FinalDecision { get; set; }
    public string FinalDecisionReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class ShadowTradeRecord
{
    public int CandidateTradeIndex { get; init; }
    public required string SourceMode { get; init; }
    public long? SourceRunId { get; init; }
    public long? BenchmarkRunId { get; init; }
    public long? BenchmarkRunItemId { get; init; }
    public long BacktestRunId { get; init; }
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public string Direction { get; init; } = string.Empty;
    public DateTime SignalTimeUtc { get; init; }
    public decimal HypotheticalEntryPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal Quantity { get; init; }
    public decimal Leverage { get; init; }
    public decimal MarginUsed { get; init; }
    public decimal NotionalValue { get; init; }
    public decimal RiskAmount { get; init; }
    public decimal RewardRiskRatio { get; init; }
    public ShadowRejectedBy RejectedBy { get; init; }
    public required string RejectionReason { get; init; }
    public bool WouldEntryTrigger { get; set; }
    public ShadowEntryModel EntryModel { get; set; } = ShadowEntryModel.MarketNextOpen;
    public decimal ProposedEntryPrice { get; set; }
    public DateTime? TriggeredAtUtc { get; set; }
    public decimal? TriggerPrice { get; set; }
    public DateTime? TriggerCandleOpenTimeUtc { get; set; }
    public string? TriggerEvidence { get; set; }
    public ShadowEntryStatus EntryStatus { get; set; } = ShadowEntryStatus.NotApplicable;
    public string? EntryExclusionReason { get; set; }
    public string IntrabarPolicyVersion { get; set; } = "ConservativeStopFirst/v1";
    public DateTime? ShadowExitTimeUtc { get; set; }
    public decimal? ShadowExitPrice { get; set; }
    public string ShadowExitReason { get; set; } = "Unknown";
    public decimal ShadowGrossPnl { get; set; }
    public decimal ShadowFees { get; set; }
    public decimal ShadowNetPnl { get; set; }
    public decimal ShadowNetPnlPercent { get; set; }
    public decimal MaxFavorableExcursion { get; set; }
    public decimal MaxAdverseExcursion { get; set; }
    public decimal MaxFavorableExcursionPercent { get; set; }
    public decimal MaxAdverseExcursionPercent { get; set; }
    public int DurationCandles { get; set; }
    public int DurationMinutes { get; set; }
    public ShadowOutcomeClassification OutcomeClassification { get; set; } = ShadowOutcomeClassification.NotEnoughFutureData;
    public DateTime CreatedAtUtc { get; init; }
}
