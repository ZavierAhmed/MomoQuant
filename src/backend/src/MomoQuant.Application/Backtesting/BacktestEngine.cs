using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Backtesting;

public interface IBacktestEngine
{
    Task RunDatasetAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        CancellationToken cancellationToken = default);

    Task RunDatasetWithParametersAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>> parameterOverrides,
        CancellationToken cancellationToken = default);

    Task<CandleProcessResult> ProcessCandleAtIndexAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        int evaluationIndex,
        CancellationToken cancellationToken = default);

    Task<CandleProcessResult> ProcessCandleAtIndexWithParametersAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>> cachedParameters,
        int evaluationIndex,
        CancellationToken cancellationToken = default);
}

public sealed class CandleProcessResult
{
    public required Candle Candle { get; init; }
    public int SignalCountBefore { get; init; }
    public int AiCountBefore { get; init; }
    public int RiskCountBefore { get; init; }
    public int OrderCountBefore { get; init; }
    public int FillCountBefore { get; init; }
    public int TradeCountBefore { get; init; }
    public int MissedCountBefore { get; init; }
}

public sealed class PreparedStrategy
{
    public required Strategy Strategy { get; init; }
    public required ITradingStrategy Plugin { get; init; }
}

public sealed class BacktestEngine : IBacktestEngine
{
    private const int RecentCandleCount = 600;

    private readonly IStrategyEngine _strategyEngine;
    private readonly IStrategyParameterProvider _parameterProvider;
    private readonly IRiskEngine _riskEngine;
    private readonly IAiIntegrationService _aiIntegrationService;
    private readonly ISimulatedExecutionProvider _executionProvider;
    private readonly IBacktestProgressStore _progressStore;
    private readonly IBbLiquiditySweepBacktestBootstrap? _bbLiquidityBootstrap;
    private readonly IBbLiquiditySweepSessionTracker? _bbSessionTracker;
    private readonly IBbLiquiditySweepFunnelTracker? _bbFunnelTracker;
    private readonly IVolatilityGatedSuperTrendFunnelTracker? _vgFunnelTracker;
    private readonly ILogger<BacktestEngine> _logger;

    public BacktestEngine(
        IStrategyEngine strategyEngine,
        IStrategyParameterProvider parameterProvider,
        IRiskEngine riskEngine,
        IAiIntegrationService aiIntegrationService,
        ISimulatedExecutionProvider executionProvider,
        IBacktestProgressStore? progressStore = null,
        IBbLiquiditySweepBacktestBootstrap? bbLiquidityBootstrap = null,
        IBbLiquiditySweepSessionTracker? bbSessionTracker = null,
        IBbLiquiditySweepFunnelTracker? bbFunnelTracker = null,
        IVolatilityGatedSuperTrendFunnelTracker? vgFunnelTracker = null,
        ILogger<BacktestEngine>? logger = null)
    {
        _strategyEngine = strategyEngine;
        _parameterProvider = parameterProvider;
        _riskEngine = riskEngine;
        _aiIntegrationService = aiIntegrationService;
        _executionProvider = executionProvider;
        _progressStore = progressStore ?? new BacktestProgressStore();
        _bbLiquidityBootstrap = bbLiquidityBootstrap;
        _bbSessionTracker = bbSessionTracker;
        _bbFunnelTracker = bbFunnelTracker;
        _vgFunnelTracker = vgFunnelTracker;
        _logger = logger ?? NullLogger<BacktestEngine>.Instance;
    }

    public async Task RunDatasetAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        CancellationToken cancellationToken = default)
    {
        var cachedParameters = new Dictionary<long, IReadOnlyDictionary<string, string>>();
        foreach (var prepared in strategies)
        {
            cachedParameters[prepared.Strategy.Id] = await _parameterProvider.GetParametersAsync(
                prepared.Strategy.Id,
                dataset.Timeframe,
                dataset.SymbolId,
                cancellationToken);
        }

        await RunDatasetInternalAsync(context, dataset, strategies, cachedParameters, cancellationToken);
    }

    public Task RunDatasetWithParametersAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>> parameterOverrides,
        CancellationToken cancellationToken = default) =>
        RunDatasetInternalAsync(context, dataset, strategies, parameterOverrides, cancellationToken);

    private async Task RunDatasetInternalAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>> cachedParameters,
        CancellationToken cancellationToken)
    {
        foreach (var prepared in strategies)
        {
            if (!cachedParameters.TryGetValue(prepared.Strategy.Id, out var strategyParams))
            {
                continue;
            }

            if (prepared.Plugin is FourHourRangeReEntryStrategy fourHourStrategy)
            {
                fourHourStrategy.PrecomputeRanges(dataset.SymbolId, dataset.Timeframe, dataset.Candles, strategyParams);
            }

            if (prepared.Plugin is BbLiquiditySweepCisdStrategyBase bbStrategy)
            {
                if (_bbLiquidityBootstrap is not null)
                {
                    await _bbLiquidityBootstrap.PrecomputeAsync(
                        context.TradingSessionId,
                        dataset,
                        bbStrategy,
                        strategyParams,
                        cancellationToken);
                }
                else
                {
                    bbStrategy.PrecomputeData(
                        dataset.SymbolId,
                        dataset.Candles,
                        null,
                        null,
                        strategyParams,
                        context.TradingSessionId);
                }
            }

            if (prepared.Plugin is VolatilityGatedSuperTrendMomentumStrategy vgStrategy)
            {
                vgStrategy.PrecomputeData(dataset.SymbolId, dataset.Candles, strategyParams, context.TradingSessionId);
            }
        }

        var startedAtUtc = DateTime.UtcNow;
        var lastHeartbeatLoggedAtUtc = startedAtUtc;
        for (var i = 0; i < dataset.EvaluationIndices.Count; i++)
        {
            var processResult = await ProcessCandleAtIndexAsync(
                context,
                dataset,
                strategies,
                cachedParameters,
                i,
                cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var shouldHeartbeat =
                i == 0
                || (i + 1) % 500 == 0
                || (nowUtc - lastHeartbeatLoggedAtUtc) >= TimeSpan.FromSeconds(5)
                || i == dataset.EvaluationIndices.Count - 1;
            if (!shouldHeartbeat)
            {
                continue;
            }

            var snapshot = new BacktestProgressSnapshot
            {
                CurrentCandleTimeUtc = processResult.Candle.CloseTimeUtc,
                CandleIndex = i + 1,
                TotalCandles = dataset.EvaluationIndices.Count,
                ElapsedSeconds = (int)Math.Max(0, (nowUtc - startedAtUtc).TotalSeconds),
                SignalsGenerated = context.TotalSignals,
                TradesGenerated = context.Trades.Count
            };

            if (context.BenchmarkRunItemId is long runItemId)
            {
                _progressStore.Report(runItemId, snapshot);
            }

            _logger.LogInformation(
                "Backtest heartbeat. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, CurrentCandleTimeUtc={CurrentCandleTimeUtc}, CandleIndex={CandleIndex}, TotalCandles={TotalCandles}, ElapsedSeconds={ElapsedSeconds}, SignalsGenerated={SignalsGenerated}, TradesGenerated={TradesGenerated}",
                context.BenchmarkRunId,
                context.BenchmarkRunItemId,
                context.BenchmarkStrategyCode,
                context.BenchmarkSymbol ?? dataset.SymbolName,
                context.BenchmarkTimeframe ?? TimeframeParser.ToApiString(dataset.Timeframe),
                snapshot.CurrentCandleTimeUtc,
                snapshot.CandleIndex,
                snapshot.TotalCandles,
                snapshot.ElapsedSeconds,
                snapshot.SignalsGenerated,
                snapshot.TradesGenerated);

            lastHeartbeatLoggedAtUtc = nowUtc;
        }

        var lastIndex = dataset.EvaluationIndices[^1];
        _executionProvider.FinalizePendingOrders(context, dataset.Candles[lastIndex], lastIndex);
        _executionProvider.UpdateOpenPositions(context, dataset.Candles[lastIndex]);

        if (_bbFunnelTracker is not null && strategies.Any(item => item.Plugin is BbLiquiditySweepCisdStrategyBase))
        {
            var funnel = _bbFunnelTracker.GetSnapshot();
            funnel.TradesCreated = context.Trades.Count;
            context.BbLiquiditySweepFunnel = funnel;
            context.BbLiquiditySweepSampleRejections = _bbFunnelTracker.GetSampleRejections();
        }

        if (strategies.Any(item => item.Plugin is VolatilityGatedSuperTrendMomentumStrategy))
        {
            if (_vgFunnelTracker is not null)
            {
                var funnel = _vgFunnelTracker.GetSnapshot();
                funnel.TradesCreated = context.Trades.Count;
                context.VgSupertrendFunnel = funnel;
            }
        }
    }

    public Task<CandleProcessResult> ProcessCandleAtIndexAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        int evaluationIndex,
        CancellationToken cancellationToken = default) =>
        ProcessCandleAtIndexAsync(context, dataset, strategies, cachedParameters: null, evaluationIndex, cancellationToken);

    public Task<CandleProcessResult> ProcessCandleAtIndexWithParametersAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>> cachedParameters,
        int evaluationIndex,
        CancellationToken cancellationToken = default) =>
        ProcessCandleAtIndexAsync(context, dataset, strategies, cachedParameters, evaluationIndex, cancellationToken);

    private async Task<CandleProcessResult> ProcessCandleAtIndexAsync(
        BacktestContext context,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>>? cachedParameters,
        int evaluationIndex,
        CancellationToken cancellationToken = default)
    {
        var signalCountBefore = context.Signals.Count;
        var aiCountBefore = context.AiDecisions.Count;
        var riskCountBefore = context.RiskDecisions.Count;
        var orderCountBefore = context.Orders.Count;
        var fillCountBefore = context.OrderFills.Count;
        var tradeCountBefore = context.Trades.Count;
        var missedCountBefore = context.MissedOrderLinks.Count;

        var candleIndex = dataset.EvaluationIndices[evaluationIndex];
        var candles = dataset.Candles;
        var candle = candles[candleIndex];

        _executionProvider.ProcessPendingMarketFills(context, candles, candleIndex);
        _executionProvider.ProcessPendingMakerOrders(context, candle, candleIndex);
        _executionProvider.UpdateOpenPositions(context, candle);

        dataset.IndicatorSnapshots.TryGetValue(candle.Id, out var snapshot);
        if (!context.OpenPositions.Any(position => position.SymbolId == dataset.SymbolId))
        {
            var regime = snapshot is null
                ? MarketRegime.Unknown
                : await DetectRegimeAsync(context, dataset, candle, snapshot, cancellationToken);
            var recentCandles = GetRecentCandles(candles, candleIndex);
            var recentSnapshots = GetRecentSnapshots(dataset, candles, candleIndex);

            foreach (var prepared in strategies)
            {
                var strategyCandles = prepared.Plugin is VolatilityGatedSuperTrendMomentumStrategy
                    || prepared.Plugin is FourHourRangeReEntryStrategy
                    || prepared.Plugin is BbLiquiditySweepCisdStrategyBase
                    ? candles
                    : recentCandles;
                IReadOnlyDictionary<string, string> parameters;
                if (cachedParameters is not null && cachedParameters.TryGetValue(prepared.Strategy.Id, out var preloaded))
                {
                    parameters = preloaded;
                }
                else
                {
                    parameters = await _parameterProvider.GetParametersAsync(
                        prepared.Strategy.Id,
                        dataset.Timeframe,
                        dataset.SymbolId,
                        cancellationToken);
                }

                var strategyContext = new StrategyContext
                {
                    TradingSessionId = context.TradingSessionId,
                    ExchangeId = context.ExchangeId,
                    SymbolId = dataset.SymbolId,
                    Symbol = dataset.SymbolName,
                    Timeframe = dataset.Timeframe,
                    HigherTimeframe = ResolveHigherTimeframe(dataset.Timeframe),
                    MarketRegime = regime,
                    Candles = strategyCandles,
                    IndicatorSnapshot = snapshot,
                    RecentIndicatorSnapshots = recentSnapshots,
                    StrategyParameters = parameters,
                    EvaluatedAtUtc = candle.CloseTimeUtc,
                    CurrentCandleIndex = candleIndex
                };

                var evaluations = await _strategyEngine.EvaluateAsync([prepared.Plugin], strategyContext, cancellationToken);
                context.StrategiesEvaluated += evaluations.Count;
                foreach (var evaluation in evaluations)
                {
                    TrackEvaluation(context, evaluation);
                }

                foreach (var evaluation in evaluations.Where(TradingPipelineConfidence.ShouldEvaluateRisk))
                {
                    context.EntrySignals++;
                    await ProcessEntrySignalAsync(
                        context,
                        dataset,
                        prepared,
                        evaluation,
                        candle,
                        candleIndex,
                        snapshot,
                        regime,
                        cancellationToken);
                }
            }
        }

        context.RecordEquityPoint(candle.CloseTimeUtc, context.BacktestRunId);

        return new CandleProcessResult
        {
            Candle = candle,
            SignalCountBefore = signalCountBefore,
            AiCountBefore = aiCountBefore,
            RiskCountBefore = riskCountBefore,
            OrderCountBefore = orderCountBefore,
            FillCountBefore = fillCountBefore,
            TradeCountBefore = tradeCountBefore,
            MissedCountBefore = missedCountBefore
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
        CancellationToken cancellationToken)
    {
        context.TotalSignals++;
        var strategyStats = GetStrategyStats(context, prepared.Strategy.Code);
        strategyStats.TotalSignals++;

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
            var aiDecision = PersistAiDecision(context, dataset, prepared, evaluation, regime, confidenceResult, candle, signal);
            if (aiDecision is not null)
            {
                aiDecisionId = aiDecision.Id > 0 ? aiDecision.Id : null;
                context.SignalAiDecisions[signal] = aiDecision;
            }
        }

        var confidenceResolution = TradingPipelineConfidence.ResolveConfidence(evaluation, aiInput);
        var confidenceScore = confidenceResolution.CombinedConfidence;
        var rawConfidenceScore = confidenceResolution.RawCombinedScore;
        var confidenceSource = confidenceResolution.Source;
        var effectiveMinConfidence = TradingPipelineConfidence.ResolveEffectiveMinimum(
            context.Settings.MinConfidenceScore,
            context.RiskRules);
        var confidenceGateEnabled = IsConfidenceGateEnabled(context.Settings.EvaluationMode);
        var confidenceApproved = !confidenceGateEnabled || confidenceScore >= effectiveMinConfidence;
        context.ConfidenceEvaluations++;
        if (confidenceApproved)
        {
            context.ConfidenceApproved++;
        }
        else
        {
            context.ConfidenceRejected++;
        }

        strategyStats.ConfidenceTotal += confidenceScore;
        strategyStats.ConfidenceCount++;

        var candidateRecord = CreateCandidateTradeRecord(
            context,
            dataset,
            prepared,
            evaluation,
            candle,
            regime,
            confidenceResolution,
            confidenceGateEnabled,
            confidenceApproved);
        var candidateIndex = context.CandidateTrades.Count;
        context.CandidateTrades.Add(candidateRecord);

        if (!confidenceApproved)
        {
            context.RejectedSignals++;
            strategyStats.RejectedSignals++;
            candidateRecord.ConfidenceApproved = false;
            candidateRecord.ConfidenceRejectionReason =
                $"Combined confidence {ConfidenceScoreNormalizer.Format(confidenceScore)} is below threshold {ConfidenceScoreNormalizer.Format(effectiveMinConfidence)}.";
            candidateRecord.FinalDecision = CandidateTradeFinalDecision.RejectedByConfidence;
            candidateRecord.FinalDecisionReason = candidateRecord.ConfidenceRejectionReason;
            TryCreateShadowTrade(context, dataset, candleIndex, candidateIndex, ShadowRejectedBy.Confidence);
            return;
        }

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
            signal,
            aiDecisionId,
            candle.CloseTimeUtc);
        context.RiskEvaluations++;
        var riskEvaluation = _riskEngine.Evaluate(riskContext);

        var riskDecision = new RiskDecision
        {
            TradingSessionId = context.TradingSessionId,
            SignalId = null,
            AiDecisionId = aiDecisionId,
            SymbolId = dataset.SymbolId,
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

        var shouldApplyRiskGate = ShouldApplyRiskGate(context.Settings.EvaluationMode);
        if (!riskEvaluation.Approved || !riskEvaluation.PositionSize.HasValue)
        {
            candidateRecord.RiskEvaluated = true;
            candidateRecord.RiskApproved = false;
            candidateRecord.RiskRejectionReason = riskEvaluation.Reason;
            if (!shouldApplyRiskGate)
            {
                candidateRecord.FinalDecision = CandidateTradeFinalDecision.SkippedByMode;
                candidateRecord.FinalDecisionReason = "Risk rejection logged in diagnostics-only mode.";
            }
            else
            {
                candidateRecord.FinalDecision = CandidateTradeFinalDecision.RejectedByRisk;
                candidateRecord.FinalDecisionReason = riskEvaluation.Reason;
            }

            context.RejectedSignals++;
            context.RiskRejected++;
            strategyStats.RejectedSignals++;
            if (shouldApplyRiskGate)
            {
                TryCreateShadowTrade(context, dataset, candleIndex, candidateIndex, ShadowRejectedBy.Risk);
                return;
            }

            // RiskOnlyResearch/FullValidation block here. Raw/Confidence-only continue with safety fallback sizing.
            if (!riskEvaluation.PositionSize.HasValue)
            {
                var fallback = ResolveFallbackSizing(entryPrice, context.Balance, evaluation, context.RiskRules);
                if (fallback.Quantity <= 0)
                {
                    candidateRecord.FinalDecision = CandidateTradeFinalDecision.RejectedInvalidTrade;
                    candidateRecord.FinalDecisionReason = "Invalid fallback quantity.";
                    TryCreateShadowTrade(context, dataset, candleIndex, candidateIndex, ShadowRejectedBy.InvalidTrade);
                    return;
                }

                candidateRecord.Quantity = fallback.Quantity;
                candidateRecord.StopLoss = fallback.StopLoss;
                candidateRecord.TakeProfit = fallback.TakeProfit;
                candidateRecord.RiskPercent = fallback.RiskPercent;
                candidateRecord.RiskAmount = fallback.RiskAmount;
                candidateRecord.RewardRiskRatio = fallback.RewardRiskRatio;
            }
        }
        else
        {
            candidateRecord.RiskEvaluated = true;
            candidateRecord.RiskApproved = true;
        }

        if (!riskEvaluation.Approved || !riskEvaluation.PositionSize.HasValue)
        {
            // Continue in non-gating modes using candidate values set above.
            var resolvedQuantity = candidateRecord.Quantity;
            var resolvedStopLoss = candidateRecord.StopLoss;
            var resolvedTakeProfit = candidateRecord.TakeProfit;
            if (resolvedQuantity <= 0)
            {
                candidateRecord.FinalDecision = CandidateTradeFinalDecision.RejectedInvalidTrade;
                candidateRecord.FinalDecisionReason = "Quantity is invalid after non-gated risk evaluation.";
                TryCreateShadowTrade(context, dataset, candleIndex, candidateIndex, ShadowRejectedBy.InvalidTrade);
                return;
            }

            context.ApprovedSignals++;
            strategyStats.ApprovedSignals++;
            QueueExecution(
                context,
                dataset,
                prepared,
                evaluation,
                signal,
                aiDecisionId,
                candle,
                candleIndex,
                resolvedQuantity,
                resolvedStopLoss,
                resolvedTakeProfit);
            candidateRecord.FinalDecision = CandidateTradeFinalDecision.Executed;
            candidateRecord.FinalDecisionReason = "Executed in non-gated mode.";
            return;
        }

        context.ApprovedSignals++;
        context.RiskApproved++;
        strategyStats.ApprovedSignals++;

        var quantity = riskEvaluation.PositionSize.Value;
        var stopLoss = riskEvaluation.StopLoss ?? evaluation.SuggestedStopLoss ?? entryPrice;
        var takeProfit = riskEvaluation.TakeProfit ?? evaluation.SuggestedTakeProfit ?? entryPrice;
        candidateRecord.Quantity = quantity;
        candidateRecord.NotionalValue = quantity * entryPrice;
        candidateRecord.MarginUsed = candidateRecord.Leverage > 0 ? candidateRecord.NotionalValue / candidateRecord.Leverage : candidateRecord.NotionalValue;
        candidateRecord.StopLoss = stopLoss;
        candidateRecord.TakeProfit = takeProfit;
        candidateRecord.RiskPercent = riskEvaluation.ApprovedRiskPercent ?? candidateRecord.RiskPercent;
        candidateRecord.RiskAmount = candidateRecord.NotionalValue * (candidateRecord.RiskPercent / 100m);
        candidateRecord.RewardRiskRatio = ResolveRewardRisk(entryPrice, stopLoss, takeProfit, evaluation.Direction);
        candidateRecord.FinalDecision = CandidateTradeFinalDecision.Executed;
        candidateRecord.FinalDecisionReason = "Approved by confidence/risk pipeline.";

        QueueExecution(
            context,
            dataset,
            prepared,
            evaluation,
            signal,
            aiDecisionId,
            candle,
            candleIndex,
            quantity,
            stopLoss,
            takeProfit);
    }

    private void QueueExecution(
        BacktestContext context,
        BacktestDataset dataset,
        PreparedStrategy prepared,
        StrategyEvaluationResult evaluation,
        StrategySignal signal,
        long? aiDecisionId,
        Candle candle,
        int candleIndex,
        decimal quantity,
        decimal stopLoss,
        decimal takeProfit)
    {
        var entryPrice = evaluation.EntryPrice ?? candle.Close;
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

    private static bool IsConfidenceGateEnabled(BenchmarkEvaluationMode mode) =>
        mode is BenchmarkEvaluationMode.ConfidenceOnlyResearch or BenchmarkEvaluationMode.FullValidation;

    private static bool ShouldApplyRiskGate(BenchmarkEvaluationMode mode) =>
        mode is BenchmarkEvaluationMode.RiskOnlyResearch or BenchmarkEvaluationMode.FullValidation;

    private static CandidateTradeRecord CreateCandidateTradeRecord(
        BacktestContext context,
        BacktestDataset dataset,
        PreparedStrategy prepared,
        StrategyEvaluationResult evaluation,
        Candle candle,
        MarketRegime regime,
        ConfidenceResolution confidenceResolution,
        bool confidenceGateEnabled,
        bool confidenceApproved)
    {
        var entry = evaluation.EntryPrice ?? candle.Close;
        var stop = evaluation.SuggestedStopLoss ?? entry;
        var target = evaluation.SuggestedTakeProfit ?? entry;
        var fallback = ResolveFallbackSizing(entry, context.Balance, evaluation, context.RiskRules);
        return new CandidateTradeRecord
        {
            SourceMode = context.SimulationMode.ToString(),
            SourceRunId = context.BacktestRunId,
            BenchmarkRunId = context.BenchmarkRunId,
            BenchmarkRunItemId = context.BenchmarkRunItemId,
            BacktestRunId = context.BacktestRunId,
            StrategyId = prepared.Strategy.Id,
            StrategyCode = prepared.Strategy.Code.ToCode(),
            StrategyName = prepared.Strategy.Name,
            ExchangeId = context.ExchangeId,
            SymbolId = dataset.SymbolId,
            Symbol = dataset.SymbolName,
            Timeframe = TimeframeParser.ToApiString(dataset.Timeframe),
            CandleId = candle.Id,
            SignalTimeUtc = candle.CloseTimeUtc,
            Direction = evaluation.Direction.ToString(),
            SignalType = evaluation.SignalType.ToString(),
            EntryPrice = entry,
            StopLoss = stop,
            TakeProfit = target,
            Quantity = fallback.Quantity,
            Leverage = fallback.Leverage,
            MarginUsed = fallback.MarginUsed,
            NotionalValue = fallback.NotionalValue,
            RiskAmount = fallback.RiskAmount,
            RiskPercent = fallback.RiskPercent,
            RewardRiskRatio = fallback.RewardRiskRatio,
            StrategyConfidence = confidenceResolution.StrategyConfidence,
            AiConfidence = confidenceResolution.AiConfidence,
            CombinedConfidence = confidenceResolution.CombinedConfidence,
            MarketRegime = regime.ToString(),
            EvaluationMode = context.Settings.EvaluationMode.ToString(),
            ConfidenceGateEnabled = confidenceGateEnabled,
            ConfidenceApproved = confidenceApproved,
            CreatedAtUtc = DateTime.UtcNow,
            FinalDecision = CandidateTradeFinalDecision.SkippedByMode,
            FinalDecisionReason = "Candidate created."
        };
    }

    private static void TryCreateShadowTrade(
        BacktestContext context,
        BacktestDataset dataset,
        int candleIndex,
        int candidateIndex,
        ShadowRejectedBy rejectedBy)
    {
        if (!context.Settings.EnableShadowTradeAnalysis)
        {
            return;
        }

        var candidate = context.CandidateTrades[candidateIndex];
        var shadow = new ShadowTradeRecord
        {
            CandidateTradeIndex = candidateIndex,
            SourceMode = candidate.SourceMode,
            SourceRunId = candidate.SourceRunId,
            BenchmarkRunId = candidate.BenchmarkRunId,
            BenchmarkRunItemId = candidate.BenchmarkRunItemId,
            BacktestRunId = context.BacktestRunId,
            StrategyId = candidate.StrategyId,
            StrategyCode = candidate.StrategyCode,
            SymbolId = candidate.SymbolId,
            Symbol = candidate.Symbol,
            Timeframe = candidate.Timeframe,
            Direction = candidate.Direction,
            SignalTimeUtc = candidate.SignalTimeUtc,
            HypotheticalEntryPrice = candidate.EntryPrice,
            StopLoss = candidate.StopLoss,
            TakeProfit = candidate.TakeProfit,
            Quantity = candidate.Quantity,
            Leverage = candidate.Leverage,
            MarginUsed = candidate.MarginUsed,
            NotionalValue = candidate.NotionalValue,
            RiskAmount = candidate.RiskAmount,
            RewardRiskRatio = candidate.RewardRiskRatio,
            RejectedBy = rejectedBy,
            RejectionReason = candidate.FinalDecisionReason,
            CreatedAtUtc = DateTime.UtcNow
        };

        SimulateShadowOutcome(dataset.Candles, candleIndex, context.Settings, shadow);
        context.ShadowTrades.Add(shadow);
    }

    private static void SimulateShadowOutcome(
        IReadOnlyList<Candle> candles,
        int signalCandleIndex,
        RunBacktestSettings settings,
        ShadowTradeRecord shadow)
    {
        var policy = ConservativeStopFirstIntrabarPolicy.Instance;
        shadow.IntrabarPolicyVersion = policy.Version;
        shadow.EntryModel = settings.ShadowEntryModel;
        shadow.ProposedEntryPrice = shadow.HypotheticalEntryPrice;

        var entryCandleIndex = signalCandleIndex + 1;
        if (entryCandleIndex >= candles.Count)
        {
            shadow.OutcomeClassification = ShadowOutcomeClassification.NotEnoughFutureData;
            shadow.EntryStatus = ShadowEntryStatus.NotEnoughFutureData;
            shadow.ShadowExitReason = "EndOfData";
            shadow.EntryExclusionReason = "No candle after signal for entry model evaluation.";
            return;
        }

        var direction = shadow.Direction.Equals("Long", StringComparison.OrdinalIgnoreCase)
            ? TradeDirection.Long
            : TradeDirection.Short;

        var entryPrice = shadow.HypotheticalEntryPrice;
        DateTime? triggeredAt = null;
        decimal? triggerPrice = null;
        DateTime? triggerOpen = null;
        string? triggerEvidence = null;
        var entryIndex = -1;

        for (var i = entryCandleIndex; i < candles.Count; i++)
        {
            var candle = candles[i];
            var (triggered, price, evidence) = TryEvaluateShadowEntry(
                settings.ShadowEntryModel,
                direction,
                entryPrice,
                candle,
                policy);

            if (!triggered)
            {
                continue;
            }

            entryIndex = i;
            triggeredAt = candle.OpenTimeUtc;
            triggerPrice = price;
            triggerOpen = candle.OpenTimeUtc;
            triggerEvidence = evidence;
            entryPrice = price;
            break;
        }

        if (entryIndex < 0)
        {
            shadow.WouldEntryTrigger = false;
            shadow.EntryStatus = ShadowEntryStatus.NotTriggered;
            shadow.OutcomeClassification = ShadowOutcomeClassification.NotTriggered;
            shadow.ShadowExitReason = "NotTriggered";
            shadow.EntryExclusionReason = "Entry model never triggered.";
            return;
        }

        shadow.WouldEntryTrigger = true;
        shadow.EntryStatus = ShadowEntryStatus.Triggered;
        shadow.TriggeredAtUtc = triggeredAt;
        shadow.TriggerPrice = triggerPrice;
        shadow.TriggerCandleOpenTimeUtc = triggerOpen;
        shadow.TriggerEvidence = triggerEvidence;

        var maxFavorable = 0m;
        var maxAdverse = 0m;

        for (var i = entryIndex; i < candles.Count; i++)
        {
            var candle = candles[i];
            var pnlHigh = direction == TradeDirection.Long
                ? (candle.High - entryPrice) * shadow.Quantity
                : (entryPrice - candle.Low) * shadow.Quantity;
            var pnlLow = direction == TradeDirection.Long
                ? (candle.Low - entryPrice) * shadow.Quantity
                : (entryPrice - candle.High) * shadow.Quantity;
            maxFavorable = Math.Max(maxFavorable, pnlHigh);
            maxAdverse = Math.Min(maxAdverse, pnlLow);

            // Same-candle entry+exit: evaluate protective exits under shared policy.
            var exitDecision = policy.EvaluateProtectiveExits(
                direction,
                shadow.StopLoss,
                shadow.TakeProfit,
                candle,
                settings.SameCandleExitPolicy);

            if (exitDecision.ExitPrice is decimal exitPx
                && exitDecision.ChosenEvent is not IntrabarChosenEvent.None)
            {
                shadow.ShadowExitTimeUtc = candle.CloseTimeUtc;
                shadow.ShadowExitPrice = exitPx;
                shadow.ShadowExitReason = exitDecision.ChosenEvent is IntrabarChosenEvent.Target
                    or IntrabarChosenEvent.GapBeyondTarget
                    ? "TakeProfit"
                    : "StopLoss";
                shadow.DurationCandles = i - signalCandleIndex;
                shadow.DurationMinutes = shadow.DurationCandles * ResolveMinutesFromTimeframe(shadow.Timeframe);
                break;
            }
        }

        if (!shadow.ShadowExitPrice.HasValue)
        {
            var lastCandle = candles[^1];
            shadow.ShadowExitTimeUtc = lastCandle.CloseTimeUtc;
            shadow.ShadowExitPrice = lastCandle.Close;
            shadow.ShadowExitReason = "EndOfData";
            shadow.DurationCandles = Math.Max(0, candles.Count - signalCandleIndex - 1);
            shadow.DurationMinutes = shadow.DurationCandles * ResolveMinutesFromTimeframe(shadow.Timeframe);
        }

        var gross = direction == TradeDirection.Long
            ? (shadow.ShadowExitPrice.Value - entryPrice) * shadow.Quantity
            : (entryPrice - shadow.ShadowExitPrice.Value) * shadow.Quantity;
        var feeRate = settings.TakerFeeRate;
        var fees = (entryPrice * shadow.Quantity + shadow.ShadowExitPrice.Value * shadow.Quantity) * feeRate;
        shadow.ShadowGrossPnl = gross;
        shadow.ShadowFees = fees;
        shadow.ShadowNetPnl = gross - fees;
        shadow.ShadowNetPnlPercent = shadow.MarginUsed > 0 ? (shadow.ShadowNetPnl / shadow.MarginUsed) * 100m : 0m;
        shadow.MaxFavorableExcursion = maxFavorable;
        shadow.MaxAdverseExcursion = maxAdverse;
        shadow.MaxFavorableExcursionPercent = shadow.MarginUsed > 0 ? (maxFavorable / shadow.MarginUsed) * 100m : 0m;
        shadow.MaxAdverseExcursionPercent = shadow.MarginUsed > 0 ? (maxAdverse / shadow.MarginUsed) * 100m : 0m;

        shadow.OutcomeClassification = shadow.ShadowNetPnl switch
        {
            > 0 => ShadowOutcomeClassification.WouldHaveWon,
            < 0 => ShadowOutcomeClassification.WouldHaveLost,
            _ => ShadowOutcomeClassification.BreakEven
        };
    }

    private static (bool Triggered, decimal Price, string Evidence) TryEvaluateShadowEntry(
        ShadowEntryModel model,
        TradeDirection direction,
        decimal proposedEntryPrice,
        Candle candle,
        IIntrabarExecutionPolicy policy)
    {
        switch (model)
        {
            case ShadowEntryModel.MarketNextOpen:
                return (true, candle.Open, "MarketNextOpen at candle open.");
            case ShadowEntryModel.MarketNextClose:
                return (true, candle.Close, "MarketNextClose at candle close.");
            case ShadowEntryModel.LimitAtPrice:
            case ShadowEntryModel.MakerLimit:
                if (policy.LimitEntryTouched(direction, proposedEntryPrice, candle))
                {
                    return (true, proposedEntryPrice, $"{model}: limit touched within OHLC.");
                }

                return (false, proposedEntryPrice, $"{model}: limit not touched.");
            case ShadowEntryModel.StopAtPrice:
                if (policy.StopEntryTriggered(direction, proposedEntryPrice, candle))
                {
                    return (true, proposedEntryPrice, "StopAtPrice: stop-entry threshold crossed.");
                }

                return (false, proposedEntryPrice, "StopAtPrice: threshold not crossed.");
            default:
                return (false, proposedEntryPrice, "NotApplicable");
        }
    }

    private static bool ResolveSameCandleStopFirst(
        SameCandleExitPolicy policy,
        TradeDirection direction,
        bool stopHit,
        bool targetHit)
    {
        if (stopHit && !targetHit)
        {
            return true;
        }

        if (!stopHit && targetHit)
        {
            return false;
        }

        if (!stopHit && !targetHit)
        {
            return false;
        }

        return policy switch
        {
            SameCandleExitPolicy.TargetFirst => false,
            SameCandleExitPolicy.OpenHighLowCloseHeuristic => direction == TradeDirection.Long,
            _ => true
        };
    }

    private static (decimal Quantity, decimal Leverage, decimal MarginUsed, decimal NotionalValue, decimal RiskAmount, decimal RiskPercent, decimal RewardRiskRatio, decimal StopLoss, decimal TakeProfit)
        ResolveFallbackSizing(
            decimal entryPrice,
            decimal balance,
            StrategyEvaluationResult evaluation,
            IReadOnlyList<Domain.Risk.RiskRule> rules)
    {
        var maxRisk = rules.FirstOrDefault(rule =>
                string.Equals(rule.RuleKey, RiskRuleKeys.MaxRiskPerTradePercent, StringComparison.OrdinalIgnoreCase))
            ?.RuleValue;
        var riskPercent = decimal.TryParse(maxRisk, out var parsedRisk) ? parsedRisk : 1m;
        riskPercent = Math.Clamp(riskPercent, 0.1m, 10m);
        var riskAmount = balance * (riskPercent / 100m);
        var stop = evaluation.SuggestedStopLoss ?? entryPrice * 0.995m;
        var target = evaluation.SuggestedTakeProfit ?? entryPrice * 1.01m;
        var stopDistance = Math.Max(Math.Abs(entryPrice - stop), entryPrice * 0.001m);
        var quantity = stopDistance > 0 ? riskAmount / stopDistance : 0m;
        var notional = quantity * entryPrice;
        var leverage = notional > 0 ? Math.Max(1m, Math.Min(5m, notional / Math.Max(riskAmount * 5m, 1m))) : 1m;
        var margin = leverage > 0 ? notional / leverage : notional;
        var rewardRiskRatio = ResolveRewardRisk(entryPrice, stop, target, evaluation.Direction);
        return (quantity, leverage, margin, notional, riskAmount, riskPercent, rewardRiskRatio, stop, target);
    }

    private static decimal ResolveRewardRisk(decimal entryPrice, decimal stopLoss, decimal takeProfit, TradeDirection direction)
    {
        var risk = direction == TradeDirection.Long
            ? Math.Max(0m, entryPrice - stopLoss)
            : Math.Max(0m, stopLoss - entryPrice);
        var reward = direction == TradeDirection.Long
            ? Math.Max(0m, takeProfit - entryPrice)
            : Math.Max(0m, entryPrice - takeProfit);
        if (risk <= 0m)
        {
            return 0m;
        }

        return reward / risk;
    }

    private static int ResolveMinutesFromTimeframe(string timeframe) =>
        TimeframeParser.TryParse(timeframe, out var parsed)
            ? parsed switch
            {
                Timeframe.M1 => 1,
                Timeframe.M3 => 3,
                Timeframe.M5 => 5,
                Timeframe.M15 => 15,
                Timeframe.M30 => 30,
                Timeframe.H1 => 60,
                Timeframe.H4 => 240,
                Timeframe.D1 => 1440,
                _ => 1
            }
            : 1;

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
        Candle candle,
        StrategySignal signal)
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
            SignalId = null,
            MarketRegime = regime,
            RegimeConfidence = null,
            ConfidenceScore = resolution.AiConfidence ?? resolution.StrategyConfidence,
            ConfidenceClassification = confidence.Response.Classification,
            PreferredStrategyCode = prepared.Strategy.Code,
            IsAnomalous = false,
            TradeAllowed = resolution.CombinedConfidence >= effectiveMin && !confidence.Response.UsedFallback,
            Summary = $"Backtest AI score {ConfidenceScoreNormalizer.Format(resolution.AiConfidence ?? 0m)}",
            Explanation = confidence.Response.Reasons.FirstOrDefault() ?? "Backtest AI evaluation",
            ReasonsJson = JsonSerializer.Serialize(confidence.Response.Reasons),
            WarningsJson = confidence.Response.Warnings.Count > 0 ? JsonSerializer.Serialize(confidence.Response.Warnings) : null,
            CreatedAtUtc = candle.CloseTimeUtc
        };

        context.AiDecisions.Add(decision);
        return decision;
    }

    private static void PersistRiskRejection(
        BacktestContext context,
        long symbolId,
        StrategySignal signal,
        long? aiDecisionId,
        decimal confidenceScore,
        MarketRegime regime,
        string ruleKey,
        string reason,
        DateTime evaluatedAtUtc)
    {
        context.RiskDecisions.Add(new RiskDecision
        {
            TradingSessionId = context.TradingSessionId,
            SignalId = null,
            AiDecisionId = aiDecisionId,
            SymbolId = symbolId,
            Decision = RiskDecisionType.Rejected,
            Reason = reason,
            RejectedRuleKey = ruleKey,
            CreatedAtUtc = evaluatedAtUtc
        });
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
        StrategySignal signal,
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
            SignalId = null,
            AiDecisionId = aiDecisionId,
            AccountBalance = context.Balance,
            DailyPnl = context.DailyPnl,
            WeeklyPnl = context.WeeklyPnl,
            OpenPositionCount = context.OpenPositions.Count,
            OpenSymbolExposure = symbolExposure,
            TotalExposure = context.OpenPositions.Sum(position => position.EntryPrice * position.Quantity),
            ConsecutiveLosses = context.ConsecutiveLosses,
            AtrPercent = null,
            MarketRegime = regime,
            EmergencyStopEnabled = context.EmergencyStopEnabled,
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

    private static Timeframe ResolveHigherTimeframe(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 or Timeframe.M3 or Timeframe.M5 => Timeframe.M15,
        Timeframe.M15 or Timeframe.M30 => Timeframe.H1,
        Timeframe.H1 => Timeframe.H4,
        _ => Timeframe.D1
    };
}
