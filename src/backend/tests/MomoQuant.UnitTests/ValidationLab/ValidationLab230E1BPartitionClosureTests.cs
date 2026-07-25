using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using System.Reflection;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E1B â€” fail-closed partition construction, evidence counts, denied partitions,
/// legacy materialization closure, and versioned evaluation-end semantics.
/// </summary>
public sealed class ValidationLab230E1BPartitionClosureTests
{
    private static readonly DateTime EvalStart = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int RequiredWarmup = 50;
    private const int EvalCount = 200;
    private const long SymbolId = 99;

    [Fact]
    public void Constructor_DuplicateTimestamp_IsRejected()
    {
        var ts = EvalStart.AddHours(-10);
        var candles = new List<Candle>
        {
            BuildCandle(ts, 100m),
            BuildCandle(ts, 101m)
        };

        var warmup = candles;
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
            warmup: new[] { BuildCandle(EvalStart.AddHours(-10), 100m) },
            evaluation: evaluation,
            combined: new[] { BuildCandle(EvalStart.AddHours(-10), 100m) });

        // Corrupt: duplicate timestamps but metadata claims count 2 via empty fingerprint path â€”
        // rebuild partition with duplicate list for count metadata then pass duplicates.
        partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", 2, 2, 0, ValidationWarmupStatus.Complete,
            EvalStart, EvalStart.AddHours(EvalCount), EvalStart.AddHours(EvalCount),
            StrategyExecutionRequirements.Version,
            new[] { BuildCandle(EvalStart.AddHours(-11), 99m), BuildCandle(EvalStart.AddHours(-10), 100m) },
            evaluation,
            new[] { BuildCandle(EvalStart.AddHours(-11), 99m), BuildCandle(EvalStart.AddHours(-10), 100m) });

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionException.ErrorCodeValue, ex.ErrorCode);
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.DuplicateOpenTime, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_WarmupCandleAtEvaluationStart_IsRejected()
    {
        var warmup = new List<Candle>
        {
            BuildCandle(EvalStart.AddHours(-10), 100m),
            BuildCandle(EvalStart, 101m)
        };
        var evaluation = new List<Candle> { BuildCandle(EvalStart.AddHours(1), 102m) };
        var partition = BuildValidPartitionMetadata(1, 1);

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.WarmupCandleOutsidePartition, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_EvaluationCandleBeforeStart_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-10), 100m) };
        var evaluation = new List<Candle>
        {
            BuildCandle(EvalStart.AddHours(-1), 101m),
            BuildCandle(EvalStart.AddHours(1), 102m)
        };
        var partition = BuildValidPartitionMetadata(1, 2);

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.EvaluationCandleOutsidePartition, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_EvaluationCandleAtEndExclusive_IsRejected()
    {
        var evalEnd = EvalStart.AddHours(2);
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-1), 100m) };
        var validEval = new List<Candle> { BuildCandle(EvalStart, 101m) };
        var invalidEval = new List<Candle> { BuildCandle(EvalStart, 101m), BuildCandle(evalEnd, 102m) };
        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", 1, 1, 1, ValidationWarmupStatus.Complete,
            EvalStart, evalEnd, evalEnd.AddHours(1), StrategyExecutionRequirements.Version,
            warmup, validEval, warmup.Concat(validEval).ToList());

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, invalidEval));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.EvaluationCandleOutsidePartition, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_CandleAtValidationBoundary_IsRejected()
    {
        var boundary = EvalStart.AddHours(10);
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-10), 100m) };
        var validEvaluation = Enumerable.Range(0, 9)
            .Select(i => BuildCandle(EvalStart.AddHours(i), 101m + i))
            .ToList();
        var invalidEvaluation = validEvaluation.Concat(new[] { BuildCandle(boundary, 110m) }).ToList();
        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", warmup.Count, warmup.Count, validEvaluation.Count,
            ValidationWarmupStatus.Complete, EvalStart, boundary, boundary,
            StrategyExecutionRequirements.Version, warmup, validEvaluation,
            warmup.Concat(validEvaluation).ToList());

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, invalidEvaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.ValidationBoundaryCandlePresent, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_NonMonotonicWarmup_IsRejected()
    {
        var warmup = new List<Candle>
        {
            BuildCandle(EvalStart.AddHours(-5), 102m),
            BuildCandle(EvalStart.AddHours(-10), 100m),
            BuildCandle(EvalStart.AddHours(-3), 103m)
        };
        var evaluation = new List<Candle> { BuildCandle(EvalStart.AddHours(1), 104m) };
        var partition = BuildValidPartitionMetadata(warmup.Count, evaluation.Count);

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.NonMonotonicOpenTime, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_NonMonotonicEvaluation_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-1), 100m) };
        var evaluation = new List<Candle>
        {
            BuildCandle(EvalStart.AddHours(1), 102m),
            BuildCandle(EvalStart, 101m)
        };
        var evalEnd = EvalStart.AddHours(2);
        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", 1, 1, evaluation.Count, ValidationWarmupStatus.Complete,
            EvalStart, evalEnd, evalEnd, StrategyExecutionRequirements.Version,
            warmup, evaluation, warmup.Concat(evaluation).ToList());

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.NonMonotonicOpenTime, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_WrongSymbol_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandleWithSymbol(EvalStart.AddHours(-10), 100m, SymbolId + 999) };
        var evaluation = new List<Candle> { BuildCandle(EvalStart.AddHours(1), 102m) };
        var partition = BuildValidPartitionMetadata(1, 1);

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.SymbolMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_WrongTimeframe_IsRejected()
    {
        var wrongTf = BuildCandle(EvalStart.AddHours(-1), 100m);
        wrongTf.Timeframe = Timeframe.M15;
        var warmup = new List<Candle> { wrongTf };
        var evaluation = new List<Candle> { BuildCandle(EvalStart, 101m) };
        var partition = BuildValidPartitionMetadata(1, 1);

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.TimeframeMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_OpenCandle_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-1), 100m) };
        warmup[0].IsClosed = false;
        var evaluation = new List<Candle> { BuildCandle(EvalStart, 101m) };
        var partition = BuildValidPartitionMetadata(1, 1);

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.OpenCandleNotAllowed, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_WarmupCountMetadataMismatch_IsRejected()
    {
        var warmup = new List<Candle>
        {
            BuildCandle(EvalStart.AddHours(-10), 100m),
            BuildCandle(EvalStart.AddHours(-9), 101m)
        };
        var evaluation = Enumerable.Range(0, 5).Select(i => BuildCandle(EvalStart.AddHours(i), 102m + i)).ToList();
        var fiveWarmup = Enumerable.Range(0, 5)
            .Select(i => BuildCandle(EvalStart.AddHours(-5 + i), 100m + i))
            .ToList();
        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", 5, 5, evaluation.Count, ValidationWarmupStatus.Complete,
            EvalStart, EvalStart.AddHours(5), EvalStart.AddHours(5), StrategyExecutionRequirements.Version,
            fiveWarmup, evaluation, fiveWarmup.Concat(evaluation).ToList());

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.WarmupCountMetadataMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_EvaluationCountMetadataMismatch_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-1), 100m) };
        var evaluation = new List<Candle> { BuildCandle(EvalStart, 101m) };
        var twoEval = new List<Candle> { BuildCandle(EvalStart, 101m), BuildCandle(EvalStart.AddHours(1), 102m) };
        var partition = ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", 1, 1, 2, ValidationWarmupStatus.Complete,
            EvalStart, EvalStart.AddHours(2), EvalStart.AddHours(2), StrategyExecutionRequirements.Version,
            warmup, twoEval, warmup.Concat(twoEval).ToList());

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(partition, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.EvaluationCountMetadataMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_TotalCountMetadataMismatch_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-1), 100m) };
        var evaluation = new List<Candle> { BuildCandle(EvalStart, 101m) };
        var partition = BuildValidPartitionMetadata(1, 1);
        var corrupt = ClonePartition(partition, p =>
        {
            p.TotalCandleCount = 99;
        });

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(corrupt, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.TotalCountMetadataMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_IndexMetadataMismatch_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-1), 100m) };
        var evaluation = new List<Candle> { BuildCandle(EvalStart, 101m) };
        var partition = BuildValidPartitionMetadata(1, 1);
        var corrupt = ClonePartition(partition, p =>
        {
            p.EvaluationStartIndex = 7;
        });

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(corrupt, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.PartitionIndexMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void Constructor_FingerprintMismatch_IsRejected()
    {
        var warmup = new List<Candle> { BuildCandle(EvalStart.AddHours(-10), 100m) };
        var evaluation = new List<Candle>
        {
            BuildCandle(EvalStart, 102m),
            BuildCandle(EvalStart.AddHours(1), 103m)
        };
        var correct = BuildValidPartitionMetadata(warmup.Count, evaluation.Count);
        var corrupt = ClonePartition(correct, p =>
        {
            p.WarmupContentFingerprint = "INVALID_FINGERPRINT_0000";
        });

        var ex = Assert.Throws<ValidationCandlePartitionConstructionException>(() =>
            new ValidationTrainingCandleScope(corrupt, warmup, evaluation));
        Assert.Equal(ValidationCandlePartitionConstructionFailureReasons.WarmupFingerprintMismatch, ex.FailureReasonCode);
    }

    [Fact]
    public void DatasetMaterialization_RequestedCountEqualsTotalCount()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);
        var dataset = scope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        });
        Assert.NotNull(dataset);
        var datasetEvent = scope.AccessLog.First(a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization);
        Assert.Equal(RequiredWarmup + EvalCount, datasetEvent.RequestedCandleCount);
        Assert.Equal(RequiredWarmup + EvalCount, datasetEvent.ReturnedCandleCount);
    }

    [Fact]
    public void EvaluationLoad_RequestedCountEqualsEvaluationCount()
    {
        var scope = BuildScope();
        var evalEnd = EvalStart.AddHours(EvalCount);
        scope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
        {
            SymbolId = SymbolId,
            SymbolName = "TESTUSDT",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "TestCaller"
        });
        var evalEvent = scope.AccessLog.First(a => a.AccessPurpose == ValidationCandleAccessPurpose.EvaluationLoad);
        Assert.Equal(EvalCount, evalEvent.RequestedCandleCount);
        Assert.Equal(EvalCount, evalEvent.ReturnedCandleCount);
    }

    [Fact]
    public void DeniedWarmupEvent_HasWarmupPartition()
    {
        var scope = BuildScope();
        Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.GetWarmupBefore(new ValidationWarmupAccessRequest
            {
                BeforeOpenTimeUtc = EvalStart.AddHours(10),
                Count = RequiredWarmup,
                Purpose = ValidationCandleAccessPurpose.WarmupBefore,
                CallerComponent = "TestCaller"
            }));
        var deniedEvent = scope.AccessLog.First(a => a.WasDenied);
        Assert.Equal("Warmup", deniedEvent.DatasetPartition);
    }

    [Fact]
    public void DeniedEvaluationEvent_HasEvaluationPartition()
    {
        var scope = BuildScope();
        Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.GetEvaluationRange(new ValidationEvaluationAccessRequest
            {
                FromUtc = EvalStart.AddHours(-5),
                ToExclusiveUtc = EvalStart.AddHours(1),
                AllowPartial = false,
                Purpose = ValidationCandleAccessPurpose.EvaluationRange,
                CallerComponent = "TestCaller"
            }));
        var deniedEvent = scope.AccessLog.First(a => a.WasDenied);
        Assert.Equal("Evaluation", deniedEvent.DatasetPartition);
    }

    [Fact]
    public void DeniedMaterializationEvent_HasCombinedPartition()
    {
        var scope = BuildScope();
        Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            scope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
            {
                SymbolId = SymbolId,
                SymbolName = "TESTUSDT",
                Timeframe = "1h",
                EvaluationFromUtc = EvalStart,
                EvaluationToExclusiveUtc = EvalStart.AddHours(EvalCount),
                WarmupCandleCount = RequiredWarmup + 1,
                CallerComponent = "TestCaller"
            }));
        var deniedEvent = scope.AccessLog.First(a => a.WasDenied);
        Assert.Equal("Combined", deniedEvent.DatasetPartition);
    }

    [Fact]
    public void PublicScopeInterface_HasNoLegacyMaterializationOverload()
    {
        var methods = typeof(IValidationSegmentCandleSource).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var legacyOverload = methods.FirstOrDefault(m =>
            m.Name == "CreateStrategyLabDataset" &&
            m.GetParameters().Length >= 2 &&
            m.GetParameters()[0].ParameterType.Name.Contains("StrategyLabRun", StringComparison.Ordinal));
        Assert.Null(legacyOverload);

        var productionAssembly = typeof(ValidationTrainingCandleScope).Assembly;
        var callers = productionAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetParameters().Any(p =>
                p.ParameterType.Name == "StrategyLabRun" &&
                m.Name.Contains("CreateStrategyLabDataset", StringComparison.Ordinal)))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .ToList();
        Assert.Empty(callers);
    }

    [Fact]
    public void V2RunnerEvaluationFilter_IsExclusive()
    {
        var open = EvalStart.AddHours(10);
        var from = EvalStart;
        var to = EvalStart.AddHours(10);
        Assert.False(StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(
            StrategyLabCandleLoadContractVersions.ExactExclusiveV2, open, from, to));
        Assert.True(StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(
            StrategyLabCandleLoadContractVersions.LegacyV1, open, from, to));
    }

    [Fact]
    public void LegacyRunnerEvaluationFilter_PreservesVersionedBehavior()
    {
        var open = EvalStart.AddHours(10);
        var from = EvalStart;
        var to = EvalStart.AddHours(10);
        Assert.True(StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(null, open, from, to));
        Assert.True(StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(
            StrategyLabCandleLoadContractVersions.LegacyV1, open, from, to));
        Assert.True(StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(
            StrategyLabCandleLoadContractVersions.ExactExclusiveV2, open.AddHours(-1), from, to));
    }

    [Fact]
    public void UnknownCandleLoadVersion_FailsClosed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(
                "StrategyLabCandleLoad/v9-Unknown",
                EvalStart,
                EvalStart,
                EvalStart.AddHours(1)));
        Assert.Contains("Unknown CandleLoadContractVersion", ex.Message, StringComparison.Ordinal);
    }

    private static ValidationTrainingCandleScope BuildScope()
    {
        var warmup = Enumerable.Range(0, RequiredWarmup)
            .Select(i => BuildCandle(EvalStart.AddHours(-RequiredWarmup + i), 100m + i))
            .ToList();
        var evaluation = Enumerable.Range(0, EvalCount)
            .Select(i => BuildCandle(EvalStart.AddHours(i), 200m + i))
            .ToList();
        return new ValidationTrainingCandleScope(BuildValidPartitionMetadata(warmup.Count, evaluation.Count), warmup, evaluation);
    }

    private static ValidationCandlePartitionMetadata BuildValidPartitionMetadata(int warmupCount, int evalCount)
    {
        var evalEnd = EvalStart.AddHours(evalCount);
        var warmup = Enumerable.Range(0, warmupCount)
            .Select(i => BuildCandle(EvalStart.AddHours(-warmupCount + i), 100m + i))
            .ToList();
        var evaluation = Enumerable.Range(0, evalCount)
            .Select(i => BuildCandle(EvalStart.AddHours(i), 200m + i))
            .ToList();

        return ValidationTrainingCandleScope.BuildPartition(
            1, SymbolId, "TESTUSDT", "1h", warmupCount, warmupCount, evalCount,
            warmupCount > 0 ? ValidationWarmupStatus.Complete : ValidationWarmupStatus.NotRequired,
            EvalStart, evalEnd, evalEnd, StrategyExecutionRequirements.Version,
            warmup, evaluation, warmup.Concat(evaluation).ToList());
    }

    private static ValidationCandlePartitionMetadata ClonePartition(
        ValidationCandlePartitionMetadata source,
        Action<ValidationCandlePartitionMetadata> mutate)
    {
        var copy = new ValidationCandlePartitionMetadata
        {
            ValidationExperimentId = source.ValidationExperimentId,
            RequiredWarmupCandleCount = source.RequiredWarmupCandleCount,
            AvailableWarmupCandleCount = source.AvailableWarmupCandleCount,
            EvaluationCandleCount = source.EvaluationCandleCount,
            TotalCandleCount = source.TotalCandleCount,
            WarmupStatus = source.WarmupStatus,
            TrainingEvaluationStartUtc = source.TrainingEvaluationStartUtc,
            TrainingEvaluationEndExclusiveUtc = source.TrainingEvaluationEndExclusiveUtc,
            ValidationBoundaryUtc = source.ValidationBoundaryUtc,
            SymbolId = source.SymbolId,
            SymbolName = source.SymbolName,
            Timeframe = source.Timeframe,
            RequirementsVersion = source.RequirementsVersion,
            EvaluationStartIndex = source.EvaluationStartIndex,
            WarmupContentFingerprint = source.WarmupContentFingerprint,
            EvaluationContentFingerprint = source.EvaluationContentFingerprint,
            CombinedContentFingerprint = source.CombinedContentFingerprint,
            WarmupStartUtc = source.WarmupStartUtc,
            WarmupEndExclusiveUtc = source.WarmupEndExclusiveUtc,
            WarmupStartIndex = source.WarmupStartIndex,
            WarmupEndExclusiveIndex = source.WarmupEndExclusiveIndex,
            EvaluationEndExclusiveIndex = source.EvaluationEndExclusiveIndex,
            WarmupCandleCount = source.WarmupCandleCount,
            PartitionContractVersion = source.PartitionContractVersion
        };
        mutate(copy);
        return copy;
    }

    private static Candle BuildCandle(DateTime openTimeUtc, decimal price) =>
        BuildCandleWithSymbol(openTimeUtc, price, SymbolId);

    private static Candle BuildCandleWithSymbol(DateTime openTimeUtc, decimal price, long symbolId) =>
        new()
        {
            Id = (long)(openTimeUtc.Ticks % int.MaxValue),
            ExchangeId = 1,
            SymbolId = symbolId,
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
