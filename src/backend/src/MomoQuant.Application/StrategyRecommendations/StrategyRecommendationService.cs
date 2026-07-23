using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.MarketSituation.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Application.StrategyRecommendations.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.StrategyRecommendations;

public interface IStrategyRecommendationService
{
    Task<ServiceResult<StrategyRecommendationResponseDto>> GetCurrentAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        string mode,
        bool showDisabled = false,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyRecommendationService : IStrategyRecommendationService
{
    private static readonly IReadOnlyDictionary<MarketRegime, StrategyCode[]> RegimeStrategyMap =
        new Dictionary<MarketRegime, StrategyCode[]>
        {
            [MarketRegime.Trending] =
            [
                StrategyCode.EmaPullback,
                StrategyCode.MacdMomentumContinuation,
                StrategyCode.SupertrendContinuation
            ],
            [MarketRegime.Ranging] =
            [
                StrategyCode.VwapMeanReversion,
                StrategyCode.LiquiditySweep,
                StrategyCode.RsiDivergenceReversal,
                StrategyCode.FourHourRangeReEntry
            ],
            [MarketRegime.Breakout] =
            [
                StrategyCode.DonchianBreakout,
                StrategyCode.BollingerSqueezeBreakout,
                StrategyCode.AtrVolatilityBreakout,
                StrategyCode.SupportResistanceBreakoutRetest,
                StrategyCode.FourHourRangeReEntry
            ],
            [MarketRegime.Reversal] =
            [
                StrategyCode.LiquiditySweep,
                StrategyCode.RsiDivergenceReversal,
                StrategyCode.VwapMeanReversion,
                StrategyCode.FourHourRangeReEntry
            ],
            [MarketRegime.HighVolatility] =
            [
                StrategyCode.LiquiditySweep,
                StrategyCode.AtrVolatilityBreakout
            ],
            [MarketRegime.LowVolatility] =
            [
                StrategyCode.BollingerSqueezeBreakout,
                StrategyCode.DonchianBreakout
            ],
            [MarketRegime.Choppy] =
            [
                StrategyCode.VwapMeanReversion
            ]
        };

    private readonly IMarketSituationService _marketSituationService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;

    public StrategyRecommendationService(
        IMarketSituationService marketSituationService,
        IStrategyRepository strategyRepository,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository)
    {
        _marketSituationService = marketSituationService;
        _strategyRepository = strategyRepository;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
    }

    public async Task<ServiceResult<StrategyRecommendationResponseDto>> GetCurrentAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        string mode,
        bool showDisabled = false,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(mode, "LivePaper", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<StrategyRecommendationResponseDto>.Fail(
                    "Live trading is disabled. Strategy recommendations are available for LivePaper only.",
                    "mode");
            }

            return ServiceResult<StrategyRecommendationResponseDto>.Fail(
                "Strategy recommendations are available for LivePaper only.",
                "mode");
        }

        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);

        var situationResult = await _marketSituationService.GetCurrentAsync(
            exchangeId,
            symbolId,
            timeframe,
            cancellationToken);

        if (!situationResult.Succeeded || situationResult.Data is null)
        {
            return ServiceResult<StrategyRecommendationResponseDto>.Ok(new StrategyRecommendationResponseDto
            {
                ExchangeId = exchangeId,
                ExchangeName = exchange?.Name ?? string.Empty,
                SymbolId = symbolId,
                Symbol = symbol?.SymbolName ?? string.Empty,
                Timeframe = timeframe,
                Mode = "LivePaper",
                MarketSituation = new MarketSituationSummaryDto
                {
                    MarketRegime = MarketRegime.Unknown.ToString(),
                    TrendDirection = TrendDirection.Unknown.ToString(),
                    VolatilityState = VolatilityState.Normal.ToString(),
                    MomentumState = MomentumState.Neutral.ToString()
                },
                RecommendedStrategies = [],
                SelectedByDefaultStrategyIds = [],
                GeneratedAtUtc = DateTime.UtcNow,
                Warning = situationResult.ErrorMessage
                    ?? "Market situation could not be detected because recent candles/indicators are unavailable."
            });
        }

        var situation = situationResult.Data;
        if (symbol is null || exchange is null)
        {
            return ServiceResult<StrategyRecommendationResponseDto>.Fail("Symbol or exchange was not found.");
        }

        if (!Enum.TryParse<MarketRegime>(situation.MarketRegime, out var regime))
        {
            regime = MarketRegime.Unknown;
        }

        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var candidates = showDisabled ? strategies : strategies.Where(strategy => strategy.IsEnabled).ToList();
        var preferredCodes = RegimeStrategyMap.TryGetValue(regime, out var mapped)
            ? mapped.ToHashSet()
            : [];

        var items = new List<StrategyRecommendationItemDto>();
        foreach (var strategy in candidates.OrderBy(strategy => strategy.Name))
        {
            var score = CalculateSuitability(strategy, regime, preferredCodes, situation);
            var recommended = score >= 60 && strategy.IsEnabled && regime is not MarketRegime.Abnormal and not MarketRegime.Unknown;
            if (regime == MarketRegime.Choppy && score < 75)
            {
                recommended = false;
            }

            items.Add(new StrategyRecommendationItemDto
            {
                StrategyId = strategy.Id,
                StrategyCode = strategy.Code.ToCode(),
                StrategyName = strategy.Name,
                SuitabilityScore = score,
                Recommended = recommended,
                IsEnabled = strategy.IsEnabled,
                Reason = BuildReason(strategy.Code, regime, score, recommended),
                Warnings = BuildWarnings(strategy.Code, regime)
            });
        }

        var selectedByDefault = items
            .Where(item => item.Recommended && item.IsEnabled)
            .OrderByDescending(item => item.SuitabilityScore)
            .Take(3)
            .Select(item => item.StrategyId)
            .ToList();

        string? warning = null;
        if (regime == MarketRegime.Unknown)
        {
            warning = "Market situation could not be detected because recent candles/indicators are unavailable.";
        }

        return ServiceResult<StrategyRecommendationResponseDto>.Ok(new StrategyRecommendationResponseDto
        {
            ExchangeId = exchangeId,
            ExchangeName = exchange.Name,
            SymbolId = symbolId,
            Symbol = symbol.SymbolName,
            Timeframe = timeframe,
            Mode = "LivePaper",
            MarketSituation = new MarketSituationSummaryDto
            {
                MarketRegime = situation.MarketRegime,
                TrendDirection = situation.TrendDirection,
                VolatilityState = situation.VolatilityState,
                MomentumState = situation.MomentumState
            },
            RecommendedStrategies = items.OrderByDescending(item => item.SuitabilityScore).ToList(),
            SelectedByDefaultStrategyIds = selectedByDefault,
            GeneratedAtUtc = DateTime.UtcNow,
            Warning = warning
        });
    }

    private static int CalculateSuitability(
        Strategy strategy,
        MarketRegime regime,
        HashSet<StrategyCode> preferredCodes,
        MarketSituation.Dtos.MarketSituationDto situation)
    {
        if (regime is MarketRegime.Abnormal or MarketRegime.Unknown)
        {
            return regime == MarketRegime.Abnormal ? 5 : 15;
        }

        if (!strategy.IsEnabled)
        {
            return 10;
        }

        if (strategy.Code == StrategyCode.FourHourRangeReEntry)
        {
            return CalculateFourHourRangeSuitability(regime, preferredCodes, situation);
        }

        var baseScore = preferredCodes.Contains(strategy.Code) ? 82 : 25;
        if (preferredCodes.Contains(strategy.Code))
        {
            return baseScore;
        }

        if (regime == MarketRegime.Choppy)
        {
            return strategy.Code == StrategyCode.VwapMeanReversion ? 45 : 15;
        }

        return baseScore;
    }

    private static int CalculateFourHourRangeSuitability(
        MarketRegime regime,
        HashSet<StrategyCode> preferredCodes,
        MarketSituation.Dtos.MarketSituationDto situation)
    {
        if (!preferredCodes.Contains(StrategyCode.FourHourRangeReEntry))
        {
            return regime == MarketRegime.LowVolatility ? 45 : 20;
        }

        var score = 80;
        if (!IsAfterNewYorkRangeClose(situation.LatestCandleTimeUtc ?? DateTime.UtcNow))
        {
            score -= 30;
        }

        if (Enum.TryParse<VolatilityState>(situation.VolatilityState, ignoreCase: true, out var volatility) &&
            volatility is VolatilityState.High or VolatilityState.Extreme)
        {
            score -= 15;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static string BuildReason(StrategyCode code, MarketRegime regime, int score, bool recommended)
    {
        if (regime == MarketRegime.Abnormal)
        {
            return "Market appears abnormal. LivePaper trading is not recommended.";
        }

        if (!recommended)
        {
            return $"{code.ToCode()} is less suitable because current regime is {regime}, not a primary match for this strategy.";
        }

        return regime switch
        {
            _ when code == StrategyCode.FourHourRangeReEntry && recommended =>
                "The New York 4H range strategy fits the current range/reversal/breakout context after the opening range is available.",
            MarketRegime.Ranging when code == StrategyCode.VwapMeanReversion =>
                "Market is ranging and price is near VWAP with neutral-to-mean-reversion conditions.",
            MarketRegime.Ranging when code == StrategyCode.LiquiditySweep =>
                "Ranging markets can produce sweep/reclaim setups.",
            MarketRegime.Trending =>
                $"{code.ToCode()} aligns with the current trending regime.",
            _ => $"{code.ToCode()} has a reasonable match for the current {regime} regime (score {score})."
        };
    }

    private static IReadOnlyList<string> BuildWarnings(StrategyCode code, MarketRegime regime)
    {
        var warnings = new List<string>();
        if (code == StrategyCode.LiquiditySweep && regime == MarketRegime.Ranging)
        {
            warnings.Add("Requires swing level confirmation.");
        }

        if (regime == MarketRegime.HighVolatility && code == StrategyCode.AtrVolatilityBreakout)
        {
            warnings.Add("High volatility increases risk. Ensure risk profile allows breakout entries.");
        }

        if (regime == MarketRegime.Choppy)
        {
            warnings.Add("Choppy market conditions reduce strategy reliability.");
        }

        if (code == StrategyCode.FourHourRangeReEntry)
        {
            warnings.Add("Requires the first 4 hours of the New York trading day to be fully closed.");
            if (regime is MarketRegime.HighVolatility or MarketRegime.Choppy)
            {
                warnings.Add("Default parameters block high-volatility and choppy conditions.");
            }
        }

        return warnings;
    }

    private static bool IsAfterNewYorkRangeClose(DateTime utcTime)
    {
        var timezone = FourHourRangeService.ResolveTimezone("America/New_York");
        var utc = utcTime.Kind == DateTimeKind.Utc ? utcTime : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, timezone);
        return local.TimeOfDay >= TimeSpan.FromHours(4);
    }
}
