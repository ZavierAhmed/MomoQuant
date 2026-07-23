using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Strategies;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Replay;

public interface IReplaySessionService
{
    Task<ServiceResult<ReplaySessionDto>> CreateAsync(CreateReplaySessionRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplaySessionDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<ReplaySessionDto>>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
}

public sealed class ReplaySessionService : IReplaySessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IReplaySessionRepository _replaySessionRepository;
    private readonly ITradingSessionRepository _tradingSessionRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly IReplayDataLoader _dataLoader;
    private readonly IReplayStateStore _stateStore;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly IMarketDataCoverageService? _coverageService;
    private readonly IStrategyDataRequirementService? _requirementService;

    public ReplaySessionService(
        IReplaySessionRepository replaySessionRepository,
        ITradingSessionRepository tradingSessionRepository,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        IRiskProfileRepository riskProfileRepository,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        IRiskRuleRepository riskRuleRepository,
        IReplayDataLoader dataLoader,
        IReplayStateStore stateStore,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IMarketDataCoverageService? coverageService = null,
        IStrategyDataRequirementService? requirementService = null)
    {
        _replaySessionRepository = replaySessionRepository;
        _tradingSessionRepository = tradingSessionRepository;
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _riskProfileRepository = riskProfileRepository;
        _strategyRepository = strategyRepository;
        _strategyRegistry = strategyRegistry;
        _riskRuleRepository = riskRuleRepository;
        _dataLoader = dataLoader;
        _stateStore = stateStore;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _coverageService = coverageService;
        _requirementService = requirementService;
    }

    public async Task<ServiceResult<ReplaySessionDto>> CreateAsync(
        CreateReplaySessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateRequestAsync(request, cancellationToken);
        if (!validation.Succeeded)
        {
            return ServiceResult<ReplaySessionDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        var validated = validation.Data!;
        var now = DateTime.UtcNow;

        var tradingSession = new TradingSession
        {
            Name = $"Replay: {request.Name}",
            Mode = TradingMode.Replay,
            Status = TradingSessionStatus.Created,
            ExchangeId = request.ExchangeId,
            StartedByUserId = _currentUserService.UserId ?? 0,
            StartedAtUtc = now,
            InitialBalance = request.InitialBalance,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _tradingSessionRepository.AddAsync(tradingSession, cancellationToken);
        await _tradingSessionRepository.SaveChangesAsync(cancellationToken);

        var session = new ReplaySession
        {
            Name = request.Name,
            TradingSessionId = tradingSession.Id,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = validated.Timeframe,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            InitialBalance = request.InitialBalance,
            CurrentBalance = request.InitialBalance,
            CurrentEquity = request.InitialBalance,
            RiskProfileId = request.RiskProfileId,
            ExecutionMode = validated.ExecutionMode,
            UseAiScoring = request.UseAiScoring,
            Speed = validated.Speed,
            Status = ReplaySessionStatus.Created,
            CurrentFrameIndex = -1,
            TotalFrames = validated.Dataset!.EvaluationIndices.Count,
            RequestedByUserId = _currentUserService.UserId,
            ConfigJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _replaySessionRepository.AddAsync(session, cancellationToken);
        await _replaySessionRepository.SaveChangesAsync(cancellationToken);

        var runtimeState = ReplayEngine.CreateRuntimeState(
            validated.Settings,
            session,
            validated.Dataset,
            validated.Strategies,
            validated.RiskRules,
            validated.Symbol);

        _stateStore.Set(session.Id, runtimeState);

        await _auditService.LogAsync(
            "REPLAY_SESSION_CREATED",
            nameof(ReplaySession),
            entityId: session.Id,
            userId: _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new { request.Name, request.SymbolId, request.Timeframe }, JsonOptions),
            cancellationToken: cancellationToken);

        return ServiceResult<ReplaySessionDto>.Ok(ReplayMapper.MapSession(session, validated.Symbol.SymbolName));
    }

    public async Task<ServiceResult<ReplaySessionDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var session = await _replaySessionRepository.GetByIdAsync(id, cancellationToken);
        if (session is null)
        {
            return ServiceResult<ReplaySessionDto>.Fail("Replay session was not found.");
        }

        var symbol = await _symbolRepository.GetByIdAsync(session.SymbolId, cancellationToken);
        return ServiceResult<ReplaySessionDto>.Ok(ReplayMapper.MapSession(session, symbol?.SymbolName ?? session.SymbolId.ToString()));
    }

    public async Task<ServiceResult<PagedResult<ReplaySessionDto>>> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _replaySessionRepository.GetPagedAsync(request, cancellationToken);
        var symbolIds = items.Select(item => item.SymbolId).Distinct().ToList();
        var symbols = new Dictionary<long, string>();
        foreach (var symbolId in symbolIds)
        {
            var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
            symbols[symbolId] = symbol?.SymbolName ?? symbolId.ToString();
        }

        var dtos = items
            .Select(item => ReplayMapper.MapSession(item, symbols[item.SymbolId]))
            .ToList();

        return ServiceResult<PagedResult<ReplaySessionDto>>.Ok(new PagedResult<ReplaySessionDto>
        {
            Items = dtos,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    private async Task<ServiceResult<ValidatedReplayRequest>> ValidateRequestAsync(
        CreateReplaySessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Name is required.", "name");
        }

        if (request.InitialBalance <= 0)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Initial balance must be greater than zero.", "initialBalance");
        }

        if (request.FromUtc >= request.ToUtc)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("fromUtc must be earlier than toUtc.", "fromUtc");
        }

        var candleOnlyReplay = request.StrategyIds.Count == 0;

        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Timeframe is invalid.", "timeframe");
        }

        if (!TryParseExecutionMode(request.ExecutionMode, out var executionMode))
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Execution mode is invalid.", "executionMode");
        }

        if (!ReplayMapper.TryParseSpeed(request.Speed, out var speed))
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Replay speed is invalid.", "speed");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Exchange was not found.", "exchangeId");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Symbol was not found.", "symbolId");
        }

        if (symbol.ExchangeId != request.ExchangeId)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Symbol does not belong to the exchange.", "symbolId");
        }

        var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (riskProfile is null)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Risk profile was not found.", "riskProfileId");
        }

        if (!candleOnlyReplay)
        {
            foreach (var strategyId in request.StrategyIds)
            {
                var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
                if (strategy is null)
                {
                    return ServiceResult<ValidatedReplayRequest>.Fail($"Strategy {strategyId} was not found.", "strategyIds");
                }

                if (!strategy.IsEnabled)
                {
                    return ServiceResult<ValidatedReplayRequest>.Fail(
                        $"Strategy {strategy.Code.ToCode()} is disabled. Enable it before using it.",
                        "strategyIds");
                }
            }
        }

        if (request.AutoImportMissingCandles && _coverageService is not null)
        {
            var coverageResult = await EnsureReplayCoverageAsync(
                request,
                TimeframeParser.ToApiString(timeframe),
                candleOnlyReplay,
                cancellationToken);
            if (!coverageResult.Succeeded)
            {
                return ServiceResult<ValidatedReplayRequest>.Fail(coverageResult.ErrorMessage!, coverageResult.ErrorField);
            }
        }

        var dataset = await _dataLoader.LoadAsync(
            request.ExchangeId,
            request.SymbolId,
            timeframe,
            request.FromUtc,
            request.ToUtc,
            cancellationToken);

        if (dataset is null || dataset.EvaluationIndices.Count == 0)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("No candle data exists for the requested replay range.");
        }

        if (!candleOnlyReplay && dataset.IndicatorSnapshots.Count == 0)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("Indicator snapshots are required for replay. Recalculate indicators first.");
        }

        var strategies = candleOnlyReplay
            ? Array.Empty<PreparedStrategy>()
            : await LoadPreparedStrategiesAsync(request.StrategyIds, cancellationToken);
        if (!candleOnlyReplay && strategies.Count == 0)
        {
            return ServiceResult<ValidatedReplayRequest>.Fail("No enabled strategy plugins were found for the requested strategies.", "strategyIds");
        }

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(request.RiskProfileId, cancellationToken);
        var settings = new ReplaySessionSettings
        {
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            StrictAiRequired = request.StrictAiRequired,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            ExecutionMode = executionMode,
            StrategyIds = request.StrategyIds
        };

        return ServiceResult<ValidatedReplayRequest>.Ok(new ValidatedReplayRequest
        {
            Timeframe = timeframe,
            ExecutionMode = executionMode,
            Speed = speed,
            Dataset = dataset,
            Symbol = symbol,
            Strategies = strategies,
            RiskRules = riskRules,
            Settings = settings
        });
    }

    private async Task<ServiceResult<bool>> EnsureReplayCoverageAsync(
        CreateReplaySessionRequest request,
        string executionTimeframe,
        bool candleOnlyReplay,
        CancellationToken cancellationToken)
    {
        if (_coverageService is null)
        {
            return ServiceResult<bool>.Ok(true);
        }

        var requiredTimeframes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { executionTimeframe };

        if (!candleOnlyReplay && _requirementService is not null)
        {
            foreach (var strategyId in request.StrategyIds)
            {
                var requirement = await _requirementService.GetByStrategyIdAsync(strategyId, cancellationToken);
                foreach (var timeframe in requirement.Data?.RequiredDataTimeframes ?? [])
                {
                    if (TimeframeNormalizer.TryNormalize(timeframe, out var canonical))
                    {
                        requiredTimeframes.Add(canonical);
                    }
                }
            }
        }

        var coverage = await _coverageService.EnsureTimeframesCoverageAsync(
            request.ExchangeId,
            request.SymbolId,
            requiredTimeframes,
            request.FromUtc,
            request.ToUtc,
            warmupCandles: 600,
            allowImport: true,
            cancellationToken);

        return coverage.Succeeded
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Fail(coverage.ErrorMessage ?? "Candle coverage check failed.", coverage.ErrorField);
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

    private sealed class ValidatedReplayRequest
    {
        public required Timeframe Timeframe { get; init; }
        public required ExecutionMode ExecutionMode { get; init; }
        public required ReplaySpeed Speed { get; init; }
        public required BacktestDataset Dataset { get; init; }
        public required Domain.Exchanges.Symbol Symbol { get; init; }
        public required IReadOnlyList<PreparedStrategy> Strategies { get; init; }
        public required IReadOnlyList<Domain.Risk.RiskRule> RiskRules { get; init; }
        public required ReplaySessionSettings Settings { get; init; }
    }
}
