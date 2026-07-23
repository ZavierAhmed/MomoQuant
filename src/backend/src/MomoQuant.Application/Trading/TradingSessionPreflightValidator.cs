using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Trading;

public sealed class TradingSessionPreflightRequest
{
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public required long RiskProfileId { get; init; }
    public decimal SessionMinConfidenceScore { get; init; }
    public bool UseAiScoring { get; init; }
    public bool StrictAiRequired { get; init; }
    public bool RunAnyway { get; init; }
    public bool AutoImportCandles { get; init; } = true;
}

public interface ITradingSessionPreflightValidator
{
    Task<ServiceResult<TradingSessionPreflightResult>> ValidateAsync(
        TradingSessionPreflightRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TradingSessionPreflightResult
{
    public int CandleCount { get; init; }
    public int IndicatorSnapshotCount { get; init; }
    public decimal EffectiveMinConfidenceScore { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class TradingSessionPreflightValidator : ITradingSessionPreflightValidator
{
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly IAiIntegrationService _aiIntegrationService;
    private readonly IMarketDataCoverageService _coverageService;

    public TradingSessionPreflightValidator(
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        IStrategyRepository strategyRepository,
        IRiskProfileRepository riskProfileRepository,
        IRiskRuleRepository riskRuleRepository,
        IAiIntegrationService aiIntegrationService,
        IMarketDataCoverageService coverageService)
    {
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _strategyRepository = strategyRepository;
        _riskProfileRepository = riskProfileRepository;
        _riskRuleRepository = riskRuleRepository;
        _aiIntegrationService = aiIntegrationService;
        _coverageService = coverageService;
    }

    public async Task<ServiceResult<TradingSessionPreflightResult>> ValidateAsync(
        TradingSessionPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        var candleTimes = await _candleRepository.GetOpenTimesInRangeAsync(
            request.ExchangeId,
            request.SymbolId,
            request.Timeframe,
            request.FromUtc,
            request.ToUtc,
            cancellationToken);
        var candleCount = candleTimes.Count;

        if (candleCount == 0 && request.AutoImportCandles && request.StrategyIds.Count > 0)
        {
            var primaryStrategy = await _strategyRepository.GetByIdAsync(request.StrategyIds[0], cancellationToken);
            if (primaryStrategy is not null)
            {
                var coverage = await _coverageService.EnsureCoverageAsync(
                    request.ExchangeId,
                    request.SymbolId,
                    primaryStrategy.Code.ToCode(),
                    TimeframeParser.ToApiString(request.Timeframe),
                    request.FromUtc,
                    request.ToUtc,
                    allowImport: true,
                    cancellationToken);

                if (coverage.Succeeded)
                {
                    if (coverage.Data?.Any(item => item.ImportedDuringRun) == true)
                    {
                        warnings.Add("Missing candles were imported automatically before this run.");
                    }

                    candleTimes = await _candleRepository.GetOpenTimesInRangeAsync(
                        request.ExchangeId,
                        request.SymbolId,
                        request.Timeframe,
                        request.FromUtc,
                        request.ToUtc,
                        cancellationToken);
                    candleCount = candleTimes.Count;
                }
            }
        }

        if (candleCount == 0 && !request.RunAnyway)
        {
            return ServiceResult<TradingSessionPreflightResult>.Fail(
                "No candles exist for the selected symbol, timeframe, and date range.",
                "candles");
        }

        var candles = await _candleRepository.GetCandlesChronologicalAsync(
            request.SymbolId,
            request.Timeframe,
            request.FromUtc,
            request.ToUtc,
            warmUpCount: 0,
            cancellationToken);
        var candleIds = candles.Select(candle => candle.Id).ToList();
        var snapshots = candleIds.Count > 0
            ? await _indicatorSnapshotRepository.GetByCandleIdsAsync(
                request.SymbolId,
                request.Timeframe,
                candleIds,
                cancellationToken)
            : new Dictionary<long, Domain.Indicators.IndicatorSnapshot>();
        var indicatorCount = snapshots.Count;

        if (indicatorCount == 0 && !request.RunAnyway)
        {
            return ServiceResult<TradingSessionPreflightResult>.Fail(
                "Indicator snapshots are missing for this range. Recalculate indicators before running this session.",
                "indicators");
        }

        if (indicatorCount == 0)
        {
            warnings.Add("Indicator snapshots are missing for this range. Recalculate indicators before running this session.");
        }

        var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (riskProfile is null)
        {
            return ServiceResult<TradingSessionPreflightResult>.Fail("Risk profile was not found.", "riskProfileId");
        }

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(request.RiskProfileId, cancellationToken);
        if (riskRules.Count == 0)
        {
            return ServiceResult<TradingSessionPreflightResult>.Fail("Risk profile has no configured rules.", "riskProfileId");
        }

        foreach (var strategyId in request.StrategyIds)
        {
            var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
            if (strategy is null)
            {
                return ServiceResult<TradingSessionPreflightResult>.Fail($"Strategy {strategyId} was not found.", "strategyIds");
            }

            if (!strategy.IsEnabled)
            {
                return ServiceResult<TradingSessionPreflightResult>.Fail(
                    $"Strategy {strategy.Code.ToCode()} is disabled. Enable it before using it.",
                    "strategyIds");
            }
        }

        if (request.UseAiScoring)
        {
            var health = await _aiIntegrationService.GetHealthAsync(cancellationToken);
            if (!health.Succeeded)
            {
                if (request.StrictAiRequired)
                {
                    return ServiceResult<TradingSessionPreflightResult>.Fail(
                        "AI service is unavailable and strict AI is required.",
                        "strictAiRequired");
                }

                warnings.Add("AI service unavailable. AI scoring skipped; strategy confidence will be used.");
            }
            else
            {
                warnings.Add("AI scoring is enabled. Confidence will combine strategy (70%) and AI (30%) scores.");
            }
        }

        var effectiveMin = TradingPipelineConfidence.ResolveEffectiveMinimum(request.SessionMinConfidenceScore, riskRules);

        return ServiceResult<TradingSessionPreflightResult>.Ok(new TradingSessionPreflightResult
        {
            CandleCount = candleCount,
            IndicatorSnapshotCount = indicatorCount,
            EffectiveMinConfidenceScore = effectiveMin,
            Warnings = warnings
        });
    }
}
