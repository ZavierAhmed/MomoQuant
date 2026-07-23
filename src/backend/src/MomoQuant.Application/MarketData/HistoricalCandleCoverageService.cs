using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.MarketData;

public interface IHistoricalCandleCoverageService
{
    Task<HistoricalCandleCoverageResult> CheckCoverageAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles = 0,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<HistoricalCandleCoverageResult>> EnsureCoverageAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles,
        bool allowAutoImport,
        Func<HistoricalCoverageProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default);
}

public sealed class HistoricalCoverageProgress
{
    public string Stage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public decimal PercentComplete { get; init; }
}

public sealed class HistoricalCandleCoverageResult
{
    public required CandleCoverageDto Coverage { get; init; }
    public DateTime CoverageCheckStartedAtUtc { get; init; }
    public DateTime RequestedFromUtc { get; init; }
    public DateTime RequestedToUtc { get; init; }
    public string RequestedTimeframe { get; init; } = string.Empty;
    public int ExistingCandleCount { get; init; }
    public IReadOnlyList<CoverageMissingRange> MissingRanges { get; init; } = [];
    public bool AutoImportAttempted { get; init; }
    public DateTime? ImportStartedAtUtc { get; init; }
    public DateTime? ImportCompletedAtUtc { get; init; }
    public int ImportedCandleCount { get; init; }
    public string? ImportError { get; init; }
    public string FinalCoverageStatus { get; init; } = "Missing";
    public int WarmupCandlesRequested { get; init; }
}

public sealed class CoverageMissingRange
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public int EstimatedMissingCandles { get; init; }
}

public sealed class HistoricalCandleCoverageService : IHistoricalCandleCoverageService
{
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IMarketDataService _marketDataService;
    private readonly IBenchmarkImportRangeChunker _chunker;
    private readonly MarketDataSettings _settings;
    private readonly ILogger<HistoricalCandleCoverageService> _logger;

    public HistoricalCandleCoverageService(
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        ICandleRepository candleRepository,
        IMarketDataService marketDataService,
        IBenchmarkImportRangeChunker chunker,
        IOptions<MarketDataSettings> settings,
        ILogger<HistoricalCandleCoverageService>? logger = null)
    {
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _candleRepository = candleRepository;
        _marketDataService = marketDataService;
        _chunker = chunker;
        _settings = settings.Value;
        _logger = logger ?? NullLogger<HistoricalCandleCoverageService>.Instance;
    }

    public async Task<HistoricalCandleCoverageResult> CheckCoverageAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles = 0,
        CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);

        if (!TimeframeNormalizer.TryNormalize(timeframe, out var canonical)
            || !TimeframeParser.TryParse(canonical, out var parsedTimeframe))
        {
            return new HistoricalCandleCoverageResult
            {
                Coverage = new CandleCoverageDto
                {
                    Symbol = symbol?.SymbolName ?? symbolId.ToString(),
                    Exchange = exchange?.Name ?? "Unknown",
                    Timeframe = timeframe,
                    RequiredFromUtc = fromUtc,
                    RequiredToUtc = toUtc,
                    CoverageStatus = "Missing",
                    ImportError = TimeframeNormalizer.UnsupportedTimeframeMessage(timeframe)
                },
                CoverageCheckStartedAtUtc = started,
                RequestedFromUtc = fromUtc,
                RequestedToUtc = toUtc,
                RequestedTimeframe = timeframe,
                FinalCoverageStatus = "Missing",
                ImportError = TimeframeNormalizer.UnsupportedTimeframeMessage(timeframe),
                WarmupCandlesRequested = warmupCandles
            };
        }

        var warmupFrom = fromUtc.AddMinutes(
            -TimeframeParser.GetDurationMinutes(parsedTimeframe) * Math.Max(0, warmupCandles));
        var openTimes = await _candleRepository.GetOpenTimesInRangeAsync(
            exchangeId, symbolId, parsedTimeframe, warmupFrom, toUtc, cancellationToken);
        var missingRanges = ComputeMissingRanges(openTimes, warmupFrom, toUtc, parsedTimeframe);
        var estimatedMissing = missingRanges.Sum(r => r.EstimatedMissingCandles);
        var candleCount = openTimes.Count;
        var expected = EstimateExpectedCandles(warmupFrom, toUtc, parsedTimeframe);
        var status = ResolveStatus(candleCount, estimatedMissing, expected);

        return new HistoricalCandleCoverageResult
        {
            Coverage = new CandleCoverageDto
            {
                Symbol = symbol?.SymbolName ?? symbolId.ToString(),
                Exchange = exchange?.Name ?? "Unknown",
                Timeframe = canonical,
                RequiredFromUtc = warmupFrom,
                RequiredToUtc = toUtc,
                AvailableFromUtc = openTimes.Count > 0 ? openTimes.Min() : null,
                AvailableToUtc = openTimes.Count > 0 ? openTimes.Max() : null,
                CandleCount = candleCount,
                MissingCandleCountEstimate = estimatedMissing,
                CoverageStatus = status
            },
            CoverageCheckStartedAtUtc = started,
            RequestedFromUtc = fromUtc,
            RequestedToUtc = toUtc,
            RequestedTimeframe = canonical,
            ExistingCandleCount = candleCount,
            MissingRanges = missingRanges,
            FinalCoverageStatus = status,
            WarmupCandlesRequested = warmupCandles
        };
    }

    public async Task<ServiceResult<HistoricalCandleCoverageResult>> EnsureCoverageAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmupCandles,
        bool allowAutoImport,
        Func<HistoricalCoverageProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        async Task ReportAsync(string stage, string message, decimal percent)
        {
            if (onProgress is null)
            {
                return;
            }

            await onProgress(new HistoricalCoverageProgress
            {
                Stage = stage,
                Message = message,
                PercentComplete = percent
            }, cancellationToken);
        }

        await ReportAsync("CheckingCoverage", "Checking candle coverage...", 2m);

        var check = await CheckCoverageAsync(
            exchangeId, symbolId, timeframe, fromUtc, toUtc, warmupCandles, cancellationToken);

        if (!string.IsNullOrWhiteSpace(check.ImportError)
            && check.FinalCoverageStatus == "Missing"
            && check.Coverage.CandleCount == 0
            && check.MissingRanges.Count == 0)
        {
            return ServiceResult<HistoricalCandleCoverageResult>.Fail(
                check.ImportError ?? "Unsupported timeframe.",
                "timeframe");
        }

        await ReportAsync("CheckingCoverage", $"Found {check.ExistingCandleCount:N0} existing candles.", 5m);

        if (check.FinalCoverageStatus == "Complete")
        {
            return ServiceResult<HistoricalCandleCoverageResult>.Ok(check);
        }

        if (!allowAutoImport)
        {
            return ServiceResult<HistoricalCandleCoverageResult>.Fail(
                $"Missing candle coverage for {check.RequestedTimeframe}. " +
                $"Found {check.ExistingCandleCount:N0} candles; estimated {check.Coverage.MissingCandleCountEstimate:N0} missing. " +
                "Enable auto-import or import from Market Watch.",
                "candles");
        }

        var importStarted = DateTime.UtcNow;
        await ReportAsync(
            "ImportingCandles",
            $"Missing {check.Coverage.MissingCandleCountEstimate:N0} candles. Importing {check.Coverage.Symbol} {check.RequestedTimeframe}...",
            8m);

        var maxDays = Math.Max(_settings.Binance.MaxDaysPerImport, 1);
        var chunkDays = BenchmarkImportRangeChunker.ResolveChunkDays(maxDays, maxDays);
        var chunks = _chunker.CreateChunks(check.Coverage.RequiredFromUtc, check.Coverage.RequiredToUtc, chunkDays);
        if (chunks.Count == 0)
        {
            return ServiceResult<HistoricalCandleCoverageResult>.Fail(
                "Import range produced no valid chunks.",
                "candles");
        }

        var importedTotal = 0;
        string? lastError = null;

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            await ReportAsync(
                "ImportingCandles",
                $"Importing {check.Coverage.Symbol} {check.RequestedTimeframe} chunk {i + 1}/{chunks.Count} " +
                $"({chunk.FromUtc:yyyy-MM-dd} to {chunk.ToUtc:yyyy-MM-dd})...",
                8m + (decimal)(i + 1) / chunks.Count * 20m);

            var importResult = await _marketDataService.ImportCandlesAsync(new ImportCandlesRequest
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Timeframe = check.RequestedTimeframe,
                FromUtc = chunk.FromUtc,
                ToUtc = chunk.ToUtc
            }, cancellationToken);

            if (!importResult.Succeeded)
            {
                lastError = importResult.ErrorMessage ?? "Candle import failed.";
                _logger.LogWarning(
                    "Strategy Lab candle import failed for {Symbol} {Timeframe} {From}-{To}: {Error}",
                    check.Coverage.Symbol,
                    check.RequestedTimeframe,
                    chunk.FromUtc,
                    chunk.ToUtc,
                    lastError);
                break;
            }

            importedTotal += importResult.Data?.InsertedCount
                ?? importResult.Data?.TotalReceived
                ?? 0;
        }

        await ReportAsync(
            "VerifyingCoverage",
            $"Imported {importedTotal:N0} candles. Verifying coverage...",
            30m);
        var refreshed = await CheckCoverageAsync(
            exchangeId, symbolId, timeframe, fromUtc, toUtc, warmupCandles, cancellationToken);

        var result = new HistoricalCandleCoverageResult
        {
            Coverage = new CandleCoverageDto
            {
                Symbol = refreshed.Coverage.Symbol,
                Exchange = refreshed.Coverage.Exchange,
                Timeframe = refreshed.Coverage.Timeframe,
                RequiredFromUtc = refreshed.Coverage.RequiredFromUtc,
                RequiredToUtc = refreshed.Coverage.RequiredToUtc,
                AvailableFromUtc = refreshed.Coverage.AvailableFromUtc,
                AvailableToUtc = refreshed.Coverage.AvailableToUtc,
                CandleCount = refreshed.Coverage.CandleCount,
                MissingCandleCountEstimate = refreshed.Coverage.MissingCandleCountEstimate,
                CoverageStatus = refreshed.FinalCoverageStatus,
                ImportedDuringRun = true,
                ImportError = lastError
            },
            CoverageCheckStartedAtUtc = check.CoverageCheckStartedAtUtc,
            RequestedFromUtc = fromUtc,
            RequestedToUtc = toUtc,
            RequestedTimeframe = refreshed.RequestedTimeframe,
            ExistingCandleCount = check.ExistingCandleCount,
            MissingRanges = refreshed.MissingRanges,
            AutoImportAttempted = true,
            ImportStartedAtUtc = importStarted,
            ImportCompletedAtUtc = DateTime.UtcNow,
            ImportedCandleCount = importedTotal,
            ImportError = lastError,
            FinalCoverageStatus = refreshed.FinalCoverageStatus,
            WarmupCandlesRequested = warmupCandles
        };

        if (lastError is not null && refreshed.FinalCoverageStatus != "Complete")
        {
            return ServiceResult<HistoricalCandleCoverageResult>.Fail(
                $"Auto-import failed: {lastError}",
                "candles");
        }

        if (refreshed.FinalCoverageStatus != "Complete")
        {
            var stillMissing = refreshed.Coverage.MissingCandleCountEstimate;
            var sample = refreshed.MissingRanges.FirstOrDefault();
            var rangeHint = sample is null
                ? string.Empty
                : $" between {sample.FromUtc:yyyy-MM-dd HH:mm} and {sample.ToUtc:yyyy-MM-dd HH:mm}";
            return ServiceResult<HistoricalCandleCoverageResult>.Fail(
                $"Import completed but {stillMissing:N0} candles are still missing{rangeHint}.",
                "candles");
        }

        await ReportAsync("VerifyingCoverage", "Coverage complete.", 32m);

        return ServiceResult<HistoricalCandleCoverageResult>.Ok(result);
    }

    private static string ResolveStatus(int candleCount, int estimatedMissing, int expected)
    {
        if (candleCount == 0)
        {
            return "Missing";
        }

        if (estimatedMissing <= Math.Max(2, expected / 50) || (expected > 0 && candleCount >= expected * 0.95))
        {
            return "Complete";
        }

        return "Partial";
    }

    private static int EstimateExpectedCandles(DateTime fromUtc, DateTime toUtc, Timeframe timeframe)
    {
        var minutes = TimeframeParser.GetDurationMinutes(timeframe);
        if (minutes <= 0)
        {
            return 0;
        }

        return (int)Math.Max(0, (toUtc - fromUtc).TotalMinutes) / minutes;
    }

    private static IReadOnlyList<CoverageMissingRange> ComputeMissingRanges(
        IReadOnlyList<DateTime> openTimes,
        DateTime fromUtc,
        DateTime toUtc,
        Timeframe timeframe)
    {
        var minutes = TimeframeParser.GetDurationMinutes(timeframe);
        if (minutes <= 0 || fromUtc >= toUtc)
        {
            return [];
        }

        var step = TimeSpan.FromMinutes(minutes);
        var existing = new HashSet<DateTime>(openTimes.Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc)));
        var missing = new List<CoverageMissingRange>();
        DateTime? gapStart = null;
        var gapCount = 0;
        DateTime? gapEnd = null;

        for (var cursor = fromUtc; cursor < toUtc; cursor = cursor.Add(step))
        {
            var utc = DateTime.SpecifyKind(cursor, DateTimeKind.Utc);
            if (existing.Contains(utc))
            {
                if (gapStart is not null)
                {
                    missing.Add(new CoverageMissingRange
                    {
                        FromUtc = gapStart.Value,
                        ToUtc = gapEnd ?? gapStart.Value.Add(step),
                        EstimatedMissingCandles = gapCount
                    });
                    gapStart = null;
                    gapCount = 0;
                    gapEnd = null;
                }

                continue;
            }

            gapStart ??= utc;
            gapEnd = utc.Add(step);
            gapCount++;
        }

        if (gapStart is not null)
        {
            missing.Add(new CoverageMissingRange
            {
                FromUtc = gapStart.Value,
                ToUtc = gapEnd ?? toUtc,
                EstimatedMissingCandles = gapCount
            });
        }

        // Bound diagnostic payload size.
        if (missing.Count > 50)
        {
            return missing.Take(50).ToList();
        }

        return missing;
    }
}
