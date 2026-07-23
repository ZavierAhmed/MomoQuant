using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Infrastructure.MarketData;

public sealed class BinanceHistoricalCandleProvider : IHistoricalCandleProvider
{
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly MarketDataSettings _settings;
    private readonly ILogger<BinanceHistoricalCandleProvider> _logger;

    public BinanceHistoricalCandleProvider(
        HttpClient httpClient,
        IOptions<MarketDataSettings> settings,
        ILogger<BinanceHistoricalCandleProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HistoricalCandleDefinition>> GetCandlesAsync(
        string exchangeCode,
        string symbolName,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        _ = exchangeCode;

        var normalizedSymbol = symbolName.ToUpperInvariant();
        var allowedSymbols = _settings.Binance.AllowedSymbols
            .Select(symbol => symbol.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        if (!allowedSymbols.Contains(normalizedSymbol))
        {
            throw new InvalidOperationException(
                $"Symbol '{symbolName}' is not supported by the Binance historical candle provider.");
        }

        var interval = TimeframeParser.ToApiString(timeframe);
        if (!_settings.Binance.AllowedIntervals.Contains(interval, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Timeframe '{interval}' is not supported by the Binance historical candle provider.");
        }

        var limit = Math.Clamp(_settings.Binance.Limit, 1, 1500);
        var delayMs = Math.Max(_settings.Binance.RequestDelayMs, 0);
        var intervalMs = (long)timeframe * 60_000L;

        var candles = new List<HistoricalCandleDefinition>();
        var cursorMs = ToUnixMilliseconds(fromUtc);
        var endMs = ToUnixMilliseconds(toUtc);
        var batchNumber = 0;
        var maxBatches = CalculateMaxBatches(fromUtc, toUtc, timeframe, limit);

        _logger.LogInformation(
            "Starting Binance historical candle fetch for {Symbol} {Timeframe} from {FromUtc:O} to {ToUtc:O}.",
            normalizedSymbol,
            interval,
            fromUtc,
            toUtc);

        while (cursorMs < endMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchNumber++;

            if (batchNumber > maxBatches)
            {
                _logger.LogWarning(
                    "Stopping Binance fetch for {Symbol} {Timeframe} after reaching the safety batch limit {MaxBatches}.",
                    normalizedSymbol,
                    interval,
                    maxBatches);
                break;
            }

            var requestUri = BuildRequestUri(normalizedSymbol, interval, cursorMs, endMs, limit);
            using var response = await SendWithRetryAsync(requestUri, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var batch = BinanceKlineParser.ParseKlines(json);

            if (batch.Count == 0)
            {
                _logger.LogInformation(
                    "Binance historical candle fetch completed for {Symbol} {Timeframe} after {BatchCount} batches with no more data.",
                    normalizedSymbol,
                    interval,
                    batchNumber);
                break;
            }

            foreach (var candle in batch)
            {
                if (candle.OpenTimeUtc >= fromUtc && candle.OpenTimeUtc < toUtc)
                {
                    candles.Add(candle);
                }
            }

            var lastOpenMs = ToUnixMilliseconds(batch[^1].OpenTimeUtc);
            var nextCursorMs = lastOpenMs + intervalMs;

            _logger.LogInformation(
                "Fetched Binance kline batch {BatchNumber} for {Symbol} {Timeframe}: received {ReceivedCount}, total kept {KeptCount}.",
                batchNumber,
                normalizedSymbol,
                interval,
                batch.Count,
                candles.Count);

            if (nextCursorMs <= cursorMs || batch.Count < limit)
            {
                break;
            }

            cursorMs = nextCursorMs;

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Completed Binance historical candle fetch for {Symbol} {Timeframe}: {TotalCount} candles.",
            normalizedSymbol,
            interval,
            candles.Count);

        return candles;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string requestUri, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode == 418)
            {
                var retryDelay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(attempt * 2, 30));
                _logger.LogWarning(
                    "Binance rate limit response {StatusCode} on attempt {Attempt}. Waiting {DelaySeconds}s.",
                    (int)response.StatusCode,
                    attempt,
                    retryDelay.TotalSeconds);

                response.Dispose();
                await Task.Delay(retryDelay, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "Binance transient failure {StatusCode} on attempt {Attempt}. Retrying.",
                    (int)response.StatusCode,
                    attempt);

                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 500), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var summary = await ReadSafeErrorSummaryAsync(response, cancellationToken);
                response.Dispose();
                throw new InvalidOperationException($"Binance kline request failed: {summary}");
            }

            return response;
        }

        throw new InvalidOperationException("Binance kline request failed after retries.");
    }

    private static async Task<string> ReadSafeErrorSummaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"HTTP {(int)response.StatusCode}";
        }

        return body.Length <= 200 ? body : body[..200];
    }

    private static int CalculateMaxBatches(DateTime fromUtc, DateTime toUtc, Timeframe timeframe, int limit)
    {
        var expectedCandles = Math.Max((int)((toUtc - fromUtc).TotalMinutes / (int)timeframe), 1);
        var estimatedBatches = (expectedCandles / Math.Max(limit, 1)) + 2;
        return Math.Clamp(estimatedBatches, 1, 10_000);
    }

    private static string BuildRequestUri(
        string symbolName,
        string interval,
        long startTimeMs,
        long endTimeMs,
        int limit)
    {
        var symbol = Uri.EscapeDataString(symbolName.ToUpperInvariant());
        var intervalValue = Uri.EscapeDataString(interval);

        return $"fapi/v1/klines?symbol={symbol}&interval={intervalValue}&startTime={startTimeMs}&endTime={endTimeMs}&limit={limit}";
    }

    private static long ToUnixMilliseconds(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
