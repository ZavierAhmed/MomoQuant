using System.Reflection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationLab230BProductionSeamsTests
{
    [Fact]
    public async Task CandleAccessRecorder_FlushesOnLeakage_AndIsIdempotent()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var scope = CreateScope();

        await Assert.ThrowsAsync<ValidationDataLeakageException>(() =>
        {
            _ = scope.GetRange(
                scope.ValidationBoundaryUtc,
                scope.ValidationBoundaryUtc.AddHours(1),
                "Adversarial");
            return Task.CompletedTask;
        });

        Assert.Contains(scope.AccessLog, a => a.WasDenied);

        await recorder.FlushAsync(scope);
        Assert.Single(audits.Items);
        Assert.True(audits.Items[0].WasDenied);

        await recorder.FlushAsync(scope);
        Assert.Single(audits.Items);
    }

    [Fact]
    public async Task TrainingScopeExecution_FlushesDeniedAccess_BeforePropagatingLeakage()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);
        var experiment = new ValidationExperiment { Id = 7 };

        var thrown = await Assert.ThrowsAsync<ValidationDataLeakageException>(() =>
            execution.ExecuteTrialAsync(
                factory.Scope,
                trialNumber: 3,
                trialId: 99,
                trialBody: () =>
                {
                    _ = factory.Scope.GetByOpenTimeUtc(factory.Scope.ValidationBoundaryUtc, "LeakProbe");
                    return Task.CompletedTask;
                }));

        Assert.Contains("ValidationDataLeakageDetected", thrown.Message, StringComparison.Ordinal);
        Assert.Single(audits.Items);
        Assert.True(audits.Items[0].WasDenied);
        Assert.Equal(3, audits.Items[0].TrialNumber);
        Assert.Equal(99, audits.Items[0].TrialId);
        _ = experiment;
    }

    [Fact]
    public void SegmentResultWriter_Interface_ExposesBuildAndPersist()
    {
        var method = typeof(IValidationSegmentResultWriter).GetMethod(
            nameof(IValidationSegmentResultWriter.BuildAndPersistSegmentResultsAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(ValidationExperiment));
        Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(ValidationSegmentType));
    }

    [Fact]
    public void PathMetrics_WarningBearingIncluded_DoesNotAlterExclusionCounts()
    {
        var risk = new ValidationRiskBasisService();
        var includedWarned = new ValidationPathTradeMetricInput
        {
            ValidationLayer = ValidationLayerType.RawStrategy,
            CandidateFingerprint = "W",
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 1m,
            GrossPnl = 2m,
            NetPnl = 1.9m,
            TotalTransactionCosts = 0.1m,
            Outcome = "Winner",
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included,
            MetricWarningCodes = [ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch],
            ReconciliationStatus = ValidationMetricReconciliationStatus.Mismatched
        };
        var includedClean = new ValidationPathTradeMetricInput
        {
            ValidationLayer = ValidationLayerType.RawStrategy,
            CandidateFingerprint = "C",
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 1m,
            GrossPnl = 1m,
            NetPnl = 0.9m,
            TotalTransactionCosts = 0.1m,
            Outcome = "Winner",
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included
        };
        var excluded = new ValidationPathTradeMetricInput
        {
            ValidationLayer = ValidationLayerType.RawStrategy,
            CandidateFingerprint = "X",
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Excluded,
            MetricExclusionReason = "MissingPathQuantity"
        };

        var metrics = ValidationMetricsContract.FromPathTradesV13(
            [includedWarned, includedClean, excluded],
            candleCount: 100,
            candidateCount: 3,
            boundaryCensored: 0,
            ValidationLayerType.RawStrategy,
            risk);

        Assert.Equal(ValidationMetricsContract.VersionV131, metrics.MetricsVersion);
        Assert.Equal(1, metrics.MetricWarningBearingIncludedTradeCount);
        Assert.Contains(
            ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
            metrics.MetricWarningCodes!);
        Assert.Equal(2, metrics.NetExpectancyIncludedTradeCount);
        Assert.Equal(1, metrics.NetExpectancyExcludedTradeCount);
        Assert.Null(includedWarned.MetricExclusionReason);
    }

    [Fact]
    public void ApplicationTypes_TakingIUnscopedCandleReader_AreOnlyScopeFactory()
    {
        var applicationAssembly = typeof(IValidationTrainingCandleScopeFactory).Assembly;
        var unscoped = typeof(IUnscopedCandleReader);
        var offenders = applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Select(c => (Type: t, Ctor: c)))
            .Where(x => x.Ctor.GetParameters().Any(p => p.ParameterType == unscoped))
            .Select(x => x.Type)
            .Distinct()
            .ToList();

        Assert.Equal(
            [typeof(ValidationTrainingCandleScopeFactory)],
            offenders);
    }

    private static ValidationTrainingCandleScope CreateScope()
    {
        var boundary = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var start = boundary.AddDays(-2);
        var candles = new List<Candle>
        {
            new()
            {
                OpenTimeUtc = start,
                CloseTimeUtc = start.AddHours(1),
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            },
            new()
            {
                OpenTimeUtc = boundary.AddHours(-1),
                CloseTimeUtc = boundary,
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            }
        };
        return new ValidationTrainingCandleScope(42, start, boundary, candles);
    }

    private sealed class FakeCandleAccessAuditRepository : IValidationCandleAccessAuditRepository
    {
        public List<ValidationCandleAccessAudit> Items { get; } = [];

        public Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            Items.AddRange(audits);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(
                Items.Where(a => a.ValidationExperimentId == experimentId).ToList());
    }

    private sealed class FakeScopeFactory : IValidationTrainingCandleScopeFactory
    {
        public ValidationTrainingCandleScope Scope { get; } = CreateScope();

        public Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
            ValidationExperiment experiment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IValidationTrainingCandleScope>(Scope);
    }
}
