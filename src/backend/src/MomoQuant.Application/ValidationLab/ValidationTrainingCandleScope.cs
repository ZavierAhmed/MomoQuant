using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationSegmentCandleSource
{
    long ValidationExperimentId { get; }
    DateTime SegmentStartUtc { get; }
    DateTime SegmentEndExclusiveUtc { get; }
    DateTime ValidationBoundaryUtc { get; }
    IReadOnlyList<Candle> Candles { get; }

    IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent);
    Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent);
    Candle this[int index] { get; }
    int Count { get; }
}

public interface IValidationTrainingCandleScope : IValidationSegmentCandleSource, IAsyncDisposable
{
    long? ActiveTrialId { get; set; }
    int? ActiveTrialNumber { get; set; }
    IReadOnlyList<ValidationCandleAccessRecord> AccessLog { get; }
}

public sealed record ValidationCandleAccessRecord(
    long ValidationExperimentId,
    long? TrialId,
    int? TrialNumber,
    string CallerComponent,
    DateTime? RequestedStartUtc,
    DateTime? RequestedEndUtc,
    DateTime? ReturnedStartUtc,
    DateTime? ReturnedEndUtc,
    int ReturnedCandleCount,
    DateTime? MinimumReturnedTimestampUtc,
    DateTime? MaximumReturnedTimestampUtc,
    string? CandleContentFingerprint,
    DateTime AccessedAtUtc,
    bool WasDenied,
    string? DenialReason);

/// <summary>
/// Ambient training candle scope. When set, candle repository reads must stay within the boundary.
/// </summary>
public static class ValidationTrainingCandleScopeAmbient
{
    private static readonly AsyncLocal<IValidationTrainingCandleScope?> CurrentScope = new();

    public static IValidationTrainingCandleScope? Current => CurrentScope.Value;

    public static IDisposable Enter(IValidationTrainingCandleScope scope)
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = scope;
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly IValidationTrainingCandleScope? _previous;
        private bool _disposed;

        public Pop(IValidationTrainingCandleScope? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentScope.Value = _previous;
        }
    }
}

public sealed class ValidationTrainingCandleScope : IValidationTrainingCandleScope
{
    private readonly ImmutableArray<Candle> _candles;
    private readonly List<ValidationCandleAccessRecord> _accessLog = new();
    private readonly object _gate = new();

    public ValidationTrainingCandleScope(
        long validationExperimentId,
        DateTime segmentStartUtc,
        DateTime validationBoundaryUtc,
        IReadOnlyList<Candle> trainingCandles)
    {
        ValidationExperimentId = validationExperimentId;
        SegmentStartUtc = DateTime.SpecifyKind(segmentStartUtc, DateTimeKind.Utc);
        ValidationBoundaryUtc = DateTime.SpecifyKind(validationBoundaryUtc, DateTimeKind.Utc);
        SegmentEndExclusiveUtc = ValidationBoundaryUtc;

        // Strictly before ValidationStartUtc.
        _candles = trainingCandles
            .Where(c => DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc) < ValidationBoundaryUtc)
            .OrderBy(c => c.OpenTimeUtc)
            .Select(CloneCandle)
            .ToImmutableArray();
    }

    public long ValidationExperimentId { get; }
    public DateTime SegmentStartUtc { get; }
    public DateTime SegmentEndExclusiveUtc { get; }
    public DateTime ValidationBoundaryUtc { get; }
    public IReadOnlyList<Candle> Candles => _candles;
    public long? ActiveTrialId { get; set; }
    public int? ActiveTrialNumber { get; set; }
    public IReadOnlyList<ValidationCandleAccessRecord> AccessLog
    {
        get
        {
            lock (_gate) return _accessLog.ToList();
        }
    }

    public int Count => _candles.Length;

    public Candle this[int index]
    {
        get
        {
            if (index < 0 || index >= _candles.Length)
            {
                RecordDenied(null, null, "Indexer", "IndexOutOfRange",
                    $"Candle index {index} is outside the training scope.");
                throw new ValidationDataLeakageException(
                    ValidationExperimentId,
                    ValidationBoundaryUtc,
                    "Indexer",
                    null,
                    null,
                    $"Training candle index {index} is outside the immutable training scope.");
            }

            var candle = _candles[index];
            RecordAllowed(candle.OpenTimeUtc, candle.OpenTimeUtc.AddTicks(1), [candle], "Indexer");
            return candle;
        }
    }

    public IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent)
    {
        var from = Normalize(fromUtc) ?? SegmentStartUtc;
        var to = Normalize(toUtcExclusive) ?? SegmentEndExclusiveUtc;

        if (from >= ValidationBoundaryUtc || to > ValidationBoundaryUtc)
        {
            RecordDenied(from, to, callerComponent, "BoundaryCrossed",
                $"Requested range [{from:O}, {to:O}) crosses ValidationStartUtc {ValidationBoundaryUtc:O}.");
            throw new ValidationDataLeakageException(
                ValidationExperimentId,
                ValidationBoundaryUtc,
                callerComponent,
                from,
                to,
                $"ValidationDataLeakageDetected: requested candle range crosses ValidationStartUtc {ValidationBoundaryUtc:O}.");
        }

        if (to <= from)
        {
            RecordAllowed(from, to, Array.Empty<Candle>(), callerComponent);
            return Array.Empty<Candle>();
        }

        var slice = _candles
            .Where(c => c.OpenTimeUtc >= from && c.OpenTimeUtc < to)
            .ToArray();
        RecordAllowed(from, to, slice, callerComponent);
        return slice;
    }

    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent)
    {
        var ts = Normalize(openTimeUtc)!.Value;
        if (ts >= ValidationBoundaryUtc)
        {
            RecordDenied(ts, ts, callerComponent, "BoundaryCrossed",
                $"Direct access to {ts:O} is at or beyond ValidationStartUtc {ValidationBoundaryUtc:O}.");
            throw new ValidationDataLeakageException(
                ValidationExperimentId,
                ValidationBoundaryUtc,
                callerComponent,
                ts,
                ts,
                $"ValidationDataLeakageDetected: requested candle at {ts:O} is at or beyond ValidationStartUtc.");
        }

        var match = _candles.FirstOrDefault(c => c.OpenTimeUtc == ts);
        if (match is null)
        {
            RecordAllowed(ts, ts, Array.Empty<Candle>(), callerComponent);
            return null;
        }

        RecordAllowed(ts, ts.AddTicks(1), [match], callerComponent);
        return match;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void RecordAllowed(
        DateTime? requestedStart,
        DateTime? requestedEnd,
        IReadOnlyList<Candle> returned,
        string caller)
    {
        lock (_gate)
        {
            _accessLog.Add(new ValidationCandleAccessRecord(
                ValidationExperimentId,
                ActiveTrialId,
                ActiveTrialNumber,
                caller,
                requestedStart,
                requestedEnd,
                returned.Count > 0 ? returned[0].OpenTimeUtc : null,
                returned.Count > 0 ? returned[^1].OpenTimeUtc : null,
                returned.Count,
                returned.Count > 0 ? returned.Min(c => c.OpenTimeUtc) : null,
                returned.Count > 0 ? returned.Max(c => c.OpenTimeUtc) : null,
                returned.Count > 0 ? ComputeContentFingerprint(returned) : null,
                DateTime.UtcNow,
                WasDenied: false,
                DenialReason: null));
        }
    }

    private void RecordDenied(
        DateTime? requestedStart,
        DateTime? requestedEnd,
        string callerComponent,
        string denialCode,
        string reason)
    {
        lock (_gate)
        {
            _accessLog.Add(new ValidationCandleAccessRecord(
                ValidationExperimentId,
                ActiveTrialId,
                ActiveTrialNumber,
                callerComponent,
                requestedStart,
                requestedEnd,
                null,
                null,
                0,
                null,
                null,
                null,
                DateTime.UtcNow,
                WasDenied: true,
                DenialReason: string.IsNullOrWhiteSpace(denialCode)
                    ? reason
                    : $"{denialCode}: {reason}"));
        }
    }

    private static DateTime? Normalize(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static Candle CloneCandle(Candle c) => new()
    {
        Id = c.Id,
        ExchangeId = c.ExchangeId,
        SymbolId = c.SymbolId,
        Timeframe = c.Timeframe,
        OpenTimeUtc = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc),
        CloseTimeUtc = DateTime.SpecifyKind(c.CloseTimeUtc, DateTimeKind.Utc),
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        QuoteVolume = c.QuoteVolume,
        TradeCount = c.TradeCount,
        IsClosed = c.IsClosed,
        CreatedAtUtc = c.CreatedAtUtc
    };

    public static string ComputeContentFingerprint(IReadOnlyList<Candle> candles)
    {
        var sb = new StringBuilder(candles.Count * 64);
        foreach (var c in candles.OrderBy(x => x.OpenTimeUtc))
        {
            sb.Append(DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture))
                .Append('|')
                .Append(c.Open.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(c.High.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(c.Low.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(c.Close.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(c.Volume.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }
}
