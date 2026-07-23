using System.Text.Json;
using System.Text.Json.Nodes;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Simulation;
using MomoQuant.Application.Simulation.Dtos;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Sessions;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MomoQuant.Application.Backtesting;

public interface IBacktestRunner
{
    Task<ServiceResult<RunBacktestResponse>> RunAsync(RunBacktestRequest request, CancellationToken cancellationToken = default);
}

public sealed class BacktestRunner : IBacktestRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IBacktestRunRepository _backtestRunRepository;
    private readonly IBacktestResultRepository _backtestResultRepository;
    private readonly IBacktestEquityPointRepository _equityPointRepository;
    private readonly IBacktestStrategyResultRepository _strategyResultRepository;
    private readonly IBacktestSymbolResultRepository _symbolResultRepository;
    private readonly ITradingSessionRepository _sessionRepository;
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderFillRepository _orderFillRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly IUserRepository? _userRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly IBacktestDataLoader _dataLoader;
    private readonly IBacktestEngine _backtestEngine;
    private readonly IBacktestMetricsCalculator _metricsCalculator;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly ITradingSessionPreflightValidator _preflightValidator;
    private readonly IMarketDataCoverageService? _coverageService;
    private readonly IStrategyDataRequirementService? _requirementService;
    private readonly IBacktestProgressStore _progressStore;
    private readonly ISimulationRunSummaryService? _summaryService;
    private readonly ILogger<BacktestRunner> _logger;

    public BacktestRunner(
        IBacktestRunRepository backtestRunRepository,
        IBacktestResultRepository backtestResultRepository,
        IBacktestEquityPointRepository equityPointRepository,
        IBacktestStrategyResultRepository strategyResultRepository,
        IBacktestSymbolResultRepository symbolResultRepository,
        ITradingSessionRepository sessionRepository,
        IStrategySignalRepository signalRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        IOrderRepository orderRepository,
        IOrderFillRepository orderFillRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        IRiskProfileRepository riskProfileRepository,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        IRiskRuleRepository riskRuleRepository,
        IBacktestDataLoader dataLoader,
        IBacktestEngine backtestEngine,
        IBacktestMetricsCalculator metricsCalculator,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ITradingSessionPreflightValidator preflightValidator,
        IBacktestProgressStore? progressStore = null,
        ILogger<BacktestRunner>? logger = null,
        ISimulationRunSummaryService? summaryService = null,
        IMarketDataCoverageService? coverageService = null,
        IStrategyDataRequirementService? requirementService = null)
        : this(
            backtestRunRepository,
            backtestResultRepository,
            equityPointRepository,
            strategyResultRepository,
            symbolResultRepository,
            sessionRepository,
            signalRepository,
            riskDecisionRepository,
            aiDecisionRepository,
            orderRepository,
            orderFillRepository,
            tradeRepository,
            missedOrderRepository,
            exchangeRepository,
            userRepository: null,
            symbolRepository,
            riskProfileRepository,
            strategyRepository,
            strategyRegistry,
            riskRuleRepository,
            dataLoader,
            backtestEngine,
            metricsCalculator,
            currentUserService,
            auditService,
            preflightValidator,
            progressStore,
            logger,
            summaryService,
            coverageService,
            requirementService)
    {
    }

    public BacktestRunner(
        IBacktestRunRepository backtestRunRepository,
        IBacktestResultRepository backtestResultRepository,
        IBacktestEquityPointRepository equityPointRepository,
        IBacktestStrategyResultRepository strategyResultRepository,
        IBacktestSymbolResultRepository symbolResultRepository,
        ITradingSessionRepository sessionRepository,
        IStrategySignalRepository signalRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        IOrderRepository orderRepository,
        IOrderFillRepository orderFillRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IExchangeRepository exchangeRepository,
        IUserRepository? userRepository,
        ISymbolRepository symbolRepository,
        IRiskProfileRepository riskProfileRepository,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        IRiskRuleRepository riskRuleRepository,
        IBacktestDataLoader dataLoader,
        IBacktestEngine backtestEngine,
        IBacktestMetricsCalculator metricsCalculator,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ITradingSessionPreflightValidator preflightValidator,
        IBacktestProgressStore? progressStore = null,
        ILogger<BacktestRunner>? logger = null,
        ISimulationRunSummaryService? summaryService = null,
        IMarketDataCoverageService? coverageService = null,
        IStrategyDataRequirementService? requirementService = null)
    {
        _backtestRunRepository = backtestRunRepository;
        _backtestResultRepository = backtestResultRepository;
        _equityPointRepository = equityPointRepository;
        _strategyResultRepository = strategyResultRepository;
        _symbolResultRepository = symbolResultRepository;
        _sessionRepository = sessionRepository;
        _signalRepository = signalRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _orderRepository = orderRepository;
        _orderFillRepository = orderFillRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
        _exchangeRepository = exchangeRepository;
        _userRepository = userRepository;
        _symbolRepository = symbolRepository;
        _riskProfileRepository = riskProfileRepository;
        _strategyRepository = strategyRepository;
        _strategyRegistry = strategyRegistry;
        _riskRuleRepository = riskRuleRepository;
        _dataLoader = dataLoader;
        _backtestEngine = backtestEngine;
        _metricsCalculator = metricsCalculator;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _preflightValidator = preflightValidator;
        _coverageService = coverageService;
        _requirementService = requirementService;
        _progressStore = progressStore ?? new BacktestProgressStore();
        _summaryService = summaryService;
        _logger = logger ?? NullLogger<BacktestRunner>.Instance;
    }

    public async Task<ServiceResult<RunBacktestResponse>> RunAsync(
        RunBacktestRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateRequestAsync(request, cancellationToken);
        if (!validation.Succeeded)
        {
            return ServiceResult<RunBacktestResponse>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        var settings = validation.Data!;

        if (request.AutoImportMissingCandles && _coverageService is not null)
        {
            var coverageResult = await EnsureBacktestCoverageAsync(request, settings, cancellationToken);
            if (!coverageResult.Succeeded)
            {
                return ServiceResult<RunBacktestResponse>.Fail(coverageResult.ErrorMessage!, coverageResult.ErrorField);
            }
        }

        var preflight = await _preflightValidator.ValidateAsync(new TradingSessionPreflightRequest
        {
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolIds[0],
            Timeframe = settings.Timeframes[0],
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            StrategyIds = request.StrategyIds,
            RiskProfileId = request.RiskProfileId,
            SessionMinConfidenceScore = request.MinConfidenceScore,
            UseAiScoring = request.UseAiScoring,
            StrictAiRequired = request.StrictAiRequired,
            RunAnyway = request.RunAnyway
        }, cancellationToken);

        if (!preflight.Succeeded)
        {
            return ServiceResult<RunBacktestResponse>.Fail(preflight.ErrorMessage!, preflight.ErrorField);
        }

        var startedAtUtc = DateTime.UtcNow;
        var startedByUserId = await ResolveStartedByUserIdAsync(request, cancellationToken);
        if (startedByUserId <= 0)
        {
            return ServiceResult<RunBacktestResponse>.Fail("Unable to resolve a valid user for the benchmark backtest.");
        }

        var session = new TradingSession
        {
            Name = $"Backtest: {request.Name}",
            Mode = TradingMode.Backtest,
            Status = TradingSessionStatus.Running,
            ExchangeId = request.ExchangeId,
            StartedByUserId = startedByUserId,
            StartedAtUtc = startedAtUtc,
            InitialBalance = request.InitialBalance,
            CreatedAtUtc = startedAtUtc,
            UpdatedAtUtc = startedAtUtc
        };

        await _sessionRepository.AddAsync(session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);

        var run = new BacktestRun
        {
            TradingSessionId = session.Id,
            Name = request.Name,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolIds[0],
            Timeframe = settings.Timeframes[0],
            HigherTimeframe = ResolveHigherTimeframe(settings.Timeframes[0]),
            StartDateUtc = request.FromUtc,
            EndDateUtc = request.ToUtc,
            InitialBalance = request.InitialBalance,
            RiskProfileId = request.RiskProfileId,
            ExecutionMode = settings.ExecutionMode,
            UseAiScoring = request.UseAiScoring,
            RequestedByUserId = startedByUserId,
            StrategySetJson = JsonSerializer.Serialize(request.StrategyIds, JsonOptions),
            SettingsJson = JsonSerializer.Serialize(new
            {
                request.MakerFeeRate,
                request.TakerFeeRate,
                request.OrderExpiryCandles,
                request.MinConfidenceScore,
                request.SlippagePercent,
                request.EvaluationMode,
                request.EnableShadowTradeAnalysis,
                request.SameCandleExitPolicy
            }, JsonOptions),
            ConfigJson = JsonSerializer.Serialize(request, JsonOptions),
            Status = BacktestRunStatus.Running,
            StartedAtUtc = startedAtUtc,
            CreatedAtUtc = startedAtUtc,
            UpdatedAtUtc = startedAtUtc
        };

        await _backtestRunRepository.AddAsync(run, cancellationToken);
        await _backtestRunRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "BACKTEST_STARTED",
            nameof(BacktestRun),
            entityId: run.Id,
            userId: _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new { request.Name, request.ExchangeId, request.FromUtc, request.ToUtc }, JsonOptions),
            cancellationToken: cancellationToken);

        var symbols = new Dictionary<long, Domain.Exchanges.Symbol>();
        foreach (var symbolId in request.SymbolIds)
        {
            var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
            if (symbol is not null)
            {
                symbols[symbolId] = symbol;
            }
        }

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(request.RiskProfileId, cancellationToken);
        var preparedStrategies = await LoadPreparedStrategiesAsync(request.StrategyIds, cancellationToken);

        var context = new BacktestContext
        {
            BacktestRunId = run.Id,
            TradingSessionId = session.Id,
            ExchangeId = request.ExchangeId,
            RiskProfileId = request.RiskProfileId,
            Settings = settings,
            RiskRules = riskRules,
            Strategies = preparedStrategies.Select(item => item.Strategy).ToList(),
            Symbols = symbols,
            Balance = request.InitialBalance,
            PeakEquity = request.InitialBalance,
            BenchmarkRunId = request.BenchmarkRunId,
            BenchmarkRunItemId = request.BenchmarkRunItemId,
            BenchmarkStrategyCode = request.BenchmarkStrategyCode,
            BenchmarkSymbol = request.BenchmarkSymbol,
            BenchmarkTimeframe = request.BenchmarkTimeframe
        };

        try
        {
            if (request.BenchmarkRunItemId is long benchmarkRunItemId)
            {
                _progressStore.Clear(benchmarkRunItemId);
            }

            var hasData = false;
            var lastCandleCount = 0;
            var lastIndicatorCount = 0;
            foreach (var symbolId in request.SymbolIds)
            {
                foreach (var timeframe in settings.Timeframes)
                {
                    var dataset = await _dataLoader.LoadSymbolTimeframeAsync(
                        request.ExchangeId,
                        symbolId,
                        timeframe,
                        request.FromUtc,
                        request.ToUtc,
                        warmUpCount: 600,
                        cancellationToken);

                    if (dataset is null)
                    {
                        continue;
                    }

                    hasData = true;
                    lastCandleCount = dataset.EvaluationIndices.Count;
                    lastIndicatorCount = dataset.IndicatorSnapshots.Count;
                    _logger.LogInformation(
                        "Loaded backtest dataset. BacktestRunId={BacktestRunId}, BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, CandleCount={CandleCount}, IndicatorSnapshotCount={IndicatorSnapshotCount}, FromUtc={FromUtc}, ToUtc={ToUtc}",
                        run.Id,
                        request.BenchmarkRunId,
                        request.BenchmarkRunItemId,
                        request.BenchmarkStrategyCode,
                        dataset.SymbolName,
                        TimeframeParser.ToApiString(dataset.Timeframe),
                        dataset.EvaluationIndices.Count,
                        dataset.IndicatorSnapshots.Count,
                        request.FromUtc,
                        request.ToUtc);
                    await _backtestEngine.RunDatasetAsync(context, dataset, preparedStrategies, cancellationToken);
                }
            }

            if (!hasData)
            {
                throw new InvalidOperationException(
                    request.AutoImportMissingCandles
                        ? "No candle data exists for the requested backtest range after coverage import."
                        : "No candle data exists for the requested backtest range.");
            }

            await PersistSimulationResultsAsync(context, cancellationToken);

            var result = _metricsCalculator.Calculate(context, run.Id);
            var strategyResults = _metricsCalculator.CalculateStrategyBreakdown(context, run.Id);
            var symbolResults = _metricsCalculator.CalculateSymbolBreakdown(context, run.Id);

            await _backtestResultRepository.AddAsync(result, cancellationToken);
            await _strategyResultRepository.AddRangeAsync(strategyResults, cancellationToken);
            await _symbolResultRepository.AddRangeAsync(symbolResults, cancellationToken);
            await _equityPointRepository.AddRangeAsync(context.EquityPoints, cancellationToken);
            await _backtestResultRepository.SaveChangesAsync(cancellationToken);

            run.Status = BacktestRunStatus.Completed;
            run.FinalBalance = context.Balance;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            var effectiveMin = TradingPipelineConfidence.ResolveEffectiveMinimum(settings.MinConfidenceScore, riskRules);
            run.SettingsJson = MergeSettingsWithDiagnostics(
                run.SettingsJson,
                context,
                lastCandleCount,
                lastIndicatorCount,
                effectiveMin,
                request.UseAiScoring);
            await _backtestRunRepository.UpdateAsync(run, cancellationToken);
            await _backtestRunRepository.SaveChangesAsync(cancellationToken);

            session.Status = TradingSessionStatus.Completed;
            session.FinalBalance = context.Balance;
            session.StoppedAtUtc = DateTime.UtcNow;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _sessionRepository.UpdateAsync(session, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "BACKTEST_COMPLETED",
                nameof(BacktestRun),
                entityId: run.Id,
                userId: _currentUserService.UserId,
                newValueJson: JsonSerializer.Serialize(new { result.NetPnl, result.TotalTrades, result.WinRatePercent }, JsonOptions),
                cancellationToken: cancellationToken);

            await TryRecordSummaryAsync(run, request, settings, result, context, startedAtUtc, cancellationToken);

            return ServiceResult<RunBacktestResponse>.Ok(new RunBacktestResponse
            {
                BacktestRunId = run.Id,
                Status = BacktestRunStatus.Completed.ToString(),
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = run.CompletedAtUtc,
                Summary = MapSummary(result, context)
            });
        }
        catch (Exception ex)
        {
            run.Status = BacktestRunStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await _backtestRunRepository.UpdateAsync(run, cancellationToken);
            await _backtestRunRepository.SaveChangesAsync(cancellationToken);

            session.Status = TradingSessionStatus.Failed;
            session.StoppedAtUtc = DateTime.UtcNow;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _sessionRepository.UpdateAsync(session, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "BACKTEST_FAILED",
                nameof(BacktestRun),
                entityId: run.Id,
                userId: _currentUserService.UserId,
                newValueJson: JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions),
                cancellationToken: cancellationToken);

            return ServiceResult<RunBacktestResponse>.Fail(ex.Message);
        }
        finally
        {
            if (request.BenchmarkRunItemId is long benchmarkRunItemId)
            {
                _progressStore.Clear(benchmarkRunItemId);
            }
        }
    }

    private async Task<long> ResolveStartedByUserIdAsync(
        RunBacktestRequest request,
        CancellationToken cancellationToken)
    {
        var candidateId = request.RequestedByUserId ?? _currentUserService.UserId;
        if (candidateId.HasValue && candidateId.Value > 0)
        {
            if (_userRepository is null)
            {
                return candidateId.Value;
            }

            var user = await _userRepository.GetByIdAsync(candidateId.Value, cancellationToken);
            if (user is not null)
            {
                return user.Id;
            }
        }

        if (_userRepository is not null)
        {
            var adminUser = await _userRepository.GetByEmailAsync("admin@momoquant.local", cancellationToken);
            if (adminUser is not null)
            {
                return adminUser.Id;
            }

            var page = await _userRepository.GetPagedAsync(new MomoQuant.Shared.Contracts.PagedRequest
            {
                Page = 1,
                PageSize = 1
            }, cancellationToken);
            if (page.Items.Count > 0)
            {
                return page.Items[0].Id;
            }
        }

        return 0;
    }

    private async Task PersistSimulationResultsAsync(BacktestContext context, CancellationToken cancellationToken)
    {
        if (context.Signals.Count > 0)
        {
            await _signalRepository.AddRangeAsync(context.Signals, cancellationToken);
            await _signalRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var pair in context.SignalAiDecisions)
        {
            pair.Value.SignalId = pair.Key.Id;
        }

        foreach (var pair in context.SignalRiskDecisions)
        {
            pair.Value.SignalId = pair.Key.Id;
        }

        if (context.AiDecisions.Count > 0)
        {
            foreach (var decision in context.AiDecisions)
            {
                await _aiDecisionRepository.AddAsync(decision, cancellationToken);
            }

            await _aiDecisionRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var pair in context.SignalRiskDecisions)
        {
            if (context.SignalAiDecisions.TryGetValue(pair.Key, out var aiDecision) && aiDecision.Id > 0)
            {
                pair.Value.AiDecisionId = aiDecision.Id;
            }
            else
            {
                pair.Value.AiDecisionId = null;
            }
        }

        if (context.RiskDecisions.Count > 0)
        {
            foreach (var decision in context.RiskDecisions)
            {
                if (decision.AiDecisionId <= 0)
                {
                    decision.AiDecisionId = null;
                }
            }

            foreach (var decision in context.RiskDecisions)
            {
                await _riskDecisionRepository.AddAsync(decision, cancellationToken);
            }

            await _riskDecisionRepository.SaveChangesAsync(cancellationToken);
        }

        if (context.Orders.Count > 0)
        {
            await _orderRepository.AddRangeAsync(context.Orders, cancellationToken);
            await _orderRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var (order, fill) in context.OrderFillLinks)
        {
            fill.OrderId = order.Id;
        }

        if (context.OrderFills.Count > 0)
        {
            await _orderFillRepository.AddRangeAsync(context.OrderFills, cancellationToken);
            await _orderFillRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var trade in context.Trades)
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

        if (context.Trades.Count > 0)
        {
            await _tradeRepository.AddRangeAsync(context.Trades, cancellationToken);
            await _tradeRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var trade in context.Trades)
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

        if (context.Trades.Count > 0)
        {
            await _orderRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var (missedOrder, signal) in context.MissedOrderLinks)
        {
            missedOrder.SignalId = signal.Id;
        }

        if (context.MissedOrderLinks.Count > 0)
        {
            await _missedOrderRepository.AddRangeAsync(context.MissedOrderLinks.Select(link => link.MissedOrder).ToList(), cancellationToken);
            await _missedOrderRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private static string MergeSettingsWithDiagnostics(
        string settingsJson,
        BacktestContext context,
        int candleCount,
        int indicatorSnapshotCount,
        decimal effectiveMinConfidenceScore,
        bool aiEnabled)
    {
        var root = string.IsNullOrWhiteSpace(settingsJson)
            ? new JsonObject()
            : JsonNode.Parse(settingsJson)?.AsObject() ?? new JsonObject();

        root["pipelineDiagnostics"] = JsonNode.Parse(
            TradingPipelineDiagnosticsBuilder.SerializeSnapshot(
                context,
                candleCount,
                indicatorSnapshotCount,
                effectiveMinConfidenceScore,
                aiEnabled));

        return root.ToJsonString(JsonOptions);
    }

    private async Task<ServiceResult<RunBacktestSettings>> ValidateRequestAsync(
        RunBacktestRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<RunBacktestSettings>.Fail("Name is required.", "name");
        }

        if (request.InitialBalance <= 0)
        {
            return ServiceResult<RunBacktestSettings>.Fail("Initial balance must be greater than zero.", "initialBalance");
        }

        if (request.FromUtc >= request.ToUtc)
        {
            return ServiceResult<RunBacktestSettings>.Fail("fromUtc must be earlier than toUtc.", "fromUtc");
        }

        if (request.SymbolIds.Count == 0)
        {
            return ServiceResult<RunBacktestSettings>.Fail("At least one symbol is required.", "symbolIds");
        }

        if (request.Timeframes.Count == 0)
        {
            return ServiceResult<RunBacktestSettings>.Fail("At least one timeframe is required.", "timeframes");
        }

        if (request.StrategyIds.Count == 0)
        {
            return ServiceResult<RunBacktestSettings>.Fail("At least one strategy is required.", "strategyIds");
        }

        if (request.MakerFeeRate < 0 || request.TakerFeeRate < 0)
        {
            return ServiceResult<RunBacktestSettings>.Fail("Fees must be non-negative.", "makerFeeRate");
        }

        if (!TryParseExecutionMode(request.ExecutionMode, out var executionMode))
        {
            return ServiceResult<RunBacktestSettings>.Fail("Execution mode is invalid.", "executionMode");
        }

        if (!Enum.TryParse<BenchmarkEvaluationMode>(request.EvaluationMode, true, out var evaluationMode))
        {
            return ServiceResult<RunBacktestSettings>.Fail("Evaluation mode is invalid.", "evaluationMode");
        }

        if (!Enum.TryParse<SameCandleExitPolicy>(request.SameCandleExitPolicy, true, out var sameCandleExitPolicy))
        {
            return ServiceResult<RunBacktestSettings>.Fail("Same candle exit policy is invalid.", "sameCandleExitPolicy");
        }

        if (executionMode is ExecutionMode.MakerOnly or ExecutionMode.MakerThenCancel && request.OrderExpiryCandles <= 0)
        {
            return ServiceResult<RunBacktestSettings>.Fail("Order expiry candles must be greater than zero for maker modes.", "orderExpiryCandles");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<RunBacktestSettings>.Fail("Exchange was not found.", "exchangeId");
        }

        var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (riskProfile is null)
        {
            return ServiceResult<RunBacktestSettings>.Fail("Risk profile was not found.", "riskProfileId");
        }

        var timeframes = new List<Timeframe>();
        foreach (var timeframeValue in request.Timeframes)
        {
            if (!TimeframeParser.TryParse(timeframeValue, out var timeframe))
            {
                return ServiceResult<RunBacktestSettings>.Fail($"Timeframe '{timeframeValue}' is invalid.", "timeframes");
            }

            timeframes.Add(timeframe);
        }

        foreach (var symbolId in request.SymbolIds)
        {
            var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
            if (symbol is null)
            {
                return ServiceResult<RunBacktestSettings>.Fail($"Symbol {symbolId} was not found.", "symbolIds");
            }

            if (symbol.ExchangeId != request.ExchangeId)
            {
                return ServiceResult<RunBacktestSettings>.Fail($"Symbol {symbolId} does not belong to the exchange.", "symbolIds");
            }
        }

        foreach (var strategyId in request.StrategyIds)
        {
            var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
            if (strategy is null)
            {
                return ServiceResult<RunBacktestSettings>.Fail($"Strategy {strategyId} was not found.", "strategyIds");
            }

            if (!strategy.IsEnabled)
            {
                return ServiceResult<RunBacktestSettings>.Fail(
                    $"Strategy {strategy.Code.ToCode()} is disabled. Enable it before using it.",
                    "strategyIds");
            }
        }

        return ServiceResult<RunBacktestSettings>.Ok(new RunBacktestSettings
        {
            Name = request.Name,
            SymbolIds = request.SymbolIds,
            Timeframes = timeframes,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            InitialBalance = request.InitialBalance,
            StrategyIds = request.StrategyIds,
            ExecutionMode = executionMode,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            StrictAiRequired = request.StrictAiRequired,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            EvaluationMode = evaluationMode,
            EnableShadowTradeAnalysis = request.EnableShadowTradeAnalysis,
            SameCandleExitPolicy = sameCandleExitPolicy
        });
    }

    private async Task<IReadOnlyList<PreparedStrategy>> LoadPreparedStrategiesAsync(
        IReadOnlyList<long> strategyIds,
        CancellationToken cancellationToken)
    {
        var all = await _strategyRepository.GetAllAsync(cancellationToken);
        var selected = all.Where(strategy => strategyIds.Contains(strategy.Id)).ToList();
        var prepared = new List<PreparedStrategy>();

        foreach (var strategy in selected)
        {
            var plugin = _strategyRegistry.GetByCode(strategy.Code);
            if (plugin is not null)
            {
                prepared.Add(new PreparedStrategy { Strategy = strategy, Plugin = plugin });
            }
        }

        return prepared;
    }

    private static bool TryParseExecutionMode(string? value, out ExecutionMode executionMode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            executionMode = ExecutionMode.MarketFill;
            return false;
        }

        return Enum.TryParse(value, ignoreCase: true, out executionMode);
    }

    private static Timeframe ResolveHigherTimeframe(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 or Timeframe.M3 or Timeframe.M5 => Timeframe.M15,
        Timeframe.M15 or Timeframe.M30 => Timeframe.H1,
        Timeframe.H1 => Timeframe.H4,
        _ => Timeframe.D1
    };

    private async Task TryRecordSummaryAsync(
        BacktestRun run,
        RunBacktestRequest request,
        RunBacktestSettings settings,
        BacktestResult result,
        BacktestContext context,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_summaryService is null)
        {
            return;
        }

        try
        {
            var symbolNames = new List<string>();
            foreach (var symbolId in request.SymbolIds)
            {
                var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
                if (symbol is not null)
                {
                    symbolNames.Add(symbol.SymbolName);
                }
            }

            var strategyNames = new List<string>();
            foreach (var strategyId in request.StrategyIds)
            {
                var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
                if (strategy is not null)
                {
                    strategyNames.Add(strategy.Name);
                }
            }

            var timeframes = settings.Timeframes
                .Select(TimeframeParser.ToApiString)
                .Distinct()
                .ToList();

            var shadowTrades = context.ShadowTrades;
            var wouldHaveWon = shadowTrades.Count(shadow => shadow.OutcomeClassification == ShadowOutcomeClassification.WouldHaveWon);
            var wouldHaveLost = shadowTrades.Count(shadow => shadow.OutcomeClassification == ShadowOutcomeClassification.WouldHaveLost);
            var shadowNetPnl = shadowTrades.Sum(shadow => shadow.ShadowNetPnl);
            var candidateSignals = context.CandidateTrades.Count > 0 ? context.CandidateTrades.Count : result.TotalSignals;

            var keyFindings = new List<string>();
            if (result.TotalTrades == 0)
            {
                keyFindings.Add("No trades were executed for this run.");
            }
            if (context.ConfidenceRejected > 0)
            {
                keyFindings.Add($"{context.ConfidenceRejected} candidate signal(s) were rejected by the confidence gate.");
            }
            if (context.RiskRejected > 0)
            {
                keyFindings.Add($"{context.RiskRejected} candidate signal(s) were rejected by the risk gate.");
            }
            if (wouldHaveWon > 0)
            {
                keyFindings.Add($"{wouldHaveWon} rejected trade(s) would have been winners (shadow analysis).");
            }

            await _summaryService.RecordAsync(new SimulationRunSummaryInput
            {
                SourceType = SimulationRunSourceType.Backtest,
                SourceId = run.Id,
                Name = request.Name,
                Status = BacktestRunStatus.Completed.ToString(),
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = run.CompletedAtUtc,
                Symbols = symbolNames,
                Strategies = strategyNames,
                Timeframes = timeframes,
                EvaluationMode = settings.EvaluationMode.ToString(),
                InitialBalance = result.InitialBalance,
                FinalBalance = result.FinalBalance,
                NetPnl = result.NetPnl,
                NetPnlPercent = result.NetPnlPercent,
                MaxDrawdown = result.MaxDrawdownPercent,
                TotalTrades = result.TotalTrades,
                WinningTrades = result.WinningTrades,
                LosingTrades = result.LosingTrades,
                WinRatePercent = result.WinRatePercent,
                CandidateSignals = candidateSignals,
                ConfidenceRejected = context.ConfidenceRejected,
                RiskRejected = context.RiskRejected,
                ExecutedTrades = result.TotalTrades,
                ShadowTrades = shadowTrades.Count,
                ShadowNetPnl = shadowNetPnl,
                RejectedWouldHaveWon = wouldHaveWon,
                RejectedWouldHaveLost = wouldHaveLost,
                KeyFindings = keyFindings,
                Warnings = []
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record backtest summary for BacktestRunId={BacktestRunId}.", run.Id);
        }
    }

    private async Task<ServiceResult<bool>> EnsureBacktestCoverageAsync(
        RunBacktestRequest request,
        RunBacktestSettings settings,
        CancellationToken cancellationToken)
    {
        if (_coverageService is null)
        {
            return ServiceResult<bool>.Ok(true);
        }

        var requiredTimeframes = settings.Timeframes
            .Select(TimeframeParser.ToApiString)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_requirementService is not null)
        {
            foreach (var strategyId in request.StrategyIds)
            {
                var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
                if (strategy is null)
                {
                    continue;
                }

                var requirement = await _requirementService.GetByStrategyIdAsync(strategy.Id, cancellationToken);
                foreach (var timeframe in requirement.Data?.RequiredDataTimeframes ?? [])
                {
                    if (TimeframeNormalizer.TryNormalize(timeframe, out var canonical))
                    {
                        requiredTimeframes.Add(canonical);
                    }
                }
            }
        }

        foreach (var symbolId in request.SymbolIds)
        {
            var coverage = await _coverageService.EnsureTimeframesCoverageAsync(
                request.ExchangeId,
                symbolId,
                requiredTimeframes,
                request.FromUtc,
                request.ToUtc,
                warmupCandles: 600,
                allowImport: true,
                cancellationToken);

            if (!coverage.Succeeded)
            {
                return ServiceResult<bool>.Fail(coverage.ErrorMessage ?? "Candle coverage check failed.", coverage.ErrorField);
            }
        }

        return ServiceResult<bool>.Ok(true);
    }

    private static BacktestSummaryDto MapSummary(BacktestResult result, BacktestContext? context = null) => new()
    {
        InitialBalance = result.InitialBalance,
        FinalBalance = result.FinalBalance,
        NetPnl = result.NetPnl,
        NetPnlPercent = result.NetPnlPercent,
        MaxDrawdownPercent = result.MaxDrawdownPercent,
        TotalTrades = result.TotalTrades,
        WinningTrades = result.WinningTrades,
        LosingTrades = result.LosingTrades,
        WinRatePercent = result.WinRatePercent,
        ProfitFactor = result.ProfitFactor,
        AverageWin = result.AverageWin,
        AverageLoss = result.AverageLoss,
        LargestLoss = result.LargestLoss,
        AverageRewardRisk = result.AverageRewardRisk,
        TotalFees = result.TotalFees,
        TotalSignals = result.TotalSignals,
        ApprovedSignals = result.ApprovedSignals,
        FilledOrders = result.FilledOrders,
        MissedOrders = result.MissedOrders,
        RejectedSignals = result.RejectedSignals,
        ConfidenceRejectedSignals = context?.ConfidenceRejected ?? 0,
        RiskRejectedSignals = context?.RiskRejected ?? 0,
        RejectedByBothSignals = context?.RejectedByBoth ?? 0
    };
}
