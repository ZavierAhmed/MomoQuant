using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.MarketData;

public interface IMarketDataCoverageService
{
    Task<CandleCoverageDto> CheckCoverageAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles = 0,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<CandleCoverageDto>>> EnsureCoverageAsync(
        long exchangeId,
        long symbolId,
        string strategyCode,
        string executionTimeframe,
        DateTime fromUtc,
        DateTime toUtc,
        bool allowImport,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<CandleCoverageDto>>> EnsureTimeframesCoverageAsync(
        long exchangeId,
        long symbolId,
        IEnumerable<string> timeframes,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles = 600,
        bool allowImport = true,
        CancellationToken cancellationToken = default);
}

public sealed class MarketDataCoverageService : IMarketDataCoverageService
{
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IMarketDataService _marketDataService;
    private readonly IHistoricalCandleCoverageService _historicalCoverage;

    public MarketDataCoverageService(
        IStrategyDataRequirementService requirementService,
        IStrategyRepository strategyRepository,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        ICandleRepository candleRepository,
        IMarketDataService marketDataService,
        IHistoricalCandleCoverageService historicalCoverage)
    {
        _requirementService = requirementService;
        _strategyRepository = strategyRepository;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _candleRepository = candleRepository;
        _marketDataService = marketDataService;
        _historicalCoverage = historicalCoverage;
    }

    public async Task<CandleCoverageDto> CheckCoverageAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles = 0,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);
        if (!TimeframeParser.TryParse(timeframe, out var parsedTimeframe))
        {
            return MissingCoverage(symbol?.SymbolName ?? symbolId.ToString(), exchange?.Name ?? "Unknown", timeframe, fromUtc, toUtc);
        }

        var canonicalTimeframe = TimeframeParser.ToApiString(parsedTimeframe);
        var warmupFrom = fromUtc.AddMinutes(-TimeframeParser.GetDurationMinutes(parsedTimeframe) * Math.Max(0, warmupCandles));
        var openTimes = await _candleRepository.GetOpenTimesInRangeAsync(
            exchangeId, symbolId, parsedTimeframe, warmupFrom, toUtc, cancellationToken);
        var candleCount = openTimes.Count;
        var status = candleCount == 0 ? "Missing" : candleCount < 10 ? "Partial" : "Complete";

        return new CandleCoverageDto
        {
            Symbol = symbol?.SymbolName ?? symbolId.ToString(),
            Exchange = exchange?.Name ?? "Unknown",
            Timeframe = canonicalTimeframe,
            RequiredFromUtc = warmupFrom,
            RequiredToUtc = toUtc,
            AvailableFromUtc = openTimes.Count > 0 ? openTimes.Min() : null,
            AvailableToUtc = openTimes.Count > 0 ? openTimes.Max() : null,
            CandleCount = candleCount,
            MissingCandleCountEstimate = candleCount == 0 ? EstimateMissingCandles(warmupFrom, toUtc, parsedTimeframe) : 0,
            CoverageStatus = status
        };
    }

    public async Task<ServiceResult<IReadOnlyList<CandleCoverageDto>>> EnsureCoverageAsync(
        long exchangeId,
        long symbolId,
        string strategyCode,
        string executionTimeframe,
        DateTime fromUtc,
        DateTime toUtc,
        bool allowImport,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<IReadOnlyList<CandleCoverageDto>>.Fail("Symbol was not found.");
        }

        var strategy = await _strategyRepository.GetByCodeAsync(StrategyCodeExtensions.FromCode(strategyCode), cancellationToken);
        StrategyDataRequirementDto? requirement = null;
        if (strategy is not null)
        {
            var reqResult = await _requirementService.GetByStrategyIdAsync(strategy.Id, cancellationToken);
            requirement = reqResult.Data;
        }

        var warmup = requirement?.WarmupCandles ?? 500;
        var timeframes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { executionTimeframe };
        foreach (var tf in requirement?.RequiredDataTimeframes ?? [])
        {
            timeframes.Add(tf);
        }

        var coverage = new List<CandleCoverageDto>();
        var importErrors = new List<string>();
        foreach (var timeframeValue in timeframes)
        {
            var ensured = await _historicalCoverage.EnsureCoverageAsync(
                exchangeId,
                symbolId,
                timeframeValue,
                fromUtc,
                toUtc,
                warmup,
                allowImport,
                onProgress: null,
                cancellationToken);

            if (!ensured.Succeeded || ensured.Data is null)
            {
                importErrors.Add(ensured.ErrorMessage ?? $"Missing candle data for {timeframeValue}.");
                var failedCheck = await _historicalCoverage.CheckCoverageAsync(
                    exchangeId, symbolId, timeframeValue, fromUtc, toUtc, warmup, cancellationToken);
                coverage.Add(new CandleCoverageDto
                {
                    Symbol = failedCheck.Coverage.Symbol,
                    Exchange = failedCheck.Coverage.Exchange,
                    Timeframe = failedCheck.Coverage.Timeframe,
                    RequiredFromUtc = failedCheck.Coverage.RequiredFromUtc,
                    RequiredToUtc = failedCheck.Coverage.RequiredToUtc,
                    AvailableFromUtc = failedCheck.Coverage.AvailableFromUtc,
                    AvailableToUtc = failedCheck.Coverage.AvailableToUtc,
                    CandleCount = failedCheck.Coverage.CandleCount,
                    MissingCandleCountEstimate = failedCheck.Coverage.MissingCandleCountEstimate,
                    CoverageStatus = "Missing",
                    ImportError = ensured.ErrorMessage
                });
                continue;
            }

            coverage.Add(ensured.Data.Coverage);
        }

        if (coverage.Any(c => c.CoverageStatus != "Complete"))
        {
            var detail = importErrors.Count > 0
                ? string.Join(" ", importErrors)
                : $"Missing candle data for: {string.Join(", ", coverage.Where(c => c.CoverageStatus != "Complete").Select(c => c.Timeframe))}.";
            return ServiceResult<IReadOnlyList<CandleCoverageDto>>.Fail(detail, "candles");
        }

        return ServiceResult<IReadOnlyList<CandleCoverageDto>>.Ok(coverage);
    }

    public async Task<ServiceResult<IReadOnlyList<CandleCoverageDto>>> EnsureTimeframesCoverageAsync(
        long exchangeId,
        long symbolId,
        IEnumerable<string> timeframes,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles = 600,
        bool allowImport = true,
        CancellationToken cancellationToken = default)
    {
        var coverage = new List<CandleCoverageDto>();
        foreach (var timeframeValue in timeframes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TimeframeNormalizer.TryNormalize(timeframeValue, out var canonical))
            {
                return ServiceResult<IReadOnlyList<CandleCoverageDto>>.Fail(
                    TimeframeNormalizer.UnsupportedTimeframeMessage(timeframeValue),
                    "timeframe");
            }

            var item = await CheckCoverageAsync(exchangeId, symbolId, canonical, fromUtc, toUtc, warmupCandles, cancellationToken);
            if (item.CoverageStatus != "Missing" || !allowImport)
            {
                coverage.Add(item);
                continue;
            }

            var importResult = await _marketDataService.ImportCandlesAsync(new ImportCandlesRequest
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Timeframe = canonical,
                FromUtc = item.RequiredFromUtc,
                ToUtc = item.RequiredToUtc
            }, cancellationToken);

            if (importResult.Succeeded)
            {
                var refreshed = await CheckCoverageAsync(exchangeId, symbolId, canonical, fromUtc, toUtc, warmupCandles, cancellationToken);
                coverage.Add(new CandleCoverageDto
                {
                    Symbol = refreshed.Symbol,
                    Exchange = refreshed.Exchange,
                    Timeframe = refreshed.Timeframe,
                    RequiredFromUtc = refreshed.RequiredFromUtc,
                    RequiredToUtc = refreshed.RequiredToUtc,
                    AvailableFromUtc = refreshed.AvailableFromUtc,
                    AvailableToUtc = refreshed.AvailableToUtc,
                    CandleCount = refreshed.CandleCount,
                    MissingCandleCountEstimate = refreshed.MissingCandleCountEstimate,
                    CoverageStatus = refreshed.CoverageStatus,
                    ImportedDuringRun = true
                });
            }
            else
            {
                coverage.Add(new CandleCoverageDto
                {
                    Symbol = item.Symbol,
                    Exchange = item.Exchange,
                    Timeframe = item.Timeframe,
                    RequiredFromUtc = item.RequiredFromUtc,
                    RequiredToUtc = item.RequiredToUtc,
                    CandleCount = item.CandleCount,
                    MissingCandleCountEstimate = item.MissingCandleCountEstimate,
                    CoverageStatus = "Missing",
                    ImportError = importResult.ErrorMessage ?? "Candle import failed."
                });
            }
        }

        if (coverage.Any(c => c.CoverageStatus == "Missing"))
        {
            var missing = coverage.Where(c => c.CoverageStatus == "Missing").Select(c => c.Timeframe);
            return ServiceResult<IReadOnlyList<CandleCoverageDto>>.Fail(
                $"Missing candle data for: {string.Join(", ", missing)}. Import candles from Market Watch or enable auto-import.",
                "candles");
        }

        return ServiceResult<IReadOnlyList<CandleCoverageDto>>.Ok(coverage);
    }

    private static CandleCoverageDto MissingCoverage(string symbol, string exchange, string timeframe, DateTime fromUtc, DateTime toUtc) =>
        new()
        {
            Symbol = symbol,
            Exchange = exchange,
            Timeframe = timeframe,
            RequiredFromUtc = fromUtc,
            RequiredToUtc = toUtc,
            CoverageStatus = "Missing"
        };

    private static int EstimateMissingCandles(DateTime fromUtc, DateTime toUtc, Timeframe timeframe)
    {
        var minutes = TimeframeParser.GetDurationMinutes(timeframe);
        if (minutes <= 0) return 0;
        return (int)Math.Max(0, (toUtc - fromUtc).TotalMinutes) / minutes;
    }
}
