using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// WP9 — Deterministic parity fixture: 150+ warm-up available, 500 eval, 50 post-boundary inaccessible,
/// required warmup = 100. Compares validation CreateStrategyLabDataset against the standard chronological
/// warm-up algorithm (ORDER DESC TAKE N → ASC + eval window) used by CandleRepository.
/// </summary>
public sealed class ValidationWarmupParityFixtureTests
{
    private static readonly DateTime EvalStart = new(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int RequiredWarmup = 100;
    private const int AvailableWarmup = 150;
    private const int EvalCount = 500;
    private const int PostBoundary = 50;
    private const long SymbolId = 42;

    [Fact]
    public async Task ValidationPath_MatchesStandardWarmupPartition_TimestampsOhlcvIndicesFingerprint()
    {
        var all = BuildSeries();
        var boundary = EvalStart.AddHours(EvalCount);
        var evalEndExclusive = boundary;
        var warmupSource = all.Where(c => c.OpenTimeUtc < EvalStart && c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToList();
        var warmup = warmupSource.TakeLast(RequiredWarmup).ToList();
        var evaluation = all
            .Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEndExclusive && c.OpenTimeUtc < boundary)
            .OrderBy(c => c.OpenTimeUtc)
            .ToList();

        Assert.Equal(AvailableWarmup, warmupSource.Count);
        Assert.Equal(RequiredWarmup, warmup.Count);
        Assert.Equal(EvalCount, evaluation.Count);
        Assert.Equal(PostBoundary, all.Count(c => c.OpenTimeUtc >= boundary));

        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId: 7,
            symbolId: SymbolId,
            symbolName: "PARITYUSDT",
            timeframe: "1h",
            requiredWarmup: RequiredWarmup,
            availableWarmup: warmup.Count,
            evaluationCount: evaluation.Count,
            status: ValidationWarmupStatus.Complete,
            evalStart: EvalStart,
            evalEndExclusive: evalEndExclusive,
            boundary: boundary,
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: warmup.Concat(evaluation).ToList());

        var scope = new ValidationTrainingCandleScope(partition, warmup, evaluation);
        Assert.Equal(ValidationWarmupStatus.Complete, scope.Partition.WarmupStatus);
        Assert.Equal(RequiredWarmup, scope.Partition.AvailableWarmupCandleCount);

        // Post-boundary bars must remain inaccessible.
        Assert.Throws<ValidationDataLeakageException>(() =>
            scope.GetByOpenTimeUtc(boundary, "ParityProbe"));

        var run = new StrategyLabRun
        {
            Id = 1,
            Name = "parity",
            StrategyCode = "PRICE_STRUCTURE_BREAKOUT_RETEST",
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            SymbolId = SymbolId,
            Symbol = "PARITYUSDT",
            Timeframe = "1h",
            FromUtc = EvalStart,
            ToUtc = evalEndExclusive,
            ExecutionMode = StrategyLabExecutionMode.RawStrategy,
            ParametersJson = "{}",
            InitialBalance = 10_000m,
            FeeSettingsJson = "{}",
            SlippageSettingsJson = "{}",
            Status = StrategyLabRunStatus.Created,
            CreatedAtUtc = DateTime.UtcNow
        };

        var validationSource = new ValidationTrainingStrategyLabCandleDataSource(scope, "ParityValidation");
        var validationDataset = await validationSource.LoadAsync(run, RequiredWarmup);

        // Standard chronological algorithm (CandleRepository / BacktestDataLoader warm-up semantics
        // without the unrelated Max(warmup, 600) floor): DESC TAKE N then ASC + eval range.
        var standardCandles = StandardChronologicalLoad(all, SymbolId, EvalStart, evalEndExclusive, RequiredWarmup);
        var standardEvalIndices = standardCandles
            .Select((c, i) => (c, i))
            .Where(x => x.c.OpenTimeUtc >= EvalStart && x.c.OpenTimeUtc < evalEndExclusive)
            .Select(x => x.i)
            .ToList();

        Assert.Equal(standardCandles.Count, validationDataset.Candles.Count);
        Assert.Equal(RequiredWarmup, validationDataset.WarmupCandleCount);
        Assert.Equal(standardEvalIndices, validationDataset.EvaluationIndices.ToList());

        for (var i = 0; i < standardCandles.Count; i++)
        {
            var a = standardCandles[i];
            var b = validationDataset.Candles[i];
            Assert.Equal(a.OpenTimeUtc, b.OpenTimeUtc);
            Assert.Equal(a.Open, b.Open);
            Assert.Equal(a.High, b.High);
            Assert.Equal(a.Low, b.Low);
            Assert.Equal(a.Close, b.Close);
            Assert.Equal(a.Volume, b.Volume);
        }

        var standardFp = ValidationTrainingCandleScope.ComputeContentFingerprint(standardCandles);
        Assert.Equal(standardFp, validationDataset.CombinedContentFingerprint);
        Assert.Equal(standardFp, scope.Partition.CombinedContentFingerprint);

        // Exactly one StrategyLabDataset access event from the data source load.
        Assert.Contains(scope.AccessLog, a =>
            !a.WasDenied
            && a.AccessPurpose == ValidationCandleAccessPurpose.StrategyLabDataset
            && a.CallerComponent.Contains("ParityValidation", StringComparison.Ordinal));
    }

    [Fact]
    public void StandardStrategyLabCandleDataSource_FromBacktestPartition_AlignsEvaluationIndices()
    {
        var all = BuildSeries();
        var boundary = EvalStart.AddHours(EvalCount);
        var candles = StandardChronologicalLoad(all, SymbolId, EvalStart, boundary, RequiredWarmup);
        var evalIndices = candles
            .Select((c, i) => (c, i))
            .Where(x => x.c.OpenTimeUtc >= EvalStart && x.c.OpenTimeUtc < boundary)
            .Select(x => x.i)
            .ToList();

        var backtest = new BacktestDataset
        {
            SymbolId = SymbolId,
            SymbolName = "PARITYUSDT",
            Timeframe = Timeframe.H1,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = evalIndices
        };

        var dataset = StrategyLabDataset.FromBacktest(backtest);
        Assert.Equal(RequiredWarmup, dataset.WarmupCandleCount);
        Assert.Equal(evalIndices, dataset.EvaluationIndices.ToList());
        Assert.Equal(RequiredWarmup + EvalCount, dataset.Candles.Count);
    }

    private static IReadOnlyList<Candle> StandardChronologicalLoad(
        IReadOnlyList<Candle> universe,
        long symbolId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        int warmUpCount)
    {
        var warmup = universe
            .Where(c => c.SymbolId == symbolId && c.OpenTimeUtc < fromUtc)
            .OrderByDescending(c => c.OpenTimeUtc)
            .Take(warmUpCount)
            .OrderBy(c => c.OpenTimeUtc)
            .ToList();

        var range = universe
            .Where(c => c.SymbolId == symbolId && c.OpenTimeUtc >= fromUtc && c.OpenTimeUtc < toUtcExclusive)
            .OrderBy(c => c.OpenTimeUtc)
            .ToList();

        return warmup.Concat(range).ToList();
    }

    private static List<Candle> BuildSeries()
    {
        var list = new List<Candle>(AvailableWarmup + EvalCount + PostBoundary);
        var cursor = EvalStart.AddHours(-AvailableWarmup);
        for (var i = 0; i < AvailableWarmup + EvalCount + PostBoundary; i++)
        {
            var open = cursor.AddHours(i);
            var px = 100m + i;
            list.Add(new Candle
            {
                Id = i + 1,
                ExchangeId = 1,
                SymbolId = SymbolId,
                Timeframe = Timeframe.H1,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddHours(1),
                Open = px,
                High = px + 1,
                Low = px - 1,
                Close = px + 0.5m,
                Volume = 10m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return list;
    }
}
