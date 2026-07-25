using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E1 — Strict partition enforcement tests.
/// Tests all WP1 red scenarios (denial codes) as green assertions.
/// </summary>
public sealed class ValidationLab230E1PartitionEnforcementTests
{
    private static readonly DateTime EvalStart = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int RequiredWarmup = 50;
    private const int AvailableWarmup = 80;
    private const int EvalCount = 200;
    private const long SymbolId = 99;

    [Fact]
    public void WarmupRequest_AfterFixedEvaluationStart_IsDenied()
    {
        var scope = BuildScope();
        var after = EvalStart.AddHours(10);

        var request = new ValidationWarmupAccessRequest
        {
            BeforeOpenTimeUtc = after,
            Count = RequiredWarmup,
            Purpose = ValidationCandleAccessPurpose.WarmupBefore,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.GetWarmupBefore(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.WarmupRequestAfterEvaluationStart, ex.DenialCode);
    }

    [Fact]
    public void EvaluationRequest_BeforeFixedEvaluationStart_IsDenied()
    {
        var scope = BuildScope();
        var before = EvalStart.AddHours(-10);

        var request = new ValidationEvaluationAccessRequest
        {
            FromUtc = before,
            ToExclusiveUtc = EvalStart,
            AllowPartial = false,
            Purpose = ValidationCandleAccessPurpose.EvaluationRange,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.GetEvaluationRange(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.EvaluationRequestBeforeEvaluationStart, ex.DenialCode);
    }

    [Fact]
    public void EvaluationRequest_AfterFixedEvaluationEnd_IsDenied()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);
        var after = evalEnd.AddHours(10);

        var request = new ValidationEvaluationAccessRequest
        {
            FromUtc = EvalStart,
            ToExclusiveUtc = after,
            AllowPartial = false,
            Purpose = ValidationCandleAccessPurpose.EvaluationRange,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.GetEvaluationRange(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.EvaluationRequestAfterEvaluationEnd, ex.DenialCode);
    }

    [Fact]
    public void CompatibilityRange_SpanningWarmupAndEvaluation_IsDenied()
    {
        var scope = BuildScope();
        var from = EvalStart.AddHours(-10);
        var to = EvalStart.AddHours(10);

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.GetRange(from, to, "TestCaller"));
        Assert.Equal(ValidationCandlePartitionDenialCodes.CrossPartitionCompatibilityReadForbidden, ex.DenialCode);
    }

    [Fact]
    public void DatasetMaterialization_RunStartMismatch_IsDenied()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);
        var wrongStart = EvalStart.AddHours(5);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = wrongStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.RunStartMismatch, ex.DenialCode);
    }

    [Fact]
    public void DatasetMaterialization_RunEndMismatch_IsDenied()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);
        var wrongEnd = evalEnd.AddHours(-5);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = wrongEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.RunEndMismatch, ex.DenialCode);
    }

    [Fact]
    public void DatasetMaterialization_WarmupCountMismatch_IsDenied()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup + 10,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.WarmupCountMismatch, ex.DenialCode);
    }

    [Fact]
    public void Materialization_ProducesExactlyThreeLogicalEvents()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var dataset = scope.CreateStrategyLabDataset(request);
        Assert.NotNull(dataset);

        var accessLog = scope.AccessLog;
        Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.WarmupLoad && !a.WasDenied);
        Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.EvaluationLoad && !a.WasDenied);
        Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization && !a.WasDenied);
    }

    [Fact]
    public void WarmupLoad_PersistsRequestedCandleCount()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var dataset = scope.CreateStrategyLabDataset(request);
        Assert.Equal(RequiredWarmup, dataset.WarmupCandleCount);

        var warmupEvent = scope.AccessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.WarmupLoad);
        Assert.NotNull(warmupEvent);
        Assert.Equal(RequiredWarmup, warmupEvent.RequestedCandleCount);
        Assert.Equal(RequiredWarmup, warmupEvent.ReturnedCandleCount);
    }

    [Fact]
    public void EvaluationLoad_UsesExclusiveRequestedEnd()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var dataset = scope.CreateStrategyLabDataset(request);

        var lastCandle = dataset.Candles[^1];
        Assert.True(lastCandle.OpenTimeUtc < evalEnd);

        var evalEvent = scope.AccessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.EvaluationLoad);
        Assert.NotNull(evalEvent);
        Assert.Equal(EvalStart, evalEvent.RequestedStartUtc);
        Assert.Equal(evalEnd, evalEvent.RequestedEndUtc);
    }

    [Fact]
    public void DatasetMaterialization_UsesCombinedPartitionLabel()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var dataset = scope.CreateStrategyLabDataset(request);
        var datasetEvent = scope.AccessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization);
        Assert.NotNull(datasetEvent);
        Assert.Equal("Combined", datasetEvent.DatasetPartition);
    }

    [Fact]
    public void Constructor_AllowsDuplicateTimestamps_DeduplicatedInternalCollection()
    {
        var candles = new List<Candle>();
        var ts = EvalStart.AddHours(-10);
        candles.Add(BuildCandle(ts, 100m));
        candles.Add(BuildCandle(ts, 101m));

        var warmup = candles.Where(c => c.OpenTimeUtc < EvalStart).ToList();
        var evaluation = Array.Empty<Candle>();

        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId: 1,
            symbolId: SymbolId,
            symbolName: "TESTUSDT",
            timeframe: "1h",
            requiredWarmup: warmup.Count,
            availableWarmup: warmup.Count,
            evaluationCount: 0,
            status: ValidationWarmupStatus.Complete,
            evalStart: EvalStart,
            evalEndExclusive: EvalStart.AddHours(EvalCount),
            boundary: EvalStart.AddHours(EvalCount),
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: warmup);

        var scope = new ValidationTrainingCandleScope(partition, warmup, evaluation);
        Assert.Empty(scope.AccessLog);
    }

    [Fact]
    public void ZeroWarmup_StillEmitsWarmupLoadWithCountZero()
    {
        var all = BuildSeries(availableWarmup: 0, requiredWarmup: 0);
        var evalEnd = EvalStart.AddHours(EvalCount);
        var evaluation = all.Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEnd).ToList();

        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId: 1,
            symbolId: SymbolId,
            symbolName: "TESTUSDT",
            timeframe: "1h",
            requiredWarmup: 0,
            availableWarmup: 0,
            evaluationCount: evaluation.Count,
            status: ValidationWarmupStatus.NotRequired,
            evalStart: EvalStart,
            evalEndExclusive: evalEnd,
            boundary: evalEnd,
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: Array.Empty<Candle>(),
            evaluation: evaluation,
            combined: evaluation);

        var scope = new ValidationTrainingCandleScope(partition, Array.Empty<Candle>(), evaluation);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = 0,
            CallerComponent = "TestCaller"
        };

        var dataset = scope.CreateStrategyLabDataset(request);
        Assert.Equal(0, dataset.WarmupCandleCount);

        var warmupEvent = scope.AccessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.WarmupLoad);
        Assert.NotNull(warmupEvent);
        Assert.Equal(0, warmupEvent.RequestedCandleCount);
        Assert.Equal(0, warmupEvent.ReturnedCandleCount);
    }

    [Fact]
    public void DirectTimestampClassification_WarmupVsEvaluationPartition()
    {
        var scope = BuildScope();
        var warmupTs = EvalStart.AddHours(-5);
        var evalTs = EvalStart.AddHours(5);

        var warmupCandle = scope.GetByOpenTimeUtc(
            warmupTs,
            ValidationCandleAccessContext.Create("Test", ValidationCandleAccessPurpose.ByOpenTime));
        Assert.NotNull(warmupCandle);

        var evalCandle = scope.GetByOpenTimeUtc(
            evalTs,
            ValidationCandleAccessContext.Create("Test", ValidationCandleAccessPurpose.ByOpenTime));
        Assert.NotNull(evalCandle);

        var warmupLog = scope.AccessLog.Where(a => a.DatasetPartition == "DirectWarmup");
        var evalLog = scope.AccessLog.Where(a => a.DatasetPartition == "DirectEvaluation");
        Assert.NotEmpty(warmupLog);
        Assert.NotEmpty(evalLog);
    }

    [Fact]
    public void BacktestDataLoader_LegacyV1_UsesInclusiveEndSemantics()
    {
        var candles = BuildSeries();
        var fromUtc = EvalStart;
        var toUtc = EvalStart.AddHours(10);

        var evalIndices = candles
            .Select((c, i) => (c, i))
            .Where(x => x.c.OpenTimeUtc >= fromUtc && x.c.OpenTimeUtc <= toUtc)
            .Select(x => x.i)
            .ToList();

        Assert.NotEmpty(evalIndices);
        var lastIndex = evalIndices[^1];
        var lastCandle = candles[lastIndex];
        Assert.Equal(toUtc, lastCandle.OpenTimeUtc);
    }

    [Fact]
    public void BacktestDataLoader_ExactExclusiveV2_UsesExclusiveEndSemantics()
    {
        var candles = BuildSeries();
        var fromUtc = EvalStart;
        var toUtc = EvalStart.AddHours(10);

        var evalIndices = candles
            .Select((c, i) => (c, i))
            .Where(x => x.c.OpenTimeUtc >= fromUtc && x.c.OpenTimeUtc < toUtc)
            .Select(x => x.i)
            .ToList();

        Assert.NotEmpty(evalIndices);
        var lastIndex = evalIndices[^1];
        var lastCandle = candles[lastIndex];
        Assert.True(lastCandle.OpenTimeUtc < toUtc);
    }

    [Fact]
    public void DatasetMaterialization_SymbolMismatch_IsDenied()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId + 999,
            SymbolName = "WRONGUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.SymbolMismatch, ex.DenialCode);
    }

    [Fact]
    public void DatasetMaterialization_TimeframeMismatch_IsDenied()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "15m",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.TimeframeMismatch, ex.DenialCode);
    }

    [Fact]
    public void TenThousandCandleMaterialization_ProducesExactlyThreeAccessEvents()
    {
        const int largeEval = 10_000;
        const int warm = 100;
        var evalEnd = EvalStart.AddHours(largeEval);
        var list = new List<Candle>(warm + largeEval);
        var cursor = EvalStart.AddHours(-warm);
        for (var i = 0; i < warm + largeEval; i++)
        {
            list.Add(BuildCandle(cursor.AddHours(i), 100m + i));
        }

        var warmup = list.Where(c => c.OpenTimeUtc < EvalStart).TakeLast(warm).ToList();
        var evaluation = list.Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEnd).ToList();
        Assert.Equal(largeEval, evaluation.Count);

        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", warm, warmup.Count, evaluation.Count,
            ValidationWarmupStatus.Complete, EvalStart, evalEnd, evalEnd,
            StrategyExecutionRequirements.Version, warmup, evaluation,
            warmup.Concat(evaluation).ToList());
        var scope = new ValidationTrainingCandleScope(partition, warmup, evaluation);

        var dataset = scope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = warm,
            CallerComponent = "PerfGuard"
        });

        Assert.Equal(warm + largeEval, dataset.Candles.Count);
        Assert.Equal(3, scope.AccessLog.Count);
        Assert.DoesNotContain(scope.AccessLog, a => a.ReturnedCandleCount == 1 && a.AccessPurpose == ValidationCandleAccessPurpose.ByOpenTime);
        Assert.Equal(warm + largeEval, scope.AccessLog.Sum(a =>
            a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization ? a.ReturnedCandleCount : 0));
    }

    [Fact]
    public void TimestampGapFixture_WarmupSelectsLatestNActualCandles()
    {
        // Gaps inside warm-up: still TakeLast(N) actual bars, not duration estimate.
        var evalEnd = EvalStart.AddHours(20);
        var warmup = new List<Candle>();
        for (var i = 0; i < 60; i++)
        {
            // Skip every 3rd hour to create gaps, but keep 60 closed bars before EvalStart.
            var open = EvalStart.AddHours(-(60 - i) * 2); // sparse
            warmup.Add(BuildCandle(open, 50m + i));
        }

        warmup = warmup.OrderBy(c => c.OpenTimeUtc).ToList();
        Assert.All(warmup, c => Assert.True(c.OpenTimeUtc < EvalStart));
        var selected = warmup.TakeLast(RequiredWarmup).ToList();
        var evaluation = Enumerable.Range(0, 20).Select(i => BuildCandle(EvalStart.AddHours(i), 200m + i)).ToList();

        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", RequiredWarmup, selected.Count, evaluation.Count,
            ValidationWarmupStatus.Complete, EvalStart, evalEnd, evalEnd,
            StrategyExecutionRequirements.Version, selected, evaluation,
            selected.Concat(evaluation).ToList());
        var scope = new ValidationTrainingCandleScope(partition, selected, evaluation);
        var ds = scope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "GapFixture"
        });

        Assert.Equal(RequiredWarmup, ds.WarmupCandleCount);
        Assert.Equal(selected.Select(c => c.OpenTimeUtc), ds.Candles.Take(RequiredWarmup).Select(c => c.OpenTimeUtc));
        Assert.Equal(3, scope.AccessLog.Count);
    }

    [Fact]
    public void ExactEvaluationEndCandle_ExcludedFromMaterialization()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);
        var ds = scope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "EndExclusive"
        });

        Assert.DoesNotContain(ds.Candles, c => c.OpenTimeUtc == evalEnd);
        Assert.All(
            ds.EvaluationIndices.Select(i => ds.Candles[i]),
            c => Assert.True(c.OpenTimeUtc < evalEnd));
    }

    [Fact]
    public void BacktestDataLoader_V2_UsesExclusiveEnd_LegacyUsesInclusive()
    {
        // Pure index-filter contract check mirroring BacktestDataLoader branching.
        var from = EvalStart;
        var to = EvalStart.AddHours(3);
        var candles = new[]
        {
            BuildCandle(from.AddHours(-1), 1m),
            BuildCandle(from, 2m),
            BuildCandle(from.AddHours(1), 3m),
            BuildCandle(from.AddHours(2), 4m),
            BuildCandle(to, 5m) // exactly at exclusive end
        };

        var v2 = candles
            .Select((c, i) => (c, i))
            .Where(x => x.c.OpenTimeUtc >= from && x.c.OpenTimeUtc < to)
            .Select(x => x.i)
            .ToList();
        var legacy = candles
            .Select((c, i) => (c, i))
            .Where(x => x.c.OpenTimeUtc >= from && x.c.OpenTimeUtc <= to)
            .Select(x => x.i)
            .ToList();

        Assert.Equal(3, v2.Count);
        Assert.DoesNotContain(4, v2); // index of candle at `to`
        Assert.Contains(4, legacy);
    }

    private static ValidationTrainingCandleScope BuildScope(int? customRequiredWarmup = null)
    {
        var all = BuildSeries();
        var evalEnd = EvalStart.AddHours(EvalCount);
        var warmupSource = all.Where(c => c.OpenTimeUtc < EvalStart).ToList();
        var required = customRequiredWarmup ?? RequiredWarmup;
        var warmup = warmupSource.TakeLast(required).ToList();
        var evaluation = all.Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEnd).ToList();

        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId: 1,
            symbolId: SymbolId,
            symbolName: "TESTUSDT",
            timeframe: "1h",
            requiredWarmup: required,
            availableWarmup: warmup.Count,
            evaluationCount: evaluation.Count,
            status: required > 0 ? ValidationWarmupStatus.Complete : ValidationWarmupStatus.NotRequired,
            evalStart: EvalStart,
            evalEndExclusive: evalEnd,
            boundary: evalEnd,
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: warmup.Concat(evaluation).ToList());

        return new ValidationTrainingCandleScope(partition, warmup, evaluation);
    }

    private static List<Candle> BuildSeries(int? availableWarmup = null, int? requiredWarmup = null)
    {
        var warmupCount = availableWarmup ?? AvailableWarmup;
        var list = new List<Candle>(warmupCount + EvalCount);
        var cursor = EvalStart.AddHours(-warmupCount);
        for (var i = 0; i < warmupCount + EvalCount; i++)
        {
            var open = cursor.AddHours(i);
            list.Add(BuildCandle(open, 100m + i));
        }
        return list;
    }

    private static Candle BuildCandle(DateTime openTimeUtc, decimal price) =>
        new()
        {
            Id = (long)(openTimeUtc.Ticks % int.MaxValue),
            ExchangeId = 1,
            SymbolId = SymbolId,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = openTimeUtc,
            CloseTimeUtc = openTimeUtc.AddHours(1),
            Open = price,
            High = price + 1,
            Low = price - 1,
            Close = price + 0.5m,
            Volume = 10m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
}
