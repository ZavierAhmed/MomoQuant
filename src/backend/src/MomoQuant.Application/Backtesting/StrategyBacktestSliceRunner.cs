using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Backtesting;

public sealed class StrategyBacktestSliceRequest
{
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public DateTime EvaluationFromUtc { get; init; }
    public DateTime EvaluationToUtc { get; init; }
    public required string StrategyCode { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public long RiskProfileId { get; init; }
    public decimal InitialBalance { get; init; }
    public StrategyResearchExecutionOptions? Options { get; init; }
    public int? WarmupCandlesOverride { get; init; }
}

public interface IStrategyBacktestSliceRunner
{
    Task<StrategyResearchBacktestResult?> RunSliceAsync(
        StrategyBacktestSliceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyBacktestSliceRunner : IStrategyBacktestSliceRunner
{
    private readonly IBacktestDataLoader _dataLoader;
    private readonly IBacktestEngine _backtestEngine;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IVolatilityGatedSuperTrendFunnelTracker _vgFunnelTracker;
    private readonly ILogger<StrategyBacktestSliceRunner> _logger;

    public StrategyBacktestSliceRunner(
        IBacktestDataLoader dataLoader,
        IBacktestEngine backtestEngine,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        IRiskRuleRepository riskRuleRepository,
        ISymbolRepository symbolRepository,
        IStrategyDataRequirementService requirementService,
        IVolatilityGatedSuperTrendFunnelTracker vgFunnelTracker,
        ILogger<StrategyBacktestSliceRunner>? logger = null)
    {
        _dataLoader = dataLoader;
        _backtestEngine = backtestEngine;
        _strategyRepository = strategyRepository;
        _strategyRegistry = strategyRegistry;
        _riskRuleRepository = riskRuleRepository;
        _symbolRepository = symbolRepository;
        _requirementService = requirementService;
        _vgFunnelTracker = vgFunnelTracker;
        _logger = logger ?? NullLogger<StrategyBacktestSliceRunner>.Instance;
    }

    public async Task<StrategyResearchBacktestResult?> RunSliceAsync(
        StrategyBacktestSliceRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = request.Options ?? new StrategyResearchExecutionOptions();
        if (!TimeframeParser.TryParse(request.Timeframe, out var parsedTimeframe))
        {
            return null;
        }

        var strategyEnum = StrategyCodeExtensions.FromCode(request.StrategyCode);
        var strategyEntity = await _strategyRepository.GetByCodeAsync(strategyEnum, cancellationToken);
        if (strategyEntity is null)
        {
            return null;
        }

        var plugin = _strategyRegistry.GetByCode(strategyEnum);
        if (plugin is null)
        {
            return null;
        }

        var resolvedParameters = VgResearchProfilePresets.AppliesToStrategy(request.StrategyCode)
            ? VgResearchProfilePresets.Apply(options.VgResearchProfile, request.Parameters)
            : request.Parameters;

        var warmup = request.WarmupCandlesOverride ?? await ResolveWarmupCandlesAsync(strategyEntity.Id, plugin, resolvedParameters, cancellationToken);

        var dataset = await _dataLoader.LoadSymbolTimeframeAsync(
            request.ExchangeId,
            request.SymbolId,
            parsedTimeframe,
            request.EvaluationFromUtc,
            request.EvaluationToUtc,
            warmup,
            cancellationToken);
        if (dataset is null)
        {
            return null;
        }

        var evaluationIndices = dataset.EvaluationIndices
            .Where(i => dataset.Candles[i].OpenTimeUtc >= request.EvaluationFromUtc
                        && dataset.Candles[i].OpenTimeUtc < request.EvaluationToUtc)
            .ToList();
        if (evaluationIndices.Count == 0)
        {
            return null;
        }

        var totalLoaded = dataset.Candles.Count;
        var warmupLoaded = Math.Max(0, totalLoaded - evaluationIndices.Count);
        var sliceDataset = new BacktestDataset
        {
            SymbolId = dataset.SymbolId,
            SymbolName = dataset.SymbolName,
            Timeframe = dataset.Timeframe,
            Candles = dataset.Candles,
            IndicatorSnapshots = dataset.IndicatorSnapshots,
            EvaluationIndices = evaluationIndices
        };

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(request.RiskProfileId, cancellationToken);
        var executionMode = Enum.TryParse<ExecutionMode>(options.ExecutionMode, true, out var mode)
            ? mode
            : ExecutionMode.MarketFill;

        var symbols = new Dictionary<long, Domain.Exchanges.Symbol>();
        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is not null)
        {
            symbols[request.SymbolId] = symbol;
        }

        var context = new BacktestContext
        {
            BacktestRunId = 0,
            TradingSessionId = -Math.Abs(HashCode.Combine(request.StrategyCode, request.SymbolId, request.EvaluationFromUtc, request.EvaluationToUtc)),
            ExchangeId = request.ExchangeId,
            RiskProfileId = request.RiskProfileId,
            Settings = new RunBacktestSettings
            {
                Name = "StrategyBacktestSlice",
                SymbolIds = [request.SymbolId],
                Timeframes = [parsedTimeframe],
                FromUtc = request.EvaluationFromUtc,
                ToUtc = request.EvaluationToUtc,
                InitialBalance = request.InitialBalance,
                StrategyIds = [strategyEntity.Id],
                ExecutionMode = executionMode,
                MakerFeeRate = options.MakerFeeRate,
                TakerFeeRate = options.TakerFeeRate,
                OrderExpiryCandles = options.OrderExpiryCandles,
                UseAiScoring = options.UseAiScoring,
                MinConfidenceScore = options.MinConfidenceScore,
                SlippagePercent = options.SlippagePercent,
                EvaluationMode = BenchmarkEvaluationMode.FullValidation,
                EnableShadowTradeAnalysis = false
            },
            RiskRules = riskRules,
            Strategies = [strategyEntity],
            Symbols = symbols,
            Balance = request.InitialBalance,
            PeakEquity = request.InitialBalance,
            BenchmarkStrategyCode = request.StrategyCode
        };

        var prepared = new PreparedStrategy { Strategy = strategyEntity, Plugin = plugin };
        var cachedParams = new Dictionary<long, IReadOnlyDictionary<string, string>>
        {
            [strategyEntity.Id] = resolvedParameters
        };

        await _backtestEngine.RunDatasetWithParametersAsync(context, sliceDataset, [prepared], cachedParams, cancellationToken);

        var vgFunnel = context.VgSupertrendFunnel;
        if (vgFunnel is null && plugin is VolatilityGatedSuperTrendMomentumStrategy)
        {
            vgFunnel = _vgFunnelTracker.GetSnapshot();
            vgFunnel.TradesCreated = context.Trades.Count;
        }
        else if (vgFunnel is not null)
        {
            vgFunnel.TradesCreated = context.Trades.Count;
        }

        var funnel = StrategyResearchFunnelMapper.MapFromContext(request.StrategyCode, vgFunnel, context.RiskRejected);
        var metrics = StrategyPerformanceMetricsMapper.FromContext(context);
        var evaluationCount = Math.Max(context.StrategiesEvaluated, funnel?.Evaluations ?? 0);
        var engineEvaluationBug = evaluationIndices.Count > warmup && evaluationCount == 0;

        if (engineEvaluationBug)
        {
            _logger.LogError(
                "EngineEvaluationBug: candles loaded but no evaluations recorded. StrategyCode={StrategyCode}, SymbolId={SymbolId}, Timeframe={Timeframe}, EvaluationCandles={EvaluationCandles}, WarmupCandles={WarmupCandles}, LoadedTotal={LoadedTotal}",
                request.StrategyCode,
                request.SymbolId,
                request.Timeframe,
                evaluationIndices.Count,
                warmup,
                totalLoaded);
        }

        var zeroTrade = ZeroTradeAnalyzer.Analyze(
            request.StrategyCode,
            funnel,
            metrics.TradeCount,
            evaluationIndices.Count,
            warmup,
            evaluationCount,
            context.RiskRejected,
            engineEvaluationBug);

        return new StrategyResearchBacktestResult
        {
            Metrics = metrics,
            BacktestEngineUsed = "BacktestEngine",
            CandleCount = evaluationIndices.Count,
            TotalCandlesLoaded = totalLoaded,
            WarmupCandlesLoaded = warmupLoaded,
            WarmupCandlesRequired = warmup,
            SkippedForWarmupCount = warmupLoaded,
            IndicatorSnapshotCount = sliceDataset.IndicatorSnapshots.Count,
            StrategyEvaluations = evaluationCount,
            EntrySignals = context.EntrySignals,
            RiskRejectedCount = context.RiskRejected,
            Funnel = funnel,
            ZeroTradeAnalysis = zeroTrade,
            DiagnosticsSummary = funnel?.PipelineSummary,
            EngineEvaluationBug = engineEvaluationBug
        };
    }

    private async Task<int> ResolveWarmupCandlesAsync(
        long strategyId,
        ITradingStrategy plugin,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        if (plugin is VolatilityGatedSuperTrendMomentumStrategy)
        {
            return new VolatilityGatedSuperTrendContextService()
                .GetWarmupCandles(VolatilityGatedSuperTrendParameters.From(parameters));
        }

        var requirement = await _requirementService.GetByStrategyIdAsync(strategyId, cancellationToken);
        return requirement.Data?.WarmupCandles ?? 500;
    }
}
