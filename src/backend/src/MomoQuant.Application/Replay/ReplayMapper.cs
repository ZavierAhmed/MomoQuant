using System.Text.Json;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Replay;

public static class ReplayMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ReplaySessionDto MapSession(ReplaySession session, string symbolName) => new()
    {
        Id = session.Id,
        Name = session.Name,
        Status = session.Status.ToString(),
        ExchangeId = session.ExchangeId,
        SymbolId = session.SymbolId,
        Symbol = symbolName,
        Timeframe = TimeframeParser.ToApiString(session.Timeframe),
        FromUtc = session.FromUtc,
        ToUtc = session.ToUtc,
        InitialBalance = session.InitialBalance,
        CurrentBalance = session.CurrentBalance,
        CurrentEquity = session.CurrentEquity,
        RiskProfileId = session.RiskProfileId,
        ExecutionMode = session.ExecutionMode.ToString(),
        UseAiScoring = session.UseAiScoring,
        Speed = MapSpeed(session.Speed),
        CurrentFrameIndex = session.CurrentFrameIndex,
        CurrentCandleId = session.CurrentCandleId,
        TotalFrames = session.TotalFrames,
        StartedAtUtc = session.StartedAtUtc,
        PausedAtUtc = session.PausedAtUtc,
        CompletedAtUtc = session.CompletedAtUtc,
        ErrorMessage = session.ErrorMessage,
        CreatedAtUtc = session.CreatedAtUtc,
        UpdatedAtUtc = session.UpdatedAtUtc
    };

    public static ReplayFrameDto MapFrame(
        ReplaySession session,
        string symbolName,
        ReplayStepResult step,
        int frameIndex) => new()
    {
        ReplaySessionId = session.Id,
        FrameIndex = frameIndex,
        TimestampUtc = step.Candle.CloseTimeUtc,
        Symbol = symbolName,
        Timeframe = TimeframeParser.ToApiString(session.Timeframe),
        Candle = MapCandle(step.Candle),
        IndicatorSnapshot = step.IndicatorSnapshot is null ? null : MapIndicatorSnapshot(step.IndicatorSnapshot),
        MarketRegime = step.MarketRegime.ToString(),
        StrategyResults = step.StrategyResults.Select(MapStrategyResult).ToList(),
        AiDecision = step.AiDecision is null ? null : MapAiDecision(step.AiDecision),
        RiskDecision = step.RiskDecision is null ? null : MapRiskDecision(step.RiskDecision),
        SimulatedOrder = step.SimulatedOrder is null ? null : MapOrder(step.SimulatedOrder),
        SimulatedFill = step.SimulatedFill is null ? null : MapFill(step.SimulatedFill),
        OpenPosition = step.OpenPosition is null ? null : MapPosition(step.OpenPosition),
        ClosedTrade = step.ClosedTrade is null ? null : MapTrade(step.ClosedTrade),
        MissedOrder = step.MissedOrder is null ? null : MapMissedOrder(step.MissedOrder),
        Balance = step.Balance,
        Equity = step.Equity,
        Drawdown = step.Drawdown,
        DrawdownPercent = step.DrawdownPercent,
        HumanReadableExplanation = step.Explanation
    };

    public static ReplayFrameDto MapFrameEntity(
        ReplaySession session,
        string symbolName,
        ReplayFrame frame,
        Candle candle,
        IReadOnlyList<StrategyEvaluationResult> strategyResults,
        IndicatorSnapshot? indicatorSnapshot = null) => new()
    {
        ReplaySessionId = session.Id,
        FrameIndex = frame.FrameIndex,
        TimestampUtc = frame.TimestampUtc,
        Symbol = symbolName,
        Timeframe = TimeframeParser.ToApiString(session.Timeframe),
        Candle = MapCandle(candle),
        IndicatorSnapshot = indicatorSnapshot is null ? null : MapIndicatorSnapshot(indicatorSnapshot),
        MarketRegime = frame.MarketRegime.ToString(),
        StrategyResults = DeserializeStrategyResults(frame.StrategyResultsJson).Select(MapStrategyResult).ToList(),
        Balance = frame.Balance,
        Equity = frame.Equity,
        Drawdown = frame.Drawdown,
        DrawdownPercent = frame.DrawdownPercent,
        HumanReadableExplanation = frame.Explanation
    };

    public static IReadOnlyList<StrategyEvaluationResult> DeserializeStrategyResults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var dtos = JsonSerializer.Deserialize<List<ReplayStrategyResultDto>>(json, JsonOptions);
            if (dtos is null || dtos.Count == 0)
            {
                return [];
            }

            return dtos.Select(dto => new StrategyEvaluationResult
            {
                StrategyCode = dto.StrategyCode,
                StrategyName = dto.StrategyName,
                Evaluated = dto.Evaluated,
                Skipped = dto.Skipped,
                SkipReason = dto.SkipReason,
                SignalType = Enum.TryParse<SignalType>(dto.SignalType, ignoreCase: true, out var signalType)
                    ? signalType
                    : SignalType.NoTrade,
                Direction = Enum.TryParse<TradeDirection>(dto.Direction, ignoreCase: true, out var direction)
                    ? direction
                    : TradeDirection.None,
                Strength = dto.Strength,
                ConfidenceContribution = dto.ConfidenceContribution,
                EntryPrice = dto.EntryPrice,
                SuggestedStopLoss = dto.SuggestedStopLoss,
                SuggestedTakeProfit = dto.SuggestedTakeProfit,
                Reason = dto.Reason,
                Regime = dto.Regime,
                Timeframe = dto.Timeframe,
                IsValid = dto.IsValid
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<ReplayStrategyResultDto> DeserializeStrategyResultDtos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ReplayStrategyResultDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static ReplayFrame ToEntity(long sessionId, int frameIndex, ReplayStepResult step, IReadOnlyList<StrategyEvaluationResult> strategyResults) => new()
    {
        ReplaySessionId = sessionId,
        FrameIndex = frameIndex,
        CandleId = step.Candle.Id,
        TimestampUtc = step.Candle.CloseTimeUtc,
        MarketRegime = step.MarketRegime,
        StrategyResultsJson = JsonSerializer.Serialize(strategyResults.Select(MapStrategyResult), JsonOptions),
        AiDecisionId = step.AiDecision?.Id,
        RiskDecisionId = step.RiskDecision?.Id,
        OrderId = step.SimulatedOrder?.Id,
        TradeId = step.ClosedTrade?.Id,
        MissedOrderId = step.MissedOrder?.Id,
        Balance = step.Balance,
        Equity = step.Equity,
        Drawdown = step.Drawdown,
        DrawdownPercent = step.DrawdownPercent,
        Explanation = step.Explanation,
        CreatedAtUtc = DateTime.UtcNow
    };

    public static void ApplyFrameData(ReplayFrame target, ReplayFrame source)
    {
        target.CandleId = source.CandleId;
        target.TimestampUtc = source.TimestampUtc;
        target.MarketRegime = source.MarketRegime;
        target.StrategyResultsJson = source.StrategyResultsJson;
        target.AiDecisionId = source.AiDecisionId;
        target.RiskDecisionId = source.RiskDecisionId;
        target.OrderId = source.OrderId;
        target.TradeId = source.TradeId;
        target.MissedOrderId = source.MissedOrderId;
        target.Balance = source.Balance;
        target.Equity = source.Equity;
        target.Drawdown = source.Drawdown;
        target.DrawdownPercent = source.DrawdownPercent;
        target.Explanation = source.Explanation;
    }

    public static string MapSpeed(ReplaySpeed speed) => speed switch
    {
        ReplaySpeed.ManualStep => "ManualStep",
        ReplaySpeed.Speed1x => "1x",
        ReplaySpeed.Speed2x => "2x",
        ReplaySpeed.Speed5x => "5x",
        ReplaySpeed.Speed10x => "10x",
        _ => speed.ToString()
    };

    public static bool TryParseSpeed(string? value, out ReplaySpeed speed)
    {
        speed = ReplaySpeed.ManualStep;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "manualstep":
                speed = ReplaySpeed.ManualStep;
                return true;
            case "1x":
                speed = ReplaySpeed.Speed1x;
                return true;
            case "2x":
                speed = ReplaySpeed.Speed2x;
                return true;
            case "5x":
                speed = ReplaySpeed.Speed5x;
                return true;
            case "10x":
                speed = ReplaySpeed.Speed10x;
                return true;
            default:
                return Enum.TryParse(value, ignoreCase: true, out speed);
        }
    }

    private static ReplayCandleDto MapCandle(Candle candle) => new()
    {
        Id = candle.Id,
        OpenTimeUtc = candle.OpenTimeUtc,
        CloseTimeUtc = candle.CloseTimeUtc,
        Open = candle.Open,
        High = candle.High,
        Low = candle.Low,
        Close = candle.Close,
        Volume = candle.Volume
    };

    public static ReplayIndicatorSnapshotDto MapIndicatorSnapshot(IndicatorSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        CandleId = snapshot.CandleId,
        Ema20 = snapshot.Ema20,
        Ema50 = snapshot.Ema50,
        Ema200 = snapshot.Ema200,
        Vwap = snapshot.Vwap,
        Rsi14 = snapshot.Rsi14,
        Atr14 = snapshot.Atr14,
        VolumeSma20 = snapshot.VolumeSma20,
        SwingHigh = snapshot.SwingHigh,
        SwingLow = snapshot.SwingLow,
        MarketStructure = snapshot.MarketStructure.ToString()
    };

    private static ReplayStrategyResultDto MapStrategyResult(StrategyEvaluationResult result) => new()
    {
        StrategyCode = result.StrategyCode,
        StrategyName = result.StrategyName,
        Evaluated = result.Evaluated,
        Skipped = result.Skipped,
        SkipReason = result.SkipReason,
        SignalType = result.SignalType.ToString(),
        Direction = result.Direction.ToString(),
        Strength = result.Strength,
        ConfidenceContribution = result.ConfidenceContribution,
        EntryPrice = result.EntryPrice,
        SuggestedStopLoss = result.SuggestedStopLoss,
        SuggestedTakeProfit = result.SuggestedTakeProfit,
        StopLoss = result.StopLoss,
        TakeProfit = result.TakeProfit,
        Reason = result.Reason,
        Regime = result.Regime,
        Timeframe = result.Timeframe,
        IsValid = result.IsValid
    };

    private static ReplayAiDecisionDto MapAiDecision(AiDecision decision) => new()
    {
        Id = decision.Id,
        MarketRegime = decision.MarketRegime.ToString(),
        ConfidenceScore = decision.ConfidenceScore,
        Classification = decision.ConfidenceClassification ?? string.Empty,
        TradeAllowed = decision.TradeAllowed,
        Summary = decision.Summary ?? string.Empty,
        Explanation = decision.Explanation ?? string.Empty
    };

    private static ReplayRiskDecisionDto MapRiskDecision(RiskDecision decision) => new()
    {
        Id = decision.Id,
        Decision = decision.Decision.ToString(),
        Reason = decision.Reason,
        RejectedRuleKey = decision.RejectedRuleKey,
        PositionSize = decision.PositionSize,
        StopLoss = decision.StopLoss,
        TakeProfit = decision.TakeProfit
    };

    private static ReplayOrderDto MapOrder(Order order) => new()
    {
        Id = order.Id,
        Mode = order.Mode.ToString(),
        Side = order.Side.ToString(),
        OrderType = order.OrderType.ToString(),
        Status = order.Status.ToString(),
        Price = order.Price,
        Quantity = order.Quantity,
        IsPostOnly = order.IsPostOnly
    };

    private static ReplayFillDto MapFill(OrderFill fill) => new()
    {
        Id = fill.Id,
        FillPrice = fill.FillPrice,
        FillQuantity = fill.FillQuantity,
        Fee = fill.Fee,
        LiquidityType = fill.LiquidityType.ToString(),
        FilledAtUtc = fill.FilledAtUtc
    };

    private static ReplayPositionDto MapPosition(SimulatedPositionSnapshot position) => new()
    {
        Direction = position.Direction.ToString(),
        EntryPrice = position.EntryPrice,
        Quantity = position.Quantity,
        StopLoss = position.StopLoss,
        TakeProfit = position.TakeProfit,
        UnrealizedPnl = position.UnrealizedPnl,
        StrategyCode = position.StrategyCode.ToCode()
    };

    private static ReplayTradeDto MapTrade(Trade trade) => new()
    {
        Id = trade.Id,
        Direction = trade.Direction.ToString(),
        EntryPrice = trade.EntryPrice,
        ExitPrice = trade.ExitPrice,
        Quantity = trade.Quantity,
        Status = trade.Status.ToString(),
        CloseReason = trade.CloseReason?.ToString(),
        NetPnl = trade.NetPnl,
        Fees = trade.Fees
    };

    private static ReplayMissedOrderDto MapMissedOrder(MissedOrder missedOrder) => new()
    {
        Id = missedOrder.Id,
        RequestedPrice = missedOrder.RequestedPrice,
        Reason = missedOrder.Reason.ToString(),
        ExpiredAtUtc = missedOrder.ExpiredAtUtc
    };

    public static ReplaySignalDto MapSignal(StrategySignal signal, string strategyCode) => new()
    {
        Id = signal.Id,
        StrategyCode = strategyCode,
        SignalType = signal.SignalType.ToString(),
        Direction = signal.Direction.ToString(),
        Strength = signal.Strength,
        Reason = signal.Reason,
        CreatedAtUtc = signal.CreatedAtUtc
    };
}
