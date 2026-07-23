using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Strategies;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.PaperTrading;

public interface IPaperSessionService
{
    Task<ServiceResult<PaperSessionDto>> CreateAsync(CreatePaperSessionRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperSessionDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<PaperSessionDto>>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
}

public sealed class PaperSessionService : IPaperSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPaperTradingSessionRepository _sessionRepository;
    private readonly IPaperAccountRepository _accountRepository;
    private readonly ITradingSessionRepository _tradingSessionRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IStrategyParameterSetRepository _parameterSetRepository;
    private readonly IStrategyParameterProvider _parameterProvider;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly IBacktestDataLoader _dataLoader;
    private readonly IPaperStateStore _stateStore;
    private readonly ILiveMarketConnectionManager _liveMarketConnectionManager;
    private readonly IMarketSituationService _marketSituationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public PaperSessionService(
        IPaperTradingSessionRepository sessionRepository,
        IPaperAccountRepository accountRepository,
        ITradingSessionRepository tradingSessionRepository,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        IRiskProfileRepository riskProfileRepository,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        IStrategyParameterSetRepository parameterSetRepository,
        IStrategyParameterProvider parameterProvider,
        IRiskRuleRepository riskRuleRepository,
        IBacktestDataLoader dataLoader,
        IPaperStateStore stateStore,
        ILiveMarketConnectionManager liveMarketConnectionManager,
        IMarketSituationService marketSituationService,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _sessionRepository = sessionRepository;
        _accountRepository = accountRepository;
        _tradingSessionRepository = tradingSessionRepository;
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _riskProfileRepository = riskProfileRepository;
        _strategyRepository = strategyRepository;
        _strategyRegistry = strategyRegistry;
        _parameterSetRepository = parameterSetRepository;
        _parameterProvider = parameterProvider;
        _riskRuleRepository = riskRuleRepository;
        _dataLoader = dataLoader;
        _stateStore = stateStore;
        _liveMarketConnectionManager = liveMarketConnectionManager;
        _marketSituationService = marketSituationService;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<PaperSessionDto>> CreateAsync(
        CreatePaperSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateRequestAsync(request, cancellationToken);
        if (!validation.Succeeded)
        {
            return ServiceResult<PaperSessionDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        var validated = validation.Data!;
        var account = validated.Account!;
        var now = DateTime.UtcNow;

        var tradingSession = new TradingSession
        {
            Name = $"Paper: {request.Name}",
            Mode = TradingMode.Paper,
            Status = TradingSessionStatus.Created,
            ExchangeId = request.ExchangeId,
            StartedByUserId = _currentUserService.UserId ?? 0,
            InitialBalance = account.CurrentBalance,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _tradingSessionRepository.AddAsync(tradingSession, cancellationToken);
        await _tradingSessionRepository.SaveChangesAsync(cancellationToken);

        var session = new PaperTradingSession
        {
            Name = request.Name.Trim(),
            PaperAccountId = request.PaperAccountId,
            TradingSessionId = tradingSession.Id,
            Status = PaperSessionStatus.Created,
            Mode = validated.Mode,
            ExchangeId = request.ExchangeId,
            RiskProfileId = request.RiskProfileId,
            ExecutionMode = validated.ExecutionMode,
            UseAiScoring = request.UseAiScoring,
            MinConfidenceScore = request.MinConfidenceScore,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            TotalCandles = validated.Mode == PaperTradingMode.LivePaper ? 0 : validated.Dataset!.EvaluationIndices.Count,
            RequestedByUserId = _currentUserService.UserId,
            ConfigJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _sessionRepository.AddAsync(session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);

        var runtimeState = PaperMapper.CreateRuntimeState(
            validated.Settings!,
            session,
            account,
            validated.Dataset!,
            validated.Strategies!,
            validated.RiskRules!,
            validated.Symbol!,
            validated.FrozenStrategyParameters);

        _stateStore.Set(session.Id, runtimeState);

        await _auditService.LogAsync(
            "PAPER_SESSION_CREATED",
            nameof(PaperTradingSession),
            session.Id,
            _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new { request.Name, request.PaperAccountId, request.Mode }, JsonOptions),
            cancellationToken: cancellationToken);

        return ServiceResult<PaperSessionDto>.Ok(PaperMapper.MapSession(session));
    }

    public async Task<ServiceResult<PaperSessionDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(id, cancellationToken);
        return session is null
            ? ServiceResult<PaperSessionDto>.Fail("Paper session was not found.")
            : ServiceResult<PaperSessionDto>.Ok(PaperMapper.MapSession(session));
    }

    public async Task<ServiceResult<PagedResult<PaperSessionDto>>> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _sessionRepository.GetPagedAsync(request, cancellationToken);
        return ServiceResult<PagedResult<PaperSessionDto>>.Ok(new PagedResult<PaperSessionDto>
        {
            Items = items.Select(PaperMapper.MapSession).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    private async Task<ServiceResult<ValidatedPaperRequest>> ValidateRequestAsync(
        CreatePaperSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Name is required.", "name");
        }

        if (!PaperMapper.TryParseMode(request.Mode, out var mode))
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Paper trading mode is invalid.", "mode");
        }

        if (mode == PaperTradingMode.LivePaper)
        {
            if (!_liveMarketConnectionManager.IsAvailable)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail(
                    "Live market data provider is unavailable. LivePaper cannot start.",
                    "mode");
            }
        }
        else if (mode != PaperTradingMode.HistoricalPaper)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Paper trading mode is invalid.", "mode");
        }

        if (request.SymbolIds is null || request.SymbolIds.Count == 0)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("At least one symbol is required.", "symbolIds");
        }

        if (request.Timeframes is null || request.Timeframes.Count == 0)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("At least one timeframe is required.", "timeframes");
        }

        if (request.StrategyIds is null || request.StrategyIds.Count == 0)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("At least one strategy is required.", "strategyIds");
        }

        if (!PaperMapper.TryParseExecutionMode(request.ExecutionMode, out var executionMode))
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Execution mode is invalid.", "executionMode");
        }

        if (request.MakerFeeRate < 0 || request.TakerFeeRate < 0)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Fee rates must be non-negative.", "fees");
        }

        if (executionMode != ExecutionMode.MarketFill && request.OrderExpiryCandles <= 0)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Order expiry candles must be greater than zero for maker modes.", "orderExpiryCandles");
        }

        if (mode == PaperTradingMode.HistoricalPaper)
        {
            if (request.FromUtc is null || request.ToUtc is null)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail("HistoricalPaper requires FromUtc and ToUtc.", "fromUtc");
            }

            if (request.FromUtc >= request.ToUtc)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail("FromUtc must be before ToUtc.", "fromUtc");
            }
        }

        var account = await _accountRepository.GetByIdAsync(request.PaperAccountId, cancellationToken);
        if (account is null)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Paper account was not found.", "paperAccountId");
        }

        if (!account.IsActive)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Paper account is not active.", "paperAccountId");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Exchange was not found.", "exchangeId");
        }

        var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (riskProfile is null)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Risk profile was not found.", "riskProfileId");
        }

        var symbolId = request.SymbolIds[0];
        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Symbol was not found.", "symbolIds");
        }

        foreach (var id in request.SymbolIds.Skip(1))
        {
            var extraSymbol = await _symbolRepository.GetByIdAsync(id, cancellationToken);
            if (extraSymbol is null)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail($"Symbol {id} was not found.", "symbolIds");
            }
        }

        if (!TimeframeParser.TryParse(request.Timeframes[0], out var timeframe))
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("Timeframe is invalid.", "timeframes");
        }

        foreach (var tf in request.Timeframes.Skip(1))
        {
            if (!TimeframeParser.TryParse(tf, out _))
            {
                return ServiceResult<ValidatedPaperRequest>.Fail($"Timeframe {tf} is invalid.", "timeframes");
            }
        }

        foreach (var strategyId in request.StrategyIds)
        {
            var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
            if (strategy is null)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail($"Strategy {strategyId} was not found.", "strategyIds");
            }

            if (!strategy.IsEnabled)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail(
                    $"Strategy {strategy.Code.ToCode()} is disabled. Enable it before using it.",
                    "strategyIds");
            }
        }

        var strategies = await LoadPreparedStrategiesAsync(request.StrategyIds, cancellationToken);
        if (strategies.Count != request.StrategyIds.Count)
        {
            return ServiceResult<ValidatedPaperRequest>.Fail("One or more strategies were not found.", "strategyIds");
        }

        BacktestDataset? dataset = null;
        if (mode == PaperTradingMode.HistoricalPaper)
        {
            dataset = await _dataLoader.LoadSymbolTimeframeAsync(
                request.ExchangeId,
                symbolId,
                timeframe,
                request.FromUtc!.Value,
                request.ToUtc!.Value,
                warmUpCount: 600,
                cancellationToken);

            if (dataset is null || dataset.EvaluationIndices.Count == 0)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail("No candles exist for the selected date range.", "fromUtc");
            }
        }
        else if (mode == PaperTradingMode.LivePaper)
        {
            var toUtc = DateTime.UtcNow;
            var fromUtc = toUtc.AddDays(-30);
            dataset = await _dataLoader.LoadSymbolTimeframeAsync(
                request.ExchangeId,
                symbolId,
                timeframe,
                fromUtc,
                toUtc,
                warmUpCount: 600,
                cancellationToken);

            dataset ??= new BacktestDataset
            {
                SymbolId = symbolId,
                SymbolName = symbol.SymbolName,
                Timeframe = timeframe,
                Candles = [],
                IndicatorSnapshots = new Dictionary<long, Domain.Indicators.IndicatorSnapshot>(),
                EvaluationIndices = []
            };

            if (!request.AllowAbnormalMarketPaperTrading)
            {
                var situation = await _marketSituationService.GetCurrentAsync(
                    request.ExchangeId,
                    symbolId,
                    request.Timeframes[0],
                    cancellationToken);

                if (situation.Succeeded
                    && situation.Data is not null
                    && string.Equals(situation.Data.MarketRegime, nameof(MarketRegime.Abnormal), StringComparison.OrdinalIgnoreCase))
                {
                    return ServiceResult<ValidatedPaperRequest>.Fail(
                        "Market appears abnormal. LivePaper trading is not recommended. Set allowAbnormalMarketPaperTrading to true to proceed.",
                        "marketRegime");
                }
            }
        }

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(request.RiskProfileId, cancellationToken);
        var timeframes = request.Timeframes
            .Select(tf => TimeframeParser.TryParse(tf, out var parsed) ? parsed : default)
            .Where(tf => tf != default)
            .ToList();

        var settings = new PaperSessionSettings
        {
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            StrictAiRequired = request.StrictAiRequired,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            ExecutionMode = executionMode,
            StrategyIds = request.StrategyIds,
            SymbolIds = request.SymbolIds,
            Timeframes = timeframes,
            ParameterSetId = request.ParameterSetId
        };

        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>>? frozenStrategyParameters = null;
        if (request.ParameterSetId is long parameterSetId)
        {
            var parameterSet = await _parameterSetRepository.GetByIdAsync(parameterSetId, cancellationToken);
            if (parameterSet is null)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail("Parameter set was not found.", "parameterSetId");
            }

            if (!parameterSet.IsApproved)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail(
                    "Parameter set must be approved before use in paper trading.",
                    "parameterSetId");
            }

            if (request.StrategyIds.Count != 1)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail(
                    "Frozen parameter sets require exactly one strategy.",
                    "parameterSetId");
            }

            var selectedStrategy = await _strategyRepository.GetByIdAsync(request.StrategyIds[0], cancellationToken);
            if (selectedStrategy is null)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail("Strategy was not found.", "strategyIds");
            }

            if (!string.Equals(parameterSet.StrategyCode, selectedStrategy.Code.ToCode(), StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<ValidatedPaperRequest>.Fail(
                    "Parameter set strategy does not match selected strategy.",
                    "parameterSetId");
            }

            var frozenParameters = await _parameterProvider.GetParametersFromSetAsync(parameterSetId, cancellationToken);
            if (frozenParameters.Count == 0)
            {
                return ServiceResult<ValidatedPaperRequest>.Fail(
                    "Approved parameter set could not be loaded.",
                    "parameterSetId");
            }

            frozenStrategyParameters = new Dictionary<long, IReadOnlyDictionary<string, string>>
            {
                [selectedStrategy.Id] = frozenParameters
            };
        }

        return ServiceResult<ValidatedPaperRequest>.Ok(new ValidatedPaperRequest
        {
            Mode = mode,
            ExecutionMode = executionMode,
            Account = account,
            Symbol = symbol,
            Dataset = dataset,
            Strategies = strategies,
            RiskRules = riskRules,
            Settings = settings,
            FrozenStrategyParameters = frozenStrategyParameters
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

    private sealed class ValidatedPaperRequest
    {
        public PaperTradingMode Mode { get; init; }
        public ExecutionMode ExecutionMode { get; init; }
        public PaperAccount? Account { get; init; }
        public Domain.Exchanges.Symbol? Symbol { get; init; }
        public BacktestDataset? Dataset { get; init; }
        public IReadOnlyList<PreparedStrategy>? Strategies { get; init; }
        public IReadOnlyList<Domain.Risk.RiskRule>? RiskRules { get; init; }
        public PaperSessionSettings? Settings { get; init; }
        public IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>>? FrozenStrategyParameters { get; init; }
    }
}
