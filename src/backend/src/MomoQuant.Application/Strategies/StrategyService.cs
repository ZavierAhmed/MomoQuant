using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public interface IStrategyService
{
    Task<ServiceResult<IReadOnlyList<StrategyDto>>> GetStrategiesAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyDetailDto>> GetStrategyByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyCatalogDetailDto>> GetStrategyByCodeAsync(string strategyCode, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyDto>> EnableStrategyAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyDto>> DisableStrategyAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<StrategyParameterDto>>> GetParametersAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<StrategyParameterDto>>> UpdateParametersAsync(
        long id,
        UpdateStrategyParametersRequest request,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyEvaluationResponse>> EvaluateAsync(
        StrategyEvaluationRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyEvaluationResponse>> EvaluateLatestAsync(
        StrategyEvaluateLatestRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyService : IStrategyService
{
    private const int RecentCandleCount = 600;

    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyParameterRepository _parameterRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IStrategyParameterProvider _parameterProvider;
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IStrategyParameterDefinitionProvider _parameterDefinitionProvider;
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public StrategyService(
        IStrategyRepository strategyRepository,
        IStrategyParameterRepository parameterRepository,
        IStrategyRegistry strategyRegistry,
        IStrategyEngine strategyEngine,
        IStrategyParameterProvider parameterProvider,
        IStrategyDataRequirementService requirementService,
        IStrategyParameterDefinitionProvider parameterDefinitionProvider,
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        ISymbolRepository symbolRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _strategyRepository = strategyRepository;
        _parameterRepository = parameterRepository;
        _strategyRegistry = strategyRegistry;
        _strategyEngine = strategyEngine;
        _parameterProvider = parameterProvider;
        _requirementService = requirementService;
        _parameterDefinitionProvider = parameterDefinitionProvider;
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _symbolRepository = symbolRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyDto>>> GetStrategiesAsync(CancellationToken cancellationToken = default)
    {
        var strategies = DeduplicateStrategies(await _strategyRepository.GetAllAsync(cancellationToken));
        var requirementsResult = await _requirementService.GetAllAsync(cancellationToken);
        var requirementsById = requirementsResult.Data?.ToDictionary(item => item.StrategyId)
            ?? new Dictionary<long, StrategyDataRequirementDto>();

        return ServiceResult<IReadOnlyList<StrategyDto>>.Ok(strategies
            .Select(strategy =>
            {
                requirementsById.TryGetValue(strategy.Id, out var requirement);
                var hasDefinitions = _parameterDefinitionProvider.GetDefinitions(strategy.Code.ToCode()).Count > 0;
                return StrategyCatalogMapper.MapToCatalogDto(strategy, requirement, hasDefinitions);
            })
            .ToList());
    }

    public async Task<ServiceResult<StrategyDetailDto>> GetStrategyByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var strategy = await _strategyRepository.GetByIdAsync(id, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<StrategyDetailDto>.Fail("Strategy was not found.");
        }

        var plugin = _strategyRegistry.GetByCode(strategy.Code);
        return ServiceResult<StrategyDetailDto>.Ok(MapToDetailDto(strategy, plugin));
    }

    public async Task<ServiceResult<StrategyCatalogDetailDto>> GetStrategyByCodeAsync(
        string strategyCode,
        CancellationToken cancellationToken = default)
    {
        StrategyCode parsedCode;
        try
        {
            parsedCode = StrategyCodeExtensions.FromCode(strategyCode);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ServiceResult<StrategyCatalogDetailDto>.Fail("Strategy was not found.");
        }

        var strategy = await _strategyRepository.GetByCodeAsync(parsedCode, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<StrategyCatalogDetailDto>.Fail("Strategy was not found.");
        }

        var requirementResult = await _requirementService.GetByStrategyIdAsync(strategy.Id, cancellationToken);
        var requirement = requirementResult.Data;
        var parameterDefinitions = _parameterDefinitionProvider.GetDefinitions(strategy.Code.ToCode());
        var plugin = _strategyRegistry.GetByCode(strategy.Code);

        return ServiceResult<StrategyCatalogDetailDto>.Ok(
            StrategyCatalogContentProvider.BuildDetail(strategy, requirement, parameterDefinitions, plugin));
    }

    public async Task<ServiceResult<StrategyDto>> EnableStrategyAsync(long id, CancellationToken cancellationToken = default)
    {
        var strategy = await LoadTrackedStrategyAsync(id, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<StrategyDto>.Fail("Strategy was not found.");
        }

        strategy.IsEnabled = true;
        strategy.UpdatedAtUtc = DateTime.UtcNow;
        await _strategyRepository.UpdateAsync(strategy, cancellationToken);
        await _strategyRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "STRATEGY_ENABLED",
            nameof(Strategy),
            entityId: strategy.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"code\":\"{strategy.Code.ToCode()}\"}}",
            cancellationToken: cancellationToken);

        return ServiceResult<StrategyDto>.Ok(StrategyCatalogMapper.MapToCatalogDto(
            strategy,
            null,
            _parameterDefinitionProvider.GetDefinitions(strategy.Code.ToCode()).Count > 0));
    }

    public async Task<ServiceResult<StrategyDto>> DisableStrategyAsync(long id, CancellationToken cancellationToken = default)
    {
        var strategy = await LoadTrackedStrategyAsync(id, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<StrategyDto>.Fail("Strategy was not found.");
        }

        strategy.IsEnabled = false;
        strategy.UpdatedAtUtc = DateTime.UtcNow;
        await _strategyRepository.UpdateAsync(strategy, cancellationToken);
        await _strategyRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "STRATEGY_DISABLED",
            nameof(Strategy),
            entityId: strategy.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"code\":\"{strategy.Code.ToCode()}\"}}",
            cancellationToken: cancellationToken);

        return ServiceResult<StrategyDto>.Ok(StrategyCatalogMapper.MapToCatalogDto(
            strategy,
            null,
            _parameterDefinitionProvider.GetDefinitions(strategy.Code.ToCode()).Count > 0));
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyParameterDto>>> GetParametersAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var strategy = await _strategyRepository.GetByIdAsync(id, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<IReadOnlyList<StrategyParameterDto>>.Fail("Strategy was not found.");
        }

        var parameters = await _parameterRepository.GetByStrategyIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<StrategyParameterDto>>.Ok(parameters.Select(MapParameterToDto).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyParameterDto>>> UpdateParametersAsync(
        long id,
        UpdateStrategyParametersRequest request,
        CancellationToken cancellationToken = default)
    {
        var strategy = await _strategyRepository.GetByIdAsync(id, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<IReadOnlyList<StrategyParameterDto>>.Fail("Strategy was not found.");
        }

        foreach (var item in request.Parameters)
        {
            if (!TimeframeParser.TryParse(item.Timeframe, out var timeframe))
            {
                return ServiceResult<IReadOnlyList<StrategyParameterDto>>.Fail(
                    $"Timeframe '{item.Timeframe}' is invalid.",
                    "timeframe");
            }

            var existing = await _parameterRepository.GetByKeyAsync(
                id,
                item.ParameterKey.Trim(),
                timeframe,
                item.SymbolId,
                cancellationToken);

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                await _parameterRepository.AddAsync(new StrategyParameter
                {
                    StrategyId = id,
                    ParameterKey = item.ParameterKey.Trim(),
                    ParameterValue = item.ParameterValue.Trim(),
                    ValueType = item.ValueType,
                    Timeframe = timeframe,
                    SymbolId = item.SymbolId,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, cancellationToken);
                continue;
            }

            existing.ParameterValue = item.ParameterValue.Trim();
            existing.ValueType = item.ValueType;
            existing.IsActive = true;
            existing.UpdatedAtUtc = now;
            await _parameterRepository.UpdateAsync(existing, cancellationToken);
        }

        await _parameterRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "STRATEGY_PARAMETERS_UPDATED",
            nameof(Strategy),
            entityId: strategy.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"code\":\"{strategy.Code.ToCode()}\",\"parameterCount\":{request.Parameters.Count}}}",
            cancellationToken: cancellationToken);

        var updated = await _parameterRepository.GetByStrategyIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<StrategyParameterDto>>.Ok(updated.Select(MapParameterToDto).ToList());
    }

    public async Task<ServiceResult<StrategyEvaluationResponse>> EvaluateAsync(
        StrategyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CandleId <= 0)
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Candle selection is required.", "candleId");
        }

        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Timeframe is invalid.", "timeframe");
        }

        if (!TryParseMarketRegime(request.MarketRegime, out var marketRegime))
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Market regime is invalid.", "marketRegime");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Symbol was not found.", "symbolId");
        }

        var candle = await _candleRepository.GetByIdAsync(request.CandleId, cancellationToken);
        if (candle is null || candle.SymbolId != symbol.Id)
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail(
                "Selected candle was not found for this symbol and timeframe.",
                "candleId");
        }

        if (candle.Timeframe != timeframe)
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail(
                "Selected candle was not found for this symbol and timeframe.",
                "candleId");
        }

        return await EvaluateForCandleAsync(
            symbol,
            timeframe,
            marketRegime,
            candle,
            request.StrategyIds,
            cancellationToken);
    }

    public async Task<ServiceResult<StrategyEvaluationResponse>> EvaluateLatestAsync(
        StrategyEvaluateLatestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Timeframe is invalid.", "timeframe");
        }

        if (!TryParseMarketRegime(request.MarketRegime, out var marketRegime))
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Market regime is invalid.", "marketRegime");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail("Symbol was not found.", "symbolId");
        }

        var recentCandles = await _candleRepository.GetCandlesAsync(
            symbol.Id,
            timeframe,
            fromUtc: null,
            toUtc: null,
            limit: RecentCandleCount,
            cancellationToken);

        if (recentCandles.Count == 0)
        {
            return ServiceResult<StrategyEvaluationResponse>.Fail(
                "No candles found for this symbol and timeframe. Import candles first from Market Watch.",
                "candleId");
        }

        var selectedCandle = recentCandles.OrderByDescending(candle => candle.OpenTimeUtc).First();

        return await EvaluateForCandleAsync(
            symbol,
            timeframe,
            marketRegime,
            selectedCandle,
            request.StrategyIds,
            cancellationToken);
    }

    private async Task<ServiceResult<StrategyEvaluationResponse>> EvaluateForCandleAsync(
        Domain.Exchanges.Symbol symbol,
        Timeframe timeframe,
        MarketRegime marketRegime,
        Domain.MarketData.Candle candle,
        IReadOnlyList<long>? strategyIds,
        CancellationToken cancellationToken)
    {
        var indicatorSnapshot = await _indicatorSnapshotRepository.GetByKeyAsync(
            symbol.Id,
            timeframe,
            candle.Id,
            cancellationToken);

        var recentCandles = await _candleRepository.GetRecentCandlesAsync(
            symbol.Id,
            timeframe,
            candle.OpenTimeUtc,
            RecentCandleCount,
            cancellationToken);

        var recentIndicatorSnapshots = await _indicatorSnapshotRepository.GetRecentForSymbolAsync(
            symbol.Id,
            timeframe,
            candle.OpenTimeUtc,
            RecentCandleCount,
            cancellationToken);

        var dbStrategies = DeduplicateStrategies(await _strategyRepository.GetAllAsync(cancellationToken));
        var selectedStrategies = (strategyIds is { Count: > 0 }
                ? dbStrategies.Where(strategy => strategyIds.Contains(strategy.Id))
                : dbStrategies.Where(strategy => strategy.IsEnabled))
            .ToList();

        var results = new List<StrategyEvaluationResult>();
        foreach (var dbStrategy in selectedStrategies)
        {
            var plugin = _strategyRegistry.GetByCode(dbStrategy.Code);
            if (plugin is null)
            {
                continue;
            }

            var parameters = await _parameterProvider.GetParametersAsync(
                dbStrategy.Id,
                timeframe,
                symbol.Id,
                cancellationToken);

            var context = new StrategyContext
            {
                SymbolId = symbol.Id,
                Symbol = symbol.SymbolName,
                ExchangeId = symbol.ExchangeId,
                Timeframe = timeframe,
                HigherTimeframe = ResolveHigherTimeframe(timeframe),
                MarketRegime = marketRegime,
                Candles = recentCandles,
                IndicatorSnapshot = indicatorSnapshot,
                RecentIndicatorSnapshots = recentIndicatorSnapshots,
                StrategyParameters = parameters,
                EvaluatedAtUtc = DateTime.UtcNow
            };

            var evaluationResults = await _strategyEngine.EvaluateAsync([plugin], context, cancellationToken);
            results.AddRange(evaluationResults);
        }

        return ServiceResult<StrategyEvaluationResponse>.Ok(new StrategyEvaluationResponse
        {
            SymbolId = symbol.Id,
            Timeframe = TimeframeParser.ToApiString(timeframe),
            CandleId = candle.Id,
            MarketRegime = marketRegime.ToString(),
            CandleOpenTimeUtc = candle.OpenTimeUtc,
            CandleCloseTimeUtc = candle.CloseTimeUtc,
            CandleClose = candle.Close,
            Results = results
        });
    }

    private Task<Strategy?> LoadTrackedStrategyAsync(long id, CancellationToken cancellationToken) =>
        _strategyRepository.GetByIdForUpdateAsync(id, cancellationToken);

    private static StrategyDto MapToDto(Strategy strategy) => new()
    {
        Id = strategy.Id,
        Code = strategy.Code.ToCode(),
        Name = strategy.Name,
        Description = strategy.Description,
        IsEnabled = strategy.IsEnabled,
        Version = strategy.Version
    };

    private static StrategyDetailDto MapToDetailDto(Strategy strategy, ITradingStrategy? plugin) => new()
    {
        Id = strategy.Id,
        Code = strategy.Code.ToCode(),
        Name = strategy.Name,
        Description = strategy.Description,
        IsEnabled = strategy.IsEnabled,
        Version = strategy.Version,
        ResearchStatus = strategy.ResearchStatus.ToString(),
        DeploymentQualificationEligible = strategy.DeploymentQualificationEligible,
        CanonicalValidationExperimentId = strategy.CanonicalValidationExperimentId,
        SupportedRegimes = plugin?.SupportedRegimes.Select(regime => regime.ToString()).ToList() ?? [],
        SupportedTimeframes = plugin?.SupportedTimeframes.Select(tf => TimeframeParser.ToApiString(tf)).ToList() ?? []
    };

    private static StrategyParameterDto MapParameterToDto(StrategyParameter parameter) => new()
    {
        Id = parameter.Id,
        ParameterKey = parameter.ParameterKey,
        ParameterValue = parameter.ParameterValue,
        ValueType = parameter.ValueType,
        Timeframe = TimeframeParser.ToApiString(parameter.Timeframe),
        SymbolId = parameter.SymbolId,
        IsActive = parameter.IsActive
    };

    private static Timeframe ResolveHigherTimeframe(Timeframe timeframe) =>
        timeframe switch
        {
            Timeframe.M3 or Timeframe.M5 => Timeframe.M15,
            Timeframe.M15 => Timeframe.H1,
            _ => Timeframe.H4
        };

    private static bool TryParseMarketRegime(string value, out MarketRegime regime) =>
        Enum.TryParse(value, ignoreCase: true, out regime);

    private static IReadOnlyList<Strategy> DeduplicateStrategies(IReadOnlyList<Strategy> strategies) =>
        strategies
            .GroupBy(strategy => strategy.Code)
            .Select(group => group
                .OrderByDescending(item => item.IsEnabled)
                .ThenBy(item => item.Name.Contains("Legacy Duplicate", StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.Id)
                .First())
            .OrderBy(item => item.Name)
            .ToList();
}
