using MomoQuant.Application.MarketData;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Validation-training candle source. Loads exclusively from <see cref="IValidationTrainingCandleScope"/>.
/// Never uses BacktestDataLoader, ICandleRepository, coverage import, or DbContext.
/// </summary>
public sealed class ValidationTrainingStrategyLabCandleDataSource : IStrategyLabCandleDataSource
{
    private readonly IValidationTrainingCandleScope _scope;
    private readonly string _callerComponent;

    public ValidationTrainingStrategyLabCandleDataSource(
        IValidationTrainingCandleScope scope,
        string? callerComponent = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _callerComponent = string.IsNullOrWhiteSpace(callerComponent)
            ? "ValidationTrainingStrategyLabCandleDataSource"
            : callerComponent;
    }

    public Task<StrategyLabDataset> LoadAsync(
        StrategyLabRun run,
        int warmupCandles,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TimeframeParser.TryParse(run.Timeframe, out var parsedTimeframe))
        {
            throw new InvalidOperationException(TimeframeNormalizer.UnsupportedTimeframeMessage(run.Timeframe));
        }

        var fromUtc = DateTime.SpecifyKind(run.FromUtc, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(run.ToUtc, DateTimeKind.Utc);
        var boundary = DateTime.SpecifyKind(_scope.ValidationBoundaryUtc, DateTimeKind.Utc);

        if (fromUtc >= boundary || toUtc > boundary)
        {
            throw new ValidationTrainingBoundaryViolationException(
                _scope.ValidationExperimentId,
                boundary,
                _callerComponent,
                fromUtc,
                toUtc,
                $"Requested training range [{fromUtc:O}, {toUtc:O}] crosses ValidationStartUtc {boundary:O}.");
        }

        // Repository semantics: OpenTimeUtc < toUtc (exclusive upper bound).
        var range = _scope.GetRange(fromUtc, toUtc, _callerComponent);

        IReadOnlyList<Candle> candles;
        if (warmupCandles > 0 && range.Count > 0)
        {
            var first = range[0].OpenTimeUtc;
            var warm = _scope.Candles
                .Where(c => c.OpenTimeUtc < first)
                .TakeLast(warmupCandles)
                .ToList();
            candles = warm.Count == 0
                ? range
                : warm.Concat(range).ToList();
        }
        else if (warmupCandles > 0)
        {
            candles = _scope.Candles
                .Where(c => c.OpenTimeUtc < fromUtc)
                .TakeLast(warmupCandles)
                .ToList();
        }
        else
        {
            candles = range;
        }

        VerifyStrictlyBeforeBoundary(candles, boundary, fromUtc, toUtc);

        var evaluationIndices = candles
            .Select((candle, index) => (candle, index))
            .Where(item => item.candle.OpenTimeUtc >= fromUtc && item.candle.OpenTimeUtc <= toUtc)
            .Select(item => item.index)
            .ToList();

        foreach (var index in evaluationIndices)
        {
            var open = candles[index].OpenTimeUtc;
            if (open >= boundary)
            {
                throw new ValidationTrainingBoundaryViolationException(
                    _scope.ValidationExperimentId,
                    boundary,
                    _callerComponent,
                    open,
                    open,
                    $"Evaluation candle at {open:O} is at or beyond ValidationStartUtc {boundary:O}.");
            }
        }

        var dataset = new StrategyLabDataset
        {
            SymbolId = run.SymbolId,
            SymbolName = run.Symbol,
            Timeframe = parsedTimeframe,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = evaluationIndices
        };

        return Task.FromResult(dataset);
    }

    private void VerifyStrictlyBeforeBoundary(
        IReadOnlyList<Candle> candles,
        DateTime boundary,
        DateTime requestedFrom,
        DateTime requestedTo)
    {
        foreach (var candle in candles)
        {
            var open = DateTime.SpecifyKind(candle.OpenTimeUtc, DateTimeKind.Utc);
            if (open >= boundary)
            {
                throw new ValidationTrainingBoundaryViolationException(
                    _scope.ValidationExperimentId,
                    boundary,
                    _callerComponent,
                    requestedFrom,
                    requestedTo,
                    $"Returned candle at {open:O} is at or beyond ValidationStartUtc {boundary:O}.");
            }
        }
    }
}
