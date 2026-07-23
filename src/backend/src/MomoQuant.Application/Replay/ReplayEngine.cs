using System.Text;
using System.Text.Json;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Replay;

public interface IReplayEngine
{
    Task<ReplayStepResult> ProcessFrameAsync(ReplayRuntimeState state, CancellationToken cancellationToken = default);

    Task<ReplayRuntimeState> RebuildToFrameAsync(
        ReplayRuntimeState template,
        int targetFrameIndex,
        CancellationToken cancellationToken = default);
}

public sealed class ReplaySessionSettings
{
    public required decimal MakerFeeRate { get; init; }
    public required decimal TakerFeeRate { get; init; }
    public required int OrderExpiryCandles { get; init; }
    public required bool UseAiScoring { get; init; }
    public bool StrictAiRequired { get; init; }
    public required decimal MinConfidenceScore { get; init; }
    public required decimal SlippagePercent { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
}

public sealed class ReplayEngine : IReplayEngine
{
    private const int RecentCandleCount = 600;

    private readonly IStrategyEngine _strategyEngine;
    private readonly IStrategyParameterProvider _parameterProvider;
    private readonly IRiskEngine _riskEngine;
    private readonly IAiIntegrationService _aiIntegrationService;
    private readonly ISimulatedExecutionProvider _executionProvider;

    public ReplayEngine(
        IStrategyEngine strategyEngine,
        IStrategyParameterProvider parameterProvider,
        IRiskEngine riskEngine,
        IAiIntegrationService aiIntegrationService,
        ISimulatedExecutionProvider executionProvider)
    {
        _strategyEngine = strategyEngine;
        _parameterProvider = parameterProvider;
        _riskEngine = riskEngine;
        _aiIntegrationService = aiIntegrationService;
        _executionProvider = executionProvider;
    }

    public async Task<ReplayStepResult> ProcessFrameAsync(ReplayRuntimeState state, CancellationToken cancellationToken = default)
    {
        var frameIndex = state.CurrentFrameIndex;
        if (frameIndex < 0 || frameIndex >= state.Dataset.EvaluationIndices.Count)
        {
            throw new InvalidOperationException("Current frame index is out of range.");
        }

        var candleIndex = state.Dataset.EvaluationIndices[frameIndex];
        var candle = state.Dataset.Candles[candleIndex];
        var context = state.Context;
        var dataset = state.Dataset;

        var signalCountBefore = context.Signals.Count;
        var aiCountBefore = context.AiDecisions.Count;
        var riskCountBefore = context.RiskDecisions.Count;
        var orderCountBefore = context.Orders.Count;
        var fillCountBefore = context.OrderFills.Count;
        var tradeCountBefore = context.Trades.Count;
        var missedCountBefore = context.MissedOrderLinks.Count;
        var closedCountBefore = context.Trades.Count(trade => trade.Status == TradeStatus.Closed);

        _executionProvider.ProcessPendingMarketFills(context, dataset.Candles, candleIndex);
        _executionProvider.ProcessPendingMakerOrders(context, candle, candleIndex);
        _executionProvider.UpdateOpenPositions(context, candle);

        var strategyResults = new List<StrategyEvaluationResult>();
        IndicatorSnapshot? snapshot = null;
        MarketRegime regime = MarketRegime.Unknown;
        var explanation = new StringBuilder();
        explanation.AppendLine($"Frame {frameIndex} at {candle.CloseTimeUtc:O}.");

        if (!dataset.IndicatorSnapshots.TryGetValue(candle.Id, out snapshot))
        {
            explanation.AppendLine("No indicator snapshot available for this candle.");
        }
        else
        {
            explanation.AppendLine($"Indicators loaded for candle {candle.Id}.");
        }

        if (context.OpenPositions.Any(position => position.SymbolId == dataset.SymbolId))
        {
            explanation.AppendLine("Open position exists; skipping new entry evaluation.");
            return BuildResult(context, candle, snapshot, regime, strategyResults, explanation,
                signalCountBefore, aiCountBefore, riskCountBefore, orderCountBefore, fillCountBefore,
                tradeCountBefore, missedCountBefore, closedCountBefore);
        }

        regime = snapshot is null
            ? MarketRegime.Unknown
            : await DetectRegimeAsync(context, dataset, candle, snapshot, cancellationToken);
        explanation.AppendLine($"Market regime: {regime}.");

        var recentCandles = GetRecentCandles(dataset.Candles, candleIndex);
        var recentSnapshots = GetRecentSnapshots(dataset, dataset.Candles, candleIndex);

        foreach (var prepared in state.Strategies)
        {
            var parameters = await _parameterProvider.GetParametersAsync(
                prepared.Strategy.Id,
                dataset.Timeframe,
                dataset.SymbolId,
                cancellationToken);

            var strategyContext = new StrategyContext
            {
                TradingSessionId = context.TradingSessionId,
                ExchangeId = context.ExchangeId,
                SymbolId = dataset.SymbolId,
                Symbol = dataset.SymbolName,
                Timeframe = dataset.Timeframe,
                HigherTimeframe = ResolveHigherTimeframe(dataset.Timeframe),
                MarketRegime = regime,
                Candles = recentCandles,
                IndicatorSnapshot = snapshot,
                RecentIndicatorSnapshots = recentSnapshots,
                StrategyParameters = parameters,
                EvaluatedAtUtc = candle.CloseTimeUtc
            };

            var evaluations = await _strategyEngine.EvaluateAsync([prepared.Plugin], strategyContext, cancellationToken);
            context.StrategiesEvaluated += evaluations.Count;
            strategyResults.AddRange(evaluations);

            foreach (var evaluation in evaluations)
            {
                explanation.AppendLine($"{evaluation.StrategyCode}: {evaluation.SignalType} {evaluation.Direction} - {evaluation.Reason}");
                TrackEvaluation(context, evaluation);
            }

            foreach (var evaluation in evaluations.Where(TradingPipelineConfidence.ShouldEvaluateRisk))
            {
                context.EntrySignals++;
                await ProcessEntrySignalAsync(context, dataset, prepared, evaluation, candle, candleIndex, snapshot, regime, explanation, cancellationToken);
            }
        }

        return BuildResult(context, candle, snapshot, regime, strategyResults, explanation,
            signalCountBefore, aiCountBefore, riskCountBefore, orderCountBefore, fillCountBefore,
            tradeCountBefore, missedCountBefore, closedCountBefore);
    }

    public async Task<ReplayRuntimeState> RebuildToFrameAsync(
        ReplayRuntimeState template,
        int targetFrameIndex,
        CancellationToken cancellationToken = default)
    {
        var state = CreateRuntimeState(
            template.Settings,
            template.Session,
            template.Dataset,
            template.Strategies,
            template.RiskRules,
            template.Symbol);

        for (var index = 0; index <= targetFrameIndex; index++)
        {
            state.CurrentFrameIndex = index;
            await ProcessFrameAsync(state, cancellationToken);
        }

        return state;
    }

    public static ReplayRuntimeState CreateRuntimeState(
        ReplaySessionSettings settings,
        ReplaySession session,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyList<RiskRule> riskRules,
        Symbol symbol)
    {
        var runSettings = new RunBacktestSettings
        {
            Name = session.Name,
            SymbolIds = [session.SymbolId],
            Timeframes = [session.Timeframe],
            FromUtc = session.FromUtc,
            ToUtc = session.ToUtc,
            InitialBalance = session.InitialBalance,
            StrategyIds = settings.StrategyIds,
            ExecutionMode = settings.ExecutionMode,
            MakerFeeRate = settings.MakerFeeRate,
            TakerFeeRate = settings.TakerFeeRate,
            OrderExpiryCandles = settings.OrderExpiryCandles,
            UseAiScoring = settings.UseAiScoring,
            StrictAiRequired = settings.StrictAiRequired,
            MinConfidenceScore = settings.MinConfidenceScore,
            SlippagePercent = settings.SlippagePercent
        };

        var context = new BacktestContext
        {
            BacktestRunId = session.Id,
            SimulationMode = TradingMode.Replay,
            TradingSessionId = session.TradingSessionId,
            ExchangeId = session.ExchangeId,
            RiskProfileId = session.RiskProfileId,
            Settings = runSettings,
            RiskRules = riskRules,
            Strategies = strategies.Select(item => item.Strategy).ToList(),
            Symbols = new Dictionary<long, Symbol> { [symbol.Id] = symbol },
            Balance = session.InitialBalance,
            PeakEquity = session.InitialBalance
        };

        return new ReplayRuntimeState
        {
            Session = session,
            Settings = settings,
            Symbol = symbol,
            RiskRules = riskRules,
            Context = context,
            Dataset = dataset,
            Strategies = strategies,
            CurrentFrameIndex = -1
        };
    }

    private ReplayStepResult BuildResult(
        BacktestContext context,
        Candle candle,
        IndicatorSnapshot? snapshot,
        MarketRegime regime,
        IReadOnlyList<StrategyEvaluationResult> strategyResults,
        StringBuilder explanation,
        int signalCountBefore,
        int aiCountBefore,
        int riskCountBefore,
        int orderCountBefore,
        int fillCountBefore,
        int tradeCountBefore,
        int missedCountBefore,
        int closedCountBefore)
    {
        var equity = context.CalculateEquity();
        var drawdown = context.PeakEquity - equity;
        var drawdownPercent = context.PeakEquity > 0 ? drawdown / context.PeakEquity * 100m : 0m;

        var newAi = context.AiDecisions.Skip(aiCountBefore).LastOrDefault();
        var newRisk = context.RiskDecisions.Skip(riskCountBefore).LastOrDefault();
        var newOrder = context.Orders.Skip(orderCountBefore).LastOrDefault();
        var newFill = context.OrderFills.Skip(fillCountBefore).LastOrDefault();
        var newMissed = context.MissedOrderLinks.Skip(missedCountBefore).LastOrDefault().MissedOrder;
        var closedTrade = context.Trades
            .Where(trade => trade.Status == TradeStatus.Closed)
            .Skip(closedCountBefore)
            .LastOrDefault();

        if (context.Signals.Count > signalCountBefore)
        {
            explanation.AppendLine($"Generated {context.Signals.Count - signalCountBefore} strategy signal(s).");
        }

        if (newRisk is not null)
        {
            explanation.AppendLine($"Risk decision: {newRisk.Decision} - {newRisk.Reason}");
        }

        if (newOrder is not null)
        {
            explanation.AppendLine($"Simulated order {newOrder.OrderType} {newOrder.Side} at {newOrder.Price}.");
        }

        if (newFill is not null)
        {
            explanation.AppendLine($"Simulated fill at {newFill.FillPrice} with fee {newFill.Fee}.");
        }

        if (newMissed is not null)
        {
            explanation.AppendLine($"Missed maker order at {newMissed.RequestedPrice}: {newMissed.Reason}.");
        }

        if (closedTrade is not null)
        {
            explanation.AppendLine($"Trade closed with net PnL {closedTrade.NetPnl}.");
        }

        var openPosition = context.OpenPositions.FirstOrDefault();
        SimulatedPositionSnapshot? positionSnapshot = openPosition is null
            ? null
            : new SimulatedPositionSnapshot
            {
                Direction = openPosition.Direction,
                EntryPrice = openPosition.EntryPrice,
                Quantity = openPosition.Quantity,
                StopLoss = openPosition.StopLoss,
                TakeProfit = openPosition.TakeProfit,
                UnrealizedPnl = openPosition.UnrealizedPnl,
                StrategyCode = openPosition.StrategyCode
            };

        return new ReplayStepResult
        {
            Candle = candle,
            IndicatorSnapshot = snapshot,
            MarketRegime = regime,
            StrategyResults = strategyResults,
            AiDecision = newAi,
            RiskDecision = newRisk,
            SimulatedOrder = newOrder,
            SimulatedFill = newFill,
            ClosedTrade = closedTrade,
            MissedOrder = newMissed,
            OpenPosition = positionSnapshot,
            Balance = context.Balance,
            Equity = equity,
            Drawdown = drawdown,
            DrawdownPercent = drawdownPercent,
            Explanation = explanation.ToString().Trim()
        };
    }

    private async Task ProcessEntrySignalAsync(
        BacktestContext context,
        BacktestDataset dataset,
        PreparedStrategy prepared,
        StrategyEvaluationResult evaluation,
        Candle candle,
        int candleIndex,
        IndicatorSnapshot? snapshot,
        MarketRegime regime,
        StringBuilder explanation,
        CancellationToken cancellationToken)
    {
        context.TotalSignals++;

        var signal = new StrategySignal
        {
            TradingSessionId = context.TradingSessionId,
            StrategyId = prepared.Strategy.Id,
            SymbolId = dataset.SymbolId,
            Timeframe = dataset.Timeframe,
            CandleId = candle.Id,
            SignalType = evaluation.SignalType,
            Direction = evaluation.Direction,
            Strength = evaluation.Strength,
            ConfidenceContribution = evaluation.ConfidenceContribution,
            EntryPrice = evaluation.EntryPrice ?? candle.Close,
            SuggestedStopLoss = evaluation.SuggestedStopLoss,
            SuggestedTakeProfit = evaluation.SuggestedTakeProfit,
            Reason = evaluation.Reason,
            RawDataJson = evaluation.RawDataJson,
            CreatedAtUtc = candle.CloseTimeUtc
        };
        context.Signals.Add(signal);

        AiScoringInput? aiInput = null;
        long? aiDecisionId = null;

        if (context.Settings.UseAiScoring && snapshot is not null)
        {
            var confidenceResult = await ScoreConfidenceAsync(context, dataset, prepared, evaluation, regime, snapshot, candle, cancellationToken);
            aiInput = confidenceResult.Input;
            var aiDecision = PersistAiDecision(context, dataset, prepared, evaluation, regime, confidenceResult, candle);
            if (aiDecision is not null)
            {
                aiDecisionId = aiDecision.Id;
                context.SignalAiDecisions[signal] = aiDecision;
            }

            if (aiInput.Succeeded && aiInput.Valid)
            {
                explanation.AppendLine($"AI confidence score: {ConfidenceScoreNormalizer.Format(aiInput.Score ?? 0m)}.");
            }
            else if (!string.IsNullOrWhiteSpace(aiInput.Warning))
            {
                explanation.AppendLine(aiInput.Warning);
            }
        }

        var confidenceResolution = TradingPipelineConfidence.ResolveConfidence(evaluation, aiInput);
        var confidenceScore = confidenceResolution.CombinedConfidence;
        var rawConfidenceScore = confidenceResolution.RawCombinedScore;
        var confidenceSource = confidenceResolution.Source;
        var effectiveMinConfidence = TradingPipelineConfidence.ResolveEffectiveMinimum(
            context.Settings.MinConfidenceScore,
            context.RiskRules);

        var entryPrice = evaluation.EntryPrice ?? candle.Close;
        var riskContext = BuildRiskContext(
            context,
            dataset.SymbolId,
            dataset.SymbolName,
            evaluation,
            entryPrice,
            confidenceScore,
            rawConfidenceScore,
            effectiveMinConfidence,
            confidenceSource,
            regime,
            aiDecisionId,
            candle.CloseTimeUtc);
        context.RiskEvaluations++;
        var riskEvaluation = _riskEngine.Evaluate(riskContext);

        var riskDecision = new RiskDecision
        {
            TradingSessionId = context.TradingSessionId,
            SymbolId = dataset.SymbolId,
            AiDecisionId = aiDecisionId,
            Decision = riskEvaluation.Decision,
            Reason = riskEvaluation.Reason,
            ApprovedRiskPercent = riskEvaluation.ApprovedRiskPercent,
            PositionSize = riskEvaluation.PositionSize,
            StopLoss = riskEvaluation.StopLoss,
            TakeProfit = riskEvaluation.TakeProfit,
            RejectedRuleKey = riskEvaluation.RejectedRuleKey,
            CreatedAtUtc = candle.CloseTimeUtc
        };
        context.RiskDecisions.Add(riskDecision);
        context.SignalRiskDecisions[signal] = riskDecision;

        if (!riskEvaluation.Approved || !riskEvaluation.PositionSize.HasValue)
        {
            context.RejectedSignals++;
            context.RiskRejected++;
            explanation.AppendLine($"Risk rejected entry: {riskEvaluation.Reason}");
            return;
        }

        context.ApprovedSignals++;
        context.RiskApproved++;
        var quantity = riskEvaluation.PositionSize.Value;
        var stopLoss = riskEvaluation.StopLoss ?? evaluation.SuggestedStopLoss ?? entryPrice;
        var takeProfit = riskEvaluation.TakeProfit ?? evaluation.SuggestedTakeProfit ?? entryPrice;

        if (context.Settings.ExecutionMode == ExecutionMode.MarketFill)
        {
            _executionProvider.SubmitMarketFill(context, new PendingMarketFill
            {
                SymbolId = dataset.SymbolId,
                StrategyId = prepared.Strategy.Id,
                StrategyCode = prepared.Strategy.Code,
                Timeframe = dataset.Timeframe,
                Direction = evaluation.Direction,
                Quantity = quantity,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                TakerFeeRate = context.Settings.TakerFeeRate,
                SlippagePercent = context.Settings.SlippagePercent,
                Signal = signal,
                AiDecisionId = aiDecisionId,
                RiskDecisionId = null,
                FillAtCandleIndex = candleIndex + 1,
                RequestedAtUtc = candle.CloseTimeUtc
            });
            explanation.AppendLine("Submitted simulated market fill for next candle open.");
            return;
        }

        var makerOrderEntity = CreateMakerOrderEntity(context, dataset.SymbolId, evaluation.Direction, entryPrice, quantity, candle.CloseTimeUtc);
        context.Orders.Add(makerOrderEntity);

        _executionProvider.SubmitMakerOrder(context, new PendingMakerOrder
        {
            SymbolId = dataset.SymbolId,
            StrategyId = prepared.Strategy.Id,
            StrategyCode = prepared.Strategy.Code,
            Timeframe = dataset.Timeframe,
            Direction = evaluation.Direction,
            LimitPrice = entryPrice,
            Quantity = quantity,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            MakerFeeRate = context.Settings.MakerFeeRate,
            Signal = signal,
            AiDecisionId = aiDecisionId,
            RiskDecisionId = null,
            PlacedAtCandleIndex = candleIndex,
            ExpiryCandleIndex = candleIndex + context.Settings.OrderExpiryCandles,
            RequestedAtUtc = candle.CloseTimeUtc,
            Order = makerOrderEntity,
            ExecutionMode = context.Settings.ExecutionMode
        });
        explanation.AppendLine("Submitted simulated maker order.");
    }

    private static Order CreateMakerOrderEntity(
        BacktestContext context,
        long symbolId,
        TradeDirection direction,
        decimal price,
        decimal quantity,
        DateTime requestedAtUtc) => new()
    {
        TradingSessionId = context.TradingSessionId,
        SymbolId = symbolId,
        Mode = context.SimulationMode,
        Side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
        OrderType = OrderType.Limit,
        PositionSide = direction == TradeDirection.Long ? PositionSide.Long : PositionSide.Short,
        Price = price,
        Quantity = quantity,
        Status = OrderStatus.Open,
        IsPostOnly = true,
        TimeInForce = TimeInForce.Gtc,
        RequestedAtUtc = requestedAtUtc,
        SubmittedAtUtc = requestedAtUtc,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task<MarketRegime> DetectRegimeAsync(
        BacktestContext context,
        BacktestDataset dataset,
        Candle candle,
        IndicatorSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!context.Settings.UseAiScoring)
        {
            return DetectRegimeHeuristic(snapshot, candle);
        }

        decimal? atrPercent = snapshot.Atr14 is not null && candle.Close > 0
            ? snapshot.Atr14.Value / candle.Close * 100m
            : null;

        var request = new DetectRegimeRequestDto
        {
            Symbol = dataset.SymbolName,
            Timeframe = TimeframeParser.ToApiString(dataset.Timeframe),
            Ema20 = snapshot.Ema20,
            Ema50 = snapshot.Ema50,
            Ema200 = snapshot.Ema200,
            Close = candle.Close,
            AtrPercent = atrPercent,
            Rsi14 = snapshot.Rsi14,
            Volume = candle.Volume,
            VolumeSma20 = snapshot.VolumeSma20
        };

        var result = await _aiIntegrationService.DetectRegimeAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return MarketRegime.Unknown;
        }

        return Enum.TryParse<MarketRegime>(result.Data.Regime, ignoreCase: true, out var regime)
            ? regime
            : MarketRegime.Unknown;
    }

    private async Task<(AiScoringInput Input, ScoreConfidenceResponseDto? Response)> ScoreConfidenceAsync(
        BacktestContext context,
        BacktestDataset dataset,
        PreparedStrategy prepared,
        StrategyEvaluationResult evaluation,
        MarketRegime regime,
        IndicatorSnapshot snapshot,
        Candle candle,
        CancellationToken cancellationToken)
    {
        decimal? atrPercent = snapshot.Atr14 is not null && candle.Close > 0
            ? snapshot.Atr14.Value / candle.Close * 100m
            : null;

        var request = new ScoreConfidenceRequestDto
        {
            Symbol = dataset.SymbolName,
            Timeframe = TimeframeParser.ToApiString(dataset.Timeframe),
            StrategyCode = prepared.Strategy.Code.ToCode(),
            SignalDirection = evaluation.Direction.ToString(),
            MarketRegime = regime.ToString(),
            StrategyStrength = evaluation.Strength,
            Rsi14 = snapshot.Rsi14,
            AtrPercent = atrPercent
        };

        var result = await _aiIntegrationService.ScoreConfidenceAsync(request, cancellationToken);
        var input = AiScoringHelper.FromConfidenceResult(
            context.Settings.UseAiScoring,
            context.Settings.StrictAiRequired,
            result);

        return (input, result.Succeeded ? result.Data : null);
    }

    private static void TrackEvaluation(BacktestContext context, StrategyEvaluationResult evaluation)
    {
        switch (evaluation.SignalType)
        {
            case SignalType.NoTrade:
                context.NoTradeSignals++;
                if (!string.IsNullOrWhiteSpace(evaluation.SkipReason))
                {
                    context.NoTradeReasonEvents.Add((evaluation.StrategyCode, evaluation.SkipReason));
                }

                break;
            case SignalType.Warning:
                context.WarningSignals++;
                break;
            case SignalType.Entry when !TradingPipelineConfidence.ShouldEvaluateRisk(evaluation):
                context.InvalidSignals++;
                break;
        }
    }

    private static AiDecision? PersistAiDecision(
        BacktestContext context,
        BacktestDataset dataset,
        PreparedStrategy prepared,
        StrategyEvaluationResult evaluation,
        MarketRegime regime,
        (AiScoringInput Input, ScoreConfidenceResponseDto? Response) confidence,
        Candle candle)
    {
        if (!confidence.Input.Enabled || !confidence.Input.Succeeded || confidence.Response is null)
        {
            return null;
        }

        var resolution = TradingPipelineConfidence.ResolveConfidence(evaluation, confidence.Input);
        var effectiveMin = context.Settings.MinConfidenceScore > 0
            ? TradingPipelineConfidence.ResolveEffectiveMinimum(context.Settings.MinConfidenceScore, context.RiskRules)
            : TradingPipelineConfidence.ResolveEffectiveMinimum(0, context.RiskRules);

        var decision = new AiDecision
        {
            TradingSessionId = context.TradingSessionId,
            SymbolId = dataset.SymbolId,
            Timeframe = dataset.Timeframe,
            CandleId = candle.Id,
            MarketRegime = regime,
            ConfidenceScore = resolution.AiConfidence ?? resolution.StrategyConfidence,
            ConfidenceClassification = confidence.Response.Classification,
            PreferredStrategyCode = prepared.Strategy.Code,
            IsAnomalous = false,
            TradeAllowed = resolution.CombinedConfidence >= effectiveMin && !confidence.Response.UsedFallback,
            Summary = $"Replay AI score {ConfidenceScoreNormalizer.Format(resolution.AiConfidence ?? 0m)}",
            Explanation = confidence.Response.Reasons.FirstOrDefault() ?? "Replay AI evaluation",
            ReasonsJson = JsonSerializer.Serialize(confidence.Response.Reasons),
            WarningsJson = confidence.Response.Warnings.Count > 0 ? JsonSerializer.Serialize(confidence.Response.Warnings) : null,
            CreatedAtUtc = candle.CloseTimeUtc
        };

        context.AiDecisions.Add(decision);
        return decision;
    }

    private static RiskContext BuildRiskContext(
        BacktestContext context,
        long symbolId,
        string symbolName,
        StrategyEvaluationResult evaluation,
        decimal entryPrice,
        decimal confidenceScore,
        decimal rawConfidenceScore,
        decimal effectiveMinConfidenceScore,
        string confidenceSource,
        MarketRegime regime,
        long? aiDecisionId,
        DateTime evaluationTimeUtc)
    {
        var symbolExposure = context.OpenPositions
            .Where(position => position.SymbolId == symbolId)
            .Sum(position => position.EntryPrice * position.Quantity);

        return new RiskContext
        {
            TradingSessionId = context.TradingSessionId,
            SymbolId = symbolId,
            Symbol = symbolName,
            Direction = evaluation.Direction,
            EntryPrice = entryPrice,
            SuggestedStopLoss = evaluation.SuggestedStopLoss,
            SuggestedTakeProfit = evaluation.SuggestedTakeProfit,
            ConfidenceScore = confidenceScore,
            RawConfidenceScore = rawConfidenceScore,
            EffectiveMinConfidenceScore = effectiveMinConfidenceScore,
            ConfidenceSource = confidenceSource,
            StrategyCode = evaluation.StrategyCode,
            AiDecisionId = aiDecisionId,
            AccountBalance = context.Balance,
            DailyPnl = context.DailyPnl,
            WeeklyPnl = context.WeeklyPnl,
            OpenPositionCount = context.OpenPositions.Count,
            OpenSymbolExposure = symbolExposure,
            TotalExposure = context.OpenPositions.Sum(position => position.EntryPrice * position.Quantity),
            ConsecutiveLosses = context.ConsecutiveLosses,
            MarketRegime = regime,
            EmergencyStopEnabled = false,
            Rules = context.RiskRules,
            EvaluationTimeUtc = evaluationTimeUtc
        };
    }

    private static MarketRegime DetectRegimeHeuristic(IndicatorSnapshot snapshot, Candle candle)
    {
        if (snapshot.Ema20 is null || snapshot.Ema50 is null || snapshot.Ema200 is null)
        {
            return MarketRegime.Unknown;
        }

        if (snapshot.Ema20 > snapshot.Ema50 && snapshot.Ema50 > snapshot.Ema200)
        {
            return MarketRegime.Trending;
        }

        if (snapshot.Ema20 < snapshot.Ema50 && snapshot.Ema50 < snapshot.Ema200)
        {
            return MarketRegime.Trending;
        }

        if (snapshot.Atr14 is not null && candle.Close > 0 && snapshot.Atr14.Value / candle.Close * 100m > 2m)
        {
            return MarketRegime.HighVolatility;
        }

        return MarketRegime.Ranging;
    }

    private static IReadOnlyList<Candle> GetRecentCandles(IReadOnlyList<Candle> candles, int candleIndex)
    {
        var start = Math.Max(0, candleIndex - RecentCandleCount + 1);
        return candles.Skip(start).Take(candleIndex - start + 1).ToList();
    }

    private static IReadOnlyList<IndicatorSnapshot> GetRecentSnapshots(
        BacktestDataset dataset,
        IReadOnlyList<Candle> candles,
        int candleIndex)
    {
        var start = Math.Max(0, candleIndex - RecentCandleCount + 1);
        var snapshots = new List<IndicatorSnapshot>();
        for (var i = start; i <= candleIndex; i++)
        {
            var candle = candles[i];
            if (dataset.IndicatorSnapshots.TryGetValue(candle.Id, out var snapshot))
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private static Timeframe ResolveHigherTimeframe(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 or Timeframe.M3 or Timeframe.M5 => Timeframe.M15,
        Timeframe.M15 or Timeframe.M30 => Timeframe.H1,
        Timeframe.H1 => Timeframe.H4,
        _ => Timeframe.D1
    };
}
