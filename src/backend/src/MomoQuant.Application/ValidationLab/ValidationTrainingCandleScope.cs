using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Production candle-access surface for validation training. Public raw candle lists / indexers
/// are intentionally omitted — use audited methods only.
/// </summary>
public interface IValidationSegmentCandleSource
{
    long ValidationExperimentId { get; }
    DateTime SegmentStartUtc { get; }
    DateTime SegmentEndExclusiveUtc { get; }
    DateTime ValidationBoundaryUtc { get; }
    ValidationCandlePartitionMetadata Partition { get; }

    IReadOnlyList<Candle> GetWarmupBefore(
        DateTime beforeOpenTimeUtc,
        int count,
        ValidationCandleAccessContext context);

    IReadOnlyList<Candle> GetEvaluationRange(
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        ValidationCandleAccessContext context);

    Candle? GetByOpenTimeUtc(DateTime openTimeUtc, ValidationCandleAccessContext context);

    /// <summary>Compatibility overload — defaults purpose to <see cref="ValidationCandleAccessPurpose.ByOpenTime"/>.</summary>
    Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent);

    /// <summary>Compatibility range read — defaults purpose to <see cref="ValidationCandleAccessPurpose.RepositoryRange"/>.</summary>
    IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent);

    StrategyLabDataset CreateStrategyLabDataset(
        StrategyLabRun run,
        int warmupCandles,
        ValidationCandleAccessContext context);
}

public interface IValidationTrainingCandleScope : IValidationSegmentCandleSource, IAsyncDisposable
{
    /// <summary>Stable id for this scope instance; shared by all access events logged here.</summary>
    Guid ScopeExecutionId { get; }

    /// <summary>Optional correlation id propagated onto access evidence.</summary>
    string? CorrelationId { get; set; }

    long? ActiveTrialId { get; set; }
    int? ActiveTrialNumber { get; set; }
    IReadOnlyList<ValidationCandleAccessRecord> AccessLog { get; }
}

/// <summary>
/// In-memory candle access evidence collected during training. <see cref="AccessEventId"/> is
/// generated exactly once when the event is created and never regenerated on flush/retry.
/// <see cref="ScopeSequenceNumber"/> is assigned monotonically at creation for confirmed-cursor flush.
/// </summary>
public sealed class ValidationCandleAccessRecord
{
    public Guid AccessEventId { get; init; }
    public Guid ScopeExecutionId { get; init; }

    /// <summary>Monotonic per-scope sequence assigned once at event creation.</summary>
    public long ScopeSequenceNumber { get; init; }

    public long ValidationExperimentId { get; init; }
    public long? TrialId { get; init; }
    public int? TrialNumber { get; init; }
    public string CallerComponent { get; init; } = string.Empty;
    public ValidationCandleAccessPurpose AccessPurpose { get; init; }
    public DateTime? RequestedStartUtc { get; init; }
    public DateTime? RequestedEndUtc { get; init; }
    public int? RequestedCandleCount { get; init; }
    public DateTime? ReturnedStartUtc { get; init; }
    public DateTime? ReturnedEndUtc { get; init; }
    public int ReturnedCandleCount { get; init; }
    public DateTime? MinimumReturnedTimestampUtc { get; init; }
    public DateTime? MaximumReturnedTimestampUtc { get; init; }
    public string? CandleContentFingerprint { get; init; }
    public DateTime AccessedAtUtc { get; init; }
    public bool WasDenied { get; init; }
    public string? DenialCode { get; init; }
    public string? DenialReason { get; init; }
    public string? CorrelationId { get; init; }
    public string DatasetPartition { get; init; } = "Training";
    public string RecorderVersion { get; init; } = ValidationCandleAccessRecorder.RecorderVersion;

    /// <summary>Optional; incremented when a flush attempt includes this event.</summary>
    public int FlushAttemptCount { get; set; }

    /// <summary>Set after a successful durable persist.</summary>
    public DateTime? PersistedAtUtc { get; set; }
}

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
    private readonly ImmutableArray<Candle> _all;
    private readonly List<ValidationCandleAccessRecord> _accessLog = new();
    private readonly object _gate = new();
    private long _nextScopeSequence;

    /// <summary>
    /// Legacy constructor used by existing unit tests: all candles strictly before the boundary,
    /// with evaluation starting at <paramref name="segmentStartUtc"/>.
    /// </summary>
    public ValidationTrainingCandleScope(
        long validationExperimentId,
        DateTime segmentStartUtc,
        DateTime validationBoundaryUtc,
        IReadOnlyList<Candle> trainingCandles,
        Guid? scopeExecutionId = null)
    {
        ArgumentNullException.ThrowIfNull(trainingCandles);

        var start = DateTime.SpecifyKind(segmentStartUtc, DateTimeKind.Utc);
        var boundary = DateTime.SpecifyKind(validationBoundaryUtc, DateTimeKind.Utc);
        var allowed = trainingCandles
            .Where(c => DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc) < boundary)
            .OrderBy(c => c.OpenTimeUtc)
            .Select(CloneCandle)
            .ToList();

        var warmup = allowed.Where(c => c.OpenTimeUtc < start).ToList();
        var evaluation = allowed.Where(c => c.OpenTimeUtc >= start).ToList();

        ValidationExperimentId = validationExperimentId;
        ScopeExecutionId = scopeExecutionId ?? Guid.NewGuid();
        SegmentStartUtc = start;
        ValidationBoundaryUtc = boundary;
        SegmentEndExclusiveUtc = boundary;
        _all = allowed.ToImmutableArray();

        Partition = BuildPartition(
            validationExperimentId,
            symbolId: allowed.FirstOrDefault()?.SymbolId ?? 0,
            symbolName: string.Empty,
            timeframe: allowed.FirstOrDefault()?.Timeframe.ToString() ?? string.Empty,
            requiredWarmup: 0,
            availableWarmup: warmup.Count,
            evaluationCount: evaluation.Count,
            status: ValidationWarmupStatus.NotRequired,
            evalStart: start,
            evalEndExclusive: boundary,
            boundary: boundary,
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: allowed);
    }

    public ValidationTrainingCandleScope(
        ValidationCandlePartitionMetadata partition,
        IReadOnlyList<Candle> warmupCandles,
        IReadOnlyList<Candle> evaluationCandles,
        Guid? scopeExecutionId = null)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(warmupCandles);
        ArgumentNullException.ThrowIfNull(evaluationCandles);

        ValidationExperimentId = partition.ValidationExperimentId;
        ScopeExecutionId = scopeExecutionId ?? Guid.NewGuid();
        Partition = partition;
        SegmentStartUtc = DateTime.SpecifyKind(partition.TrainingEvaluationStartUtc, DateTimeKind.Utc);
        ValidationBoundaryUtc = DateTime.SpecifyKind(partition.ValidationBoundaryUtc, DateTimeKind.Utc);
        SegmentEndExclusiveUtc = Min(
            DateTime.SpecifyKind(partition.TrainingEvaluationEndExclusiveUtc, DateTimeKind.Utc),
            ValidationBoundaryUtc);

        var warmup = warmupCandles
            .Where(c =>
            {
                var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
                return open < SegmentStartUtc && open < ValidationBoundaryUtc;
            })
            .OrderBy(c => c.OpenTimeUtc)
            .Select(CloneCandle)
            .ToList();

        var evaluation = evaluationCandles
            .Where(c =>
            {
                var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
                return open >= SegmentStartUtc
                       && open < SegmentEndExclusiveUtc
                       && open < ValidationBoundaryUtc;
            })
            .OrderBy(c => c.OpenTimeUtc)
            .Select(CloneCandle)
            .ToList();

        _all = warmup.Concat(evaluation).ToImmutableArray();
    }

    public Guid ScopeExecutionId { get; }
    public string? CorrelationId { get; set; }
    public long ValidationExperimentId { get; }
    public DateTime SegmentStartUtc { get; }
    public DateTime SegmentEndExclusiveUtc { get; }
    public DateTime ValidationBoundaryUtc { get; }
    public ValidationCandlePartitionMetadata Partition { get; }
    public long? ActiveTrialId { get; set; }
    public int? ActiveTrialNumber { get; set; }
    public IReadOnlyList<ValidationCandleAccessRecord> AccessLog
    {
        get
        {
            lock (_gate) return _accessLog.ToList();
        }
    }

    /// <summary>Internal total for repository CountCandlesAsync — not part of the public production interface.</summary>
    internal int InternalCandleCount => _all.Length;

    public IReadOnlyList<Candle> GetWarmupBefore(
        DateTime beforeOpenTimeUtc,
        int count,
        ValidationCandleAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var before = Normalize(beforeOpenTimeUtc)!.Value;
        if (before > ValidationBoundaryUtc)
        {
            RecordDenied(before, before, context, "BoundaryCrossed",
                $"Warm-up before {before:O} crosses ValidationStartUtc {ValidationBoundaryUtc:O}.");
            throw new ValidationDataLeakageException(
                ValidationExperimentId,
                ValidationBoundaryUtc,
                context.ToCallerAuditLabel(),
                before,
                before,
                $"ValidationDataLeakageDetected: warm-up request crosses ValidationStartUtc {ValidationBoundaryUtc:O}.");
        }

        if (count <= 0)
        {
            RecordAllowed(before, before, Array.Empty<Candle>(), context);
            return Array.Empty<Candle>();
        }

        var slice = SliceWarmupBefore(before, count);
        RecordAllowed(
            slice.Count > 0 ? slice[0].OpenTimeUtc : before,
            before,
            slice,
            context);
        return slice;
    }

    public IReadOnlyList<Candle> GetEvaluationRange(
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        ValidationCandleAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var from = Normalize(fromUtc) ?? SegmentStartUtc;
        var to = Normalize(toUtcExclusive) ?? SegmentEndExclusiveUtc;

        if (from >= ValidationBoundaryUtc || to > ValidationBoundaryUtc)
        {
            RecordDenied(from, to, context, "BoundaryCrossed",
                $"Requested range [{from:O}, {to:O}) crosses ValidationStartUtc {ValidationBoundaryUtc:O}.");
            throw new ValidationDataLeakageException(
                ValidationExperimentId,
                ValidationBoundaryUtc,
                context.ToCallerAuditLabel(),
                from,
                to,
                $"ValidationDataLeakageDetected: requested candle range crosses ValidationStartUtc {ValidationBoundaryUtc:O}.");
        }

        if (to <= from)
        {
            RecordAllowed(from, to, Array.Empty<Candle>(), context);
            return Array.Empty<Candle>();
        }

        var slice = SliceEvaluation(from, to);
        RecordAllowed(from, to, slice, context);
        return slice;
    }

    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, ValidationCandleAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ts = Normalize(openTimeUtc)!.Value;
        if (ts >= ValidationBoundaryUtc)
        {
            RecordDenied(ts, ts, context, "BoundaryCrossed",
                $"Direct access to {ts:O} is at or beyond ValidationStartUtc {ValidationBoundaryUtc:O}.");
            throw new ValidationDataLeakageException(
                ValidationExperimentId,
                ValidationBoundaryUtc,
                context.ToCallerAuditLabel(),
                ts,
                ts,
                $"ValidationDataLeakageDetected: requested candle at {ts:O} is at or beyond ValidationStartUtc.");
        }

        var match = _all.FirstOrDefault(c => c.OpenTimeUtc == ts);
        if (match is null)
        {
            RecordAllowed(ts, ts, Array.Empty<Candle>(), context);
            return null;
        }

        RecordAllowed(ts, ts.AddTicks(1), [match], context);
        return match;
    }

    /// <summary>Compatibility overload used by older call sites / repository decorator.</summary>
    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent) =>
        GetByOpenTimeUtc(
            openTimeUtc,
            ValidationCandleAccessContext.Create(callerComponent, ValidationCandleAccessPurpose.ByOpenTime));

    /// <summary>Compatibility overload for repository decorator range reads.</summary>
    public IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent) =>
        GetEvaluationRange(
            fromUtc,
            toUtcExclusive,
            ValidationCandleAccessContext.Create(callerComponent, ValidationCandleAccessPurpose.RepositoryRange));

    public StrategyLabDataset CreateStrategyLabDataset(
        StrategyLabRun run,
        int warmupCandles,
        ValidationCandleAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);

        if (run.SymbolId != Partition.SymbolId && Partition.SymbolId > 0)
        {
            RecordDenied(run.FromUtc, run.ToUtc, context, "SymbolMismatch",
                $"Run symbol {run.SymbolId} does not match scope symbol {Partition.SymbolId}.");
            throw new InvalidOperationException(
                $"Validation training scope symbol mismatch: run={run.SymbolId}, scope={Partition.SymbolId}.");
        }

        if (!TimeframeParser.TryParse(run.Timeframe, out var parsedTimeframe))
        {
            throw new InvalidOperationException(TimeframeNormalizer.UnsupportedTimeframeMessage(run.Timeframe));
        }

        var fromUtc = DateTime.SpecifyKind(run.FromUtc, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(run.ToUtc, DateTimeKind.Utc);
        var boundary = ValidationBoundaryUtc;

        if (fromUtc >= boundary || toUtc > boundary)
        {
            RecordDenied(fromUtc, toUtc, context, "BoundaryCrossed",
                $"Requested training range [{fromUtc:O}, {toUtc:O}) crosses ValidationStartUtc {boundary:O}.");
            throw new ValidationTrainingBoundaryViolationException(
                ValidationExperimentId,
                boundary,
                context.ToCallerAuditLabel(),
                fromUtc,
                toUtc,
                $"Requested training range [{fromUtc:O}, {toUtc:O}) crosses ValidationStartUtc {boundary:O}.");
        }

        var warm = warmupCandles > 0
            ? SliceWarmupBefore(fromUtc, warmupCandles)
            : Array.Empty<Candle>();
        var eval = SliceEvaluation(fromUtc, toUtc);
        var candles = warm.Count == 0 ? eval : warm.Concat(eval).ToList();

        foreach (var candle in candles)
        {
            var open = DateTime.SpecifyKind(candle.OpenTimeUtc, DateTimeKind.Utc);
            if (open >= boundary)
            {
                RecordDenied(fromUtc, toUtc, context, "BoundaryCrossed",
                    $"Returned candle at {open:O} is at or beyond ValidationStartUtc {boundary:O}.");
                throw new ValidationTrainingBoundaryViolationException(
                    ValidationExperimentId,
                    boundary,
                    context.ToCallerAuditLabel(),
                    fromUtc,
                    toUtc,
                    $"Returned candle at {open:O} is at or beyond ValidationStartUtc {boundary:O}.");
            }
        }

        var evaluationIndices = candles
            .Select((candle, index) => (candle, index))
            .Where(item =>
                item.candle.OpenTimeUtc >= fromUtc
                && item.candle.OpenTimeUtc < toUtc
                && item.candle.OpenTimeUtc < boundary)
            .Select(item => item.index)
            .ToList();

        RecordAllowed(fromUtc, toUtc, candles, context);

        return new StrategyLabDataset
        {
            SymbolId = run.SymbolId,
            SymbolName = string.IsNullOrWhiteSpace(run.Symbol) ? Partition.SymbolName : run.Symbol,
            Timeframe = parsedTimeframe,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = evaluationIndices,
            WarmupCandleCount = warm.Count,
            WarmupContentFingerprint = warm.Count > 0 ? ComputeContentFingerprint(warm) : null,
            EvaluationContentFingerprint = eval.Count > 0 ? ComputeContentFingerprint(eval) : null,
            CombinedContentFingerprint = candles.Count > 0 ? ComputeContentFingerprint(candles) : null
        };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IReadOnlyList<Candle> SliceWarmupBefore(DateTime beforeUtc, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<Candle>();
        }

        return _all
            .Where(c => c.OpenTimeUtc < beforeUtc && c.OpenTimeUtc < ValidationBoundaryUtc)
            .TakeLast(count)
            .ToArray();
    }

    private IReadOnlyList<Candle> SliceEvaluation(DateTime fromUtc, DateTime toUtcExclusive)
    {
        var to = Min(toUtcExclusive, ValidationBoundaryUtc);
        if (to <= fromUtc)
        {
            return Array.Empty<Candle>();
        }

        return _all
            .Where(c => c.OpenTimeUtc >= fromUtc && c.OpenTimeUtc < to)
            .ToArray();
    }

    private void RecordAllowed(
        DateTime? requestedStart,
        DateTime? requestedEnd,
        IReadOnlyList<Candle> returned,
        ValidationCandleAccessContext context)
    {
        lock (_gate)
        {
            _accessLog.Add(new ValidationCandleAccessRecord
            {
                AccessEventId = Guid.NewGuid(),
                ScopeExecutionId = ScopeExecutionId,
                ScopeSequenceNumber = ++_nextScopeSequence,
                ValidationExperimentId = ValidationExperimentId,
                TrialId = ActiveTrialId,
                TrialNumber = ActiveTrialNumber,
                CallerComponent = context.ToCallerAuditLabel(),
                AccessPurpose = context.AccessPurpose,
                RequestedStartUtc = requestedStart,
                RequestedEndUtc = requestedEnd,
                RequestedCandleCount = null,
                ReturnedStartUtc = returned.Count > 0 ? returned[0].OpenTimeUtc : null,
                ReturnedEndUtc = returned.Count > 0 ? returned[^1].OpenTimeUtc : null,
                ReturnedCandleCount = returned.Count,
                MinimumReturnedTimestampUtc = returned.Count > 0 ? returned.Min(c => c.OpenTimeUtc) : null,
                MaximumReturnedTimestampUtc = returned.Count > 0 ? returned.Max(c => c.OpenTimeUtc) : null,
                CandleContentFingerprint = returned.Count > 0 ? ComputeContentFingerprint(returned) : null,
                AccessedAtUtc = DateTime.UtcNow,
                WasDenied = false,
                DenialCode = null,
                DenialReason = null,
                CorrelationId = CorrelationId,
                DatasetPartition = ResolveDatasetPartition(context.AccessPurpose),
                RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion
            });
        }
    }

    private void RecordDenied(
        DateTime? requestedStart,
        DateTime? requestedEnd,
        ValidationCandleAccessContext context,
        string denialCode,
        string reason)
    {
        lock (_gate)
        {
            _accessLog.Add(new ValidationCandleAccessRecord
            {
                AccessEventId = Guid.NewGuid(),
                ScopeExecutionId = ScopeExecutionId,
                ScopeSequenceNumber = ++_nextScopeSequence,
                ValidationExperimentId = ValidationExperimentId,
                TrialId = ActiveTrialId,
                TrialNumber = ActiveTrialNumber,
                CallerComponent = context.ToCallerAuditLabel(),
                AccessPurpose = context.AccessPurpose,
                RequestedStartUtc = requestedStart,
                RequestedEndUtc = requestedEnd,
                RequestedCandleCount = null,
                ReturnedStartUtc = null,
                ReturnedEndUtc = null,
                ReturnedCandleCount = 0,
                MinimumReturnedTimestampUtc = null,
                MaximumReturnedTimestampUtc = null,
                CandleContentFingerprint = null,
                AccessedAtUtc = DateTime.UtcNow,
                WasDenied = true,
                DenialCode = string.IsNullOrWhiteSpace(denialCode) ? null : denialCode,
                DenialReason = reason,
                CorrelationId = CorrelationId,
                DatasetPartition = ResolveDatasetPartition(context.AccessPurpose),
                RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion
            });
        }
    }

    private static string ResolveDatasetPartition(ValidationCandleAccessPurpose purpose) =>
        purpose == ValidationCandleAccessPurpose.WarmupBefore ? "Warmup" : "Training";

    private static DateTime? Normalize(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

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

    public static ValidationCandlePartitionMetadata BuildPartition(
        long validationExperimentId,
        long symbolId,
        string symbolName,
        string timeframe,
        int requiredWarmup,
        int availableWarmup,
        int evaluationCount,
        ValidationWarmupStatus status,
        DateTime evalStart,
        DateTime evalEndExclusive,
        DateTime boundary,
        string requirementsVersion,
        IReadOnlyList<Candle> warmup,
        IReadOnlyList<Candle> evaluation,
        IReadOnlyList<Candle> combined) =>
        new()
        {
            ValidationExperimentId = validationExperimentId,
            RequiredWarmupCandleCount = requiredWarmup,
            AvailableWarmupCandleCount = availableWarmup,
            EvaluationCandleCount = evaluationCount,
            TotalCandleCount = combined.Count,
            WarmupStatus = status,
            TrainingEvaluationStartUtc = DateTime.SpecifyKind(evalStart, DateTimeKind.Utc),
            TrainingEvaluationEndExclusiveUtc = DateTime.SpecifyKind(evalEndExclusive, DateTimeKind.Utc),
            ValidationBoundaryUtc = DateTime.SpecifyKind(boundary, DateTimeKind.Utc),
            SymbolId = symbolId,
            SymbolName = symbolName,
            Timeframe = timeframe,
            RequirementsVersion = requirementsVersion,
            EvaluationStartIndex = warmup.Count,
            WarmupContentFingerprint = warmup.Count > 0 ? ComputeContentFingerprint(warmup) : null,
            EvaluationContentFingerprint = evaluation.Count > 0 ? ComputeContentFingerprint(evaluation) : null,
            CombinedContentFingerprint = combined.Count > 0 ? ComputeContentFingerprint(combined) : null
        };
}
