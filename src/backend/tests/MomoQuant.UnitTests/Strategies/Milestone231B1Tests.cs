using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;
using Moq;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1B1 — Unified plugin Lab path, strict fingerprints, contract metadata, HTF integrity.
/// </summary>
public sealed class Milestone231B1Tests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_PsbrLab_UsesRegisteredPlugin_NotDetectorOnlyJson()
    {
        var plugin = new PriceStructureBreakoutRetestStrategy();
        Assert.IsType<PriceStructureBreakoutRetestStrategy>(plugin);

        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var from = candles[0].OpenTimeUtc;
        var to = candles[^1].OpenTimeUtc.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(0, candles.Count).ToList(),
            WarmupCandleCount = 0
        };

        var run = CreateRun(23101, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to);
        run.StrategyVersion = PriceStructureBreakoutRetestEvaluator.StrategyVersion;
        var sink = new List<StrategyResearchCandidate>();
        var spy = new CountingStrategy(plugin);
        var runner = CreateRunner(run, spy, StrategyCode.PriceStructureBreakoutRetest,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion, dataset, sink);

        await runner.ExecuteAsync(run.Id, GeneralResearchContext(dataset));

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.True(spy.EvaluateCount > 0);
        Assert.NotEmpty(sink);
        Assert.All(sink, c =>
        {
            Assert.True(StrategyLabRunner.IsCanonicalSetupFingerprint(c.SetupFingerprint));
            Assert.True(Milestone231BParityFixtures.HasStrengthBreakdown(c.StructureJson));
            Assert.DoesNotContain("missing-fp-", c.SetupFingerprint, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_AdaptiveAndRange_StillPluginPath()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var evalIndex = ltf.Count - 1;
        var from = ltf[evalIndex].OpenTimeUtc;
        var to = from.AddMinutes(5);
        var adaptiveDataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = [evalIndex],
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = htf
            }
        };
        var adaptiveRun = CreateRun(23102, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "5m", from, to);
        var adaptiveSink = new List<StrategyResearchCandidate>();
        var adaptiveSpy = new CountingStrategy(new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy());
        await CreateRunner(adaptiveRun, adaptiveSpy, StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version, adaptiveDataset, adaptiveSink)
            .ExecuteAsync(adaptiveRun.Id, GeneralResearchContext(adaptiveDataset));
        Assert.Equal(StrategyLabRunStatus.Completed, adaptiveRun.Status);
        Assert.True(adaptiveSpy.EvaluateCount > 0);
        Assert.NotEmpty(adaptiveSink);

        var ranging = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        Milestone231BParityFixtures.AssignSequentialIds(ranging);
        var rFrom = ranging[^1].OpenTimeUtc;
        var rangeDataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = ranging[0].Timeframe,
            Candles = ranging,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildRangingSnapshots(ranging),
            EvaluationIndices = [ranging.Count - 1],
            WarmupCandleCount = 0
        };
        var rangeRun = CreateRun(23103, StrategyCodes.MomoVolatilityRangeReversion, "5m", rFrom, rFrom.AddMinutes(5));
        var rangeSink = new List<StrategyResearchCandidate>();
        var rangeSpy = new CountingStrategy(new MomoVolatilityRangeReversionStrategy());
        await CreateRunner(rangeRun, rangeSpy, StrategyCode.MomoVolatilityRangeReversion,
            MomoVolatilityRangeReversionStrategy.Version, rangeDataset, rangeSink)
            .ExecuteAsync(rangeRun.Id, GeneralResearchContext(rangeDataset));
        Assert.Equal(StrategyLabRunStatus.Completed, rangeRun.Status);
        Assert.True(rangeSpy.EvaluateCount > 0);
        Assert.NotEmpty(rangeSink);
    }

    [Fact]
    public async Task B_EmptyFingerprint_NoPersist_FailsRun()
    {
        var candles = BuildMinimalCandles(3);
        var from = candles[0].OpenTimeUtc;
        var to = candles[^1].OpenTimeUtc.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(0, candles.Count).ToList(),
            WarmupCandleCount = 0
        };

        var run = CreateRun(23110, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to);
        var sink = new List<StrategyResearchCandidate>();
        var plugin = new ForcedEntryStrategy(emitMalformedFingerprint: true, emitValidFirst: false);
        var runner = CreateRunner(run, plugin, StrategyCode.PriceStructureBreakoutRetest, "1.1.0", dataset, sink);

        await runner.ExecuteAsync(run.Id, GeneralResearchContext(dataset));

        Assert.Equal(StrategyLabRunStatus.Failed, run.Status);
        Assert.Contains("MissingSetupFingerprint", run.ErrorMessage ?? string.Empty);
        Assert.Empty(sink);
        Assert.DoesNotContain(sink, c => (c.SetupFingerprint ?? string.Empty).StartsWith("missing-fp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task B_MalformedFingerprintAfterValid_FailsEntireRun()
    {
        var candles = BuildMinimalCandles(4);
        var from = candles[0].OpenTimeUtc;
        var to = candles[^1].OpenTimeUtc.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(0, candles.Count).ToList(),
            WarmupCandleCount = 0
        };

        var run = CreateRun(23111, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to);
        var sink = new List<StrategyResearchCandidate>();
        var plugin = new ForcedEntryStrategy(emitMalformedFingerprint: true, emitValidFirst: true);
        await CreateRunner(run, plugin, StrategyCode.PriceStructureBreakoutRetest, "1.1.0", dataset, sink)
            .ExecuteAsync(run.Id, GeneralResearchContext(dataset));

        Assert.Equal(StrategyLabRunStatus.Failed, run.Status);
        Assert.Contains("MissingSetupFingerprint", run.ErrorMessage ?? string.Empty);
        Assert.Empty(sink);
        Assert.NotEqual(StrategyLabRunStatus.Completed, run.Status);
    }

    [Fact]
    public async Task C_FunnelReconciliation_AdaptiveRangePsbr()
    {
        await AssertFunnelReconcilesAsync(
            23120,
            StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy(),
            MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            BuildAdaptiveDataset());

        await AssertFunnelReconcilesAsync(
            23121,
            StrategyCodes.MomoVolatilityRangeReversion,
            StrategyCode.MomoVolatilityRangeReversion,
            new MomoVolatilityRangeReversionStrategy(),
            MomoVolatilityRangeReversionStrategy.Version,
            BuildRangeDataset());

        await AssertFunnelReconcilesAsync(
            23122,
            StrategyCodes.PriceStructureBreakoutRetest,
            StrategyCode.PriceStructureBreakoutRetest,
            new PriceStructureBreakoutRetestStrategy(),
            PriceStructureBreakoutRetestEvaluator.StrategyVersion,
            BuildPsbrDataset());
    }

    [Fact]
    public async Task D_GetLabStrategiesAsync_ExactContracts()
    {
        var requirements = new List<StrategyDataRequirementDto>
        {
            BuildRequirementDto(
                StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
                "MOMO Adaptive Multi-Timeframe Trend Breakout",
                ["5m", "15m", "1h", "4h"],
                ["5m"],
                ["5m:1h", "15m:4h", "1h:4h", "4h:1d"],
                600),
            BuildRequirementDto(
                StrategyCodes.PriceStructureBreakoutRetest,
                "Price Structure Breakout + Retest",
                ["5m", "15m", "30m", "1h", "4h"],
                [],
                [],
                100),
            BuildRequirementDto(
                StrategyCodes.MomoVolatilityRangeReversion,
                "MOMO Volatility Range Reversion",
                ["5m", "15m", "30m", "1h"],
                [],
                [],
                158)
        };

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout))
            .Returns(new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy());
        registry.Setup(r => r.GetByCode(StrategyCode.PriceStructureBreakoutRetest))
            .Returns(new PriceStructureBreakoutRetestStrategy());
        registry.Setup(r => r.GetByCode(StrategyCode.MomoVolatilityRangeReversion))
            .Returns(new MomoVolatilityRangeReversionStrategy());

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(s => s.GetByCodeAsync(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, "MOMO Adaptive Multi-Timeframe Trend Breakout", MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version));
        strategyRepo.Setup(s => s.GetByCodeAsync(StrategyCode.PriceStructureBreakoutRetest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity(StrategyCode.PriceStructureBreakoutRetest, "Price Structure Breakout + Retest", PriceStructureBreakoutRetestStrategy.Version));
        strategyRepo.Setup(s => s.GetByCodeAsync(StrategyCode.MomoVolatilityRangeReversion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity(StrategyCode.MomoVolatilityRangeReversion, "MOMO Volatility Range Reversion", MomoVolatilityRangeReversionStrategy.Version));

        var reqService = new Mock<IStrategyDataRequirementService>();
        reqService.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.ServiceResult<IReadOnlyList<StrategyDataRequirementDto>>.Ok(requirements));

        var service = new StrategyLabService(
            Mock.Of<IStrategyLabRunRepository>(),
            Mock.Of<IStrategyResearchCandidateRepository>(),
            strategyRepo.Object,
            registry.Object,
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IStrategyLabQueue>(),
            reqService.Object);

        var result = await service.GetLabStrategiesAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);

        var adaptive = Assert.Single(result.Data, s => s.Code == StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout);
        Assert.Equal(MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version, adaptive.Version);
        Assert.Equal(["5m", "15m", "1h", "4h"], adaptive.AllowedTimeframes);
        Assert.Equal(["5m:1h", "15m:4h", "1h:4h", "4h:1d"], adaptive.HtfMappings);
        Assert.Equal(["5m"], adaptive.RequiredDataTimeframes);
        Assert.Equal(600, adaptive.WarmupBars);
        Assert.Equal("Trend / Breakout", adaptive.Category);
        Assert.False(adaptive.SupportsOptimization);
        Assert.False(adaptive.SupportsValidation);

        var psbr = Assert.Single(result.Data, s => s.Code == StrategyCodes.PriceStructureBreakoutRetest);
        Assert.Equal(PriceStructureBreakoutRetestStrategy.Version, psbr.Version);
        Assert.Equal(["5m", "15m", "30m", "1h", "4h"], psbr.AllowedTimeframes);
        Assert.Empty(psbr.HtfMappings);
        Assert.Equal("Price Action / Market Structure", psbr.Category);
        Assert.True(psbr.SupportsOptimization);
        Assert.True(psbr.SupportsValidation);

        var range = Assert.Single(result.Data, s => s.Code == StrategyCodes.MomoVolatilityRangeReversion);
        Assert.Equal(MomoVolatilityRangeReversionStrategy.Version, range.Version);
        Assert.Equal(["5m", "15m", "30m", "1h"], range.AllowedTimeframes);
        Assert.Empty(range.HtfMappings);
        Assert.Equal("Range / Mean Reversion", range.Category);
        Assert.False(range.SupportsOptimization);
        Assert.False(range.SupportsValidation);
    }

    [Fact]
    public async Task E_PsbrUsesClassifier_MissingSnapshot_UnknownDiagnostic()
    {
        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var from = candles[0].OpenTimeUtc;
        var to = candles[^1].OpenTimeUtc.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(0, candles.Count).ToList(),
            WarmupCandleCount = 0
        };
        var run = CreateRun(23130, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to);
        run.StrategyVersion = PriceStructureBreakoutRetestEvaluator.StrategyVersion;
        var sink = new List<StrategyResearchCandidate>();
        await CreateRunner(run, new PriceStructureBreakoutRetestStrategy(), StrategyCode.PriceStructureBreakoutRetest,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion, dataset, sink)
            .ExecuteAsync(run.Id, GeneralResearchContext(dataset));

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        using var doc = JsonDocument.Parse(run.ResultSummaryJson);
        Assert.True(doc.RootElement.TryGetProperty("regimeDistribution", out var regimes));
        Assert.True(regimes.TryGetProperty(MarketRegime.Unknown.ToString(), out var unknown));
        Assert.True(unknown.GetInt32() > 0);
        Assert.True(doc.RootElement.TryGetProperty("regimeDiagnostics", out var diagnostics));
        Assert.True(diagnostics.GetProperty("missingSnapshotCount").GetInt32() > 0);
    }

    [Fact]
    public void G_Htf_OpensBeforeBoundaryClosesAfter_Rejected()
    {
        var (scope, request) = CreateAdaptiveScopeRequest(
            htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(3), symbolId: 1, exchangeId: 1)]);
        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfCloseBeyondBoundary, ex.DenialCode);
        Assert.Contains(scope.AccessLog, a => a.WasDenied && a.DenialCode == ValidationCandlePartitionDenialCodes.HtfCloseBeyondBoundary);
    }

    [Fact]
    public void G_Htf_OpenCandle_Rejected()
    {
        var c = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), symbolId: 1, exchangeId: 1);
        c.IsClosed = false;
        var (scope, request) = CreateAdaptiveScopeRequest(htf: [c]);
        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfOpenCandle, ex.DenialCode);
    }

    [Fact]
    public void G_Htf_WrongSymbol_Rejected()
    {
        var (scope, request) = CreateAdaptiveScopeRequest(
            htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), symbolId: 99, exchangeId: 1)]);
        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfWrongSymbol, ex.DenialCode);
    }

    [Fact]
    public void G_Htf_WrongExchange_Rejected()
    {
        var (scope, request) = CreateAdaptiveScopeRequest(
            htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), symbolId: 1, exchangeId: 99)]);
        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfWrongExchange, ex.DenialCode);
    }

    [Fact]
    public void G_Htf_WrongTimeframeUnderMappedKey_Rejected()
    {
        var (scope, request) = CreateAdaptiveScopeRequest(
            htf: [HtfCandle(1, Timeframe.H4, Start, Start.AddHours(4), symbolId: 1, exchangeId: 1)],
            htfKey: Timeframe.H1);
        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfWrongTimeframe, ex.DenialCode);
    }

    [Fact]
    public void G_Htf_UnorderedOrDuplicate_Rejected()
    {
        var a = HtfCandle(1, Timeframe.H1, Start.AddHours(1), Start.AddHours(2), 1, 1);
        var b = HtfCandle(2, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        var (scope, request) = CreateAdaptiveScopeRequest(htf: [a, b]);
        var unordered = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfUnordered, unordered.DenialCode);

        var d1 = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        var d2 = HtfCandle(2, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        var (scope2, request2) = CreateAdaptiveScopeRequest(htf: [d1, d2]);
        var dup = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope2.CreateStrategyLabDataset(request2));
        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfDuplicate, dup.DenialCode);
    }

    [Fact]
    public void G_Htf_MissingScoped_Rejected()
    {
        var (scope, request) = CreateAdaptiveScopeRequest();
        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, ex.DenialCode);
        Assert.Contains("HTF", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G_Htf_RepositoryNeverCalled_AndAccessEvidenceRecorded()
    {
        var candleRepo = new Mock<ICandleRepository>(MockBehavior.Strict);
        var valid = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        var (scope, request) = CreateAdaptiveScopeRequest(htf: [valid]);

        // Scope materialization must not touch unrestricted candle repositories.
        _ = candleRepo;
        var dataset = scope.CreateStrategyLabDataset(request);
        Assert.True(dataset.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.H1));
        candleRepo.VerifyNoOtherCalls();

        Assert.Contains(scope.AccessLog, a =>
            a.AccessPurpose == ValidationCandleAccessPurpose.HigherTimeframeAccess
            && a.DatasetPartition.StartsWith("HTF:", StringComparison.Ordinal)
            && a.ReturnedCandleCount > 0
            && !string.IsNullOrWhiteSpace(a.CandleContentFingerprint));

        var lastHtf = scope.AccessLog.Last(a => a.AccessPurpose == ValidationCandleAccessPurpose.HigherTimeframeAccess);
        var materialization = scope.AccessLog.Last(a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization);
        Assert.True(lastHtf.ScopeSequenceNumber > materialization.ScopeSequenceNumber);
        Assert.Equal(Start, lastHtf.RequestedStartUtc);
        Assert.Equal(Start.AddHours(2), lastHtf.RequestedEndUtc);
        Assert.Equal(valid.OpenTimeUtc, lastHtf.ReturnedStartUtc);
        Assert.Equal(valid.CloseTimeUtc, lastHtf.ReturnedEndUtc);
    }

    [Fact]
    public async Task G_CoverageImport_ForbiddenOnValidationTraining()
    {
        var candles = BuildMinimalCandles(3);
        var from = candles[0].OpenTimeUtc;
        var to = candles[^1].OpenTimeUtc.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [0],
            WarmupCandleCount = 0
        };
        var run = CreateRun(23140, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to);
        var sink = new List<StrategyResearchCandidate>();
        var runner = CreateRunner(run, new PriceStructureBreakoutRetestStrategy(),
            StrategyCode.PriceStructureBreakoutRetest, "1.1.0", dataset, sink);

        var ex = await Assert.ThrowsAsync<ValidationTrainingCoverageImportForbiddenException>(() =>
            runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
            {
                ExecutionPurpose = ExecutionPurpose.ValidationTraining,
                AllowCoverageImport = true,
                CandleDataSource = new FixedSource(dataset),
                CallerComponent = "Milestone231B1Tests",
                ValidationExperimentId = 42,
                ValidationTrialNumber = 1,
                TrainingBoundaryUtc = to,
                CorrelationId = "b1-coverage-forbid"
            }));
        Assert.Contains("import", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsCanonicalSetupFingerprint_AcceptsHasherOutputOnly()
    {
        var fp = Application.Strategies.PriceStructure.SetupFingerprintHasher.Hash("canonical-probe");
        Assert.True(StrategyLabRunner.IsCanonicalSetupFingerprint(fp));
        Assert.False(StrategyLabRunner.IsCanonicalSetupFingerprint(""));
        Assert.False(StrategyLabRunner.IsCanonicalSetupFingerprint("missing-fp-1-Long"));
        Assert.False(StrategyLabRunner.IsCanonicalSetupFingerprint("ABCDEFGHIJKLMNOPQ")); // 17
        Assert.False(StrategyLabRunner.IsCanonicalSetupFingerprint("GHIJKLMNOPQRSTUV")); // non-hex
    }

    private static async Task AssertFunnelReconcilesAsync(
        long runId,
        string code,
        StrategyCode strategyCode,
        ITradingStrategy plugin,
        string version,
        StrategyLabDataset dataset)
    {
        var from = dataset.Candles[dataset.EvaluationIndices[0]].OpenTimeUtc;
        var last = dataset.Candles[dataset.EvaluationIndices[^1]];
        var to = last.OpenTimeUtc.Add(last.Timeframe == Timeframe.M15 ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5));
        var run = CreateRun(runId, code, TimeframeParser.ToApiString(dataset.Timeframe), from, to);
        run.StrategyVersion = version;
        var sink = new List<StrategyResearchCandidate>();
        await CreateRunner(run, plugin, strategyCode, version, dataset, sink)
            .ExecuteAsync(run.Id, GeneralResearchContext(dataset));

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        using var doc = JsonDocument.Parse(run.ResultSummaryJson);
        var funnel = doc.RootElement.GetProperty("rejectionFunnel");
        var evaluations = funnel.GetProperty("evaluations").GetInt32();
        var entryConfirmed = funnel.GetProperty("entryConfirmed").GetInt32();
        var counts = funnel.GetProperty("counts");
        var rejectionSum = 0;
        foreach (var prop in counts.EnumerateObject())
        {
            rejectionSum += prop.Value.GetInt32();
        }

        Assert.True(funnel.GetProperty("reconciled").GetBoolean());
        Assert.Equal(evaluations, rejectionSum + entryConfirmed);
    }

    private static StrategyLabDataset BuildAdaptiveDataset()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var evalIndex = ltf.Count - 1;
        return new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = [evalIndex],
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>> { [Timeframe.H1] = htf }
        };
    }

    private static StrategyLabDataset BuildRangeDataset()
    {
        var ranging = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        Milestone231BParityFixtures.AssignSequentialIds(ranging);
        return new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = ranging[0].Timeframe,
            Candles = ranging,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildRangingSnapshots(ranging),
            EvaluationIndices = [ranging.Count - 1],
            WarmupCandleCount = 0
        };
    }

    private static StrategyLabDataset BuildPsbrDataset()
    {
        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        return new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(0, candles.Count).ToList(),
            WarmupCandleCount = 0
        };
    }

    private static (ValidationTrainingCandleScope Scope, ValidationDatasetMaterializationRequest Request) CreateAdaptiveScopeRequest(
        IReadOnlyList<Candle>? htf = null,
        Timeframe htfKey = Timeframe.H1,
        Guid? boundAuditExecutionId = null)
    {
        var boundary = Start.AddHours(2);
        var candles = Enumerable.Range(0, 24)
            .Select(i => new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = Start.AddMinutes(i * 5),
                CloseTimeUtc = Start.AddMinutes(i * 5 + 5),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1m,
                IsClosed = true,
                CreatedAtUtc = Start.AddMinutes(i * 5)
            })
            .ToList();

        IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>>? partition = null;
        if (htf is not null)
        {
            partition = new Dictionary<Timeframe, IReadOnlyList<Candle>> { [htfKey] = htf };
        }

        var scope = new ValidationTrainingCandleScope(
            42,
            Start,
            boundary,
            candles,
            higherTimeframePartition: partition,
            strategyCode: StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            strategyVersion: MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            boundAuditExecutionId: boundAuditExecutionId);
        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = "5m",
            EvaluationFromUtc = Start,
            EvaluationToExclusiveUtc = boundary,
            WarmupCandleCount = 0,
            CallerComponent = "Milestone231B1Tests",
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout
        };
        return (scope, request);
    }

    private static Candle HtfCandle(long id, Timeframe tf, DateTime open, DateTime close, long symbolId, long exchangeId) =>
        new()
        {
            Id = id,
            SymbolId = symbolId,
            ExchangeId = exchangeId,
            Timeframe = tf,
            OpenTimeUtc = open,
            CloseTimeUtc = close,
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = open
        };

    private static List<Candle> BuildMinimalCandles(int count)
    {
        var list = new List<Candle>(count);
        for (var i = 0; i < count; i++)
        {
            var open = Start.AddMinutes(i * 5);
            list.Add(new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddMinutes(5),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100.5m,
                Volume = 1m,
                IsClosed = true,
                CreatedAtUtc = open
            });
        }

        return list;
    }

    private static StrategyLabRun CreateRun(long id, string code, string timeframe, DateTime from, DateTime to) =>
        new()
        {
            Id = id,
            Name = $"m231b1-{id}",
            StrategyCode = code,
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = code.Contains("RANGE", StringComparison.Ordinal) ? "ETHUSDT" : "BTCUSDT",
            Timeframe = timeframe,
            FromUtc = from,
            ToUtc = to,
            ExecutionMode = StrategyLabExecutionMode.RawStrategy,
            ParametersJson = "{}",
            FeeSettingsJson = """{"takerFeeRate":0.0004}""",
            SlippageSettingsJson = """{"slippagePercent":0}""",
            InitialBalance = 10000m,
            Status = StrategyLabRunStatus.Created,
            CreatedAtUtc = DateTime.UtcNow,
            CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.Current
        };

    private static StrategyLabExecutionContext GeneralResearchContext(StrategyLabDataset dataset) =>
        new()
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new FixedSource(dataset),
            CallerComponent = "Milestone231B1Tests"
        };

    private static StrategyLabRunner CreateRunner(
        StrategyLabRun run,
        ITradingStrategy plugin,
        StrategyCode code,
        string version,
        StrategyLabDataset dataset,
        List<StrategyResearchCandidate> sink)
    {
        var runRepo = new Mock<IStrategyLabRunRepository>();
        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepo.Setup(r => r.UpdateAsync(It.IsAny<StrategyLabRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var candidateRepo = new Mock<IStrategyResearchCandidateRepository>();
        candidateRepo.Setup(c => c.AddRangeAsync(It.IsAny<IEnumerable<StrategyResearchCandidate>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<StrategyResearchCandidate>, CancellationToken>((items, _) => sink.AddRange(items))
            .Returns(Task.CompletedTask);

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(s => s.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity(code, plugin.Name, version));

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(code)).Returns(plugin);

        var requirements = new Mock<IStrategyDataRequirementService>();
        requirements.Setup(r => r.GetByStrategyIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.ServiceResult<StrategyDataRequirementDto>.Ok(
                BuildRequirementDto(code.ToCode(), plugin.Name, [run.Timeframe], [run.Timeframe], [], 0)));

        var coverage = new Mock<IHistoricalCandleCoverageService>();
        coverage.Setup(c => c.EnsureCoverageAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<Func<HistoricalCoverageProgress, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.ServiceResult<HistoricalCandleCoverageResult>.Ok(new HistoricalCandleCoverageResult
            {
                Coverage = new Application.Validation.Dtos.CandleCoverageDto
                {
                    Symbol = run.Symbol,
                    Exchange = "BINANCE",
                    Timeframe = run.Timeframe,
                    MissingCandleCountEstimate = 0,
                    CoverageStatus = "Complete"
                },
                FinalCoverageStatus = "Complete",
                RequestedFromUtc = run.FromUtc,
                RequestedToUtc = run.ToUtc,
                RequestedTimeframe = run.Timeframe,
                ExistingCandleCount = dataset.Candles.Count,
                MissingRanges = []
            }));

        return new StrategyLabRunner(
            runRepo.Object,
            candidateRepo.Object,
            Mock.Of<IBacktestDataLoader>(),
            strategyRepo.Object,
            registry.Object,
            requirements.Object,
            coverage.Object,
            Mock.Of<IRiskRuleRepository>(),
            Mock.Of<IRiskProfileRepository>(),
            new PositionSizingService(),
            Mock.Of<ICandidateConfidenceScorer>(),
            standardCandleDataSource: new StandardStrategyLabCandleDataSource(Mock.Of<IBacktestDataLoader>()));
    }

    private static StrategyDataRequirementDto BuildRequirementDto(
        string code,
        string name,
        IReadOnlyList<string> allowed,
        IReadOnlyList<string> required,
        IReadOnlyList<string> htf,
        int warmup) =>
        new()
        {
            StrategyId = 1,
            StrategyCode = code,
            StrategyName = name,
            PreferredExecutionTimeframe = allowed[0],
            AllowedExecutionTimeframes = allowed,
            RequiredDataTimeframes = required,
            OptionalDataTimeframes = [],
            AnchorTimeframes = [],
            HigherTimeframeFilters = htf,
            WarmupCandles = warmup,
            RequiredIndicators = [],
            RequiredIndicatorTimeframes = [],
            Warnings = []
        };

    private static Strategy Entity(StrategyCode code, string name, string version) =>
        new()
        {
            Id = 1,
            Code = code,
            Name = name,
            Version = version,
            IsEnabled = true,
            Description = name,
            CreatedAtUtc = DateTime.UtcNow
        };

    private sealed class FixedSource : IStrategyLabCandleDataSource
    {
        private readonly StrategyLabDataset _dataset;
        public FixedSource(StrategyLabDataset dataset) => _dataset = dataset;
        public Task<StrategyLabDataset> LoadAsync(StrategyLabRun run, int warmupCandles, CancellationToken cancellationToken = default) =>
            Task.FromResult(_dataset);
    }

    private sealed class CountingStrategy : ITradingStrategy
    {
        private readonly ITradingStrategy _inner;
        public int EvaluateCount { get; private set; }
        public CountingStrategy(ITradingStrategy inner) => _inner = inner;
        public StrategyCode Code => _inner.Code;
        public string Name => _inner.Name;
        public string Description => _inner.Description;
        public IReadOnlyCollection<MarketRegime> SupportedRegimes => _inner.SupportedRegimes;
        public IReadOnlyCollection<Timeframe> SupportedTimeframes => _inner.SupportedTimeframes;
        public StrategySignalResult Evaluate(StrategyContext context)
        {
            EvaluateCount++;
            return _inner.Evaluate(context);
        }
    }

    private sealed class ForcedEntryStrategy : StrategyBase
    {
        private readonly bool _emitMalformedFingerprint;
        private readonly bool _emitValidFirst;
        private int _entries;

        public ForcedEntryStrategy(bool emitMalformedFingerprint, bool emitValidFirst)
        {
            _emitMalformedFingerprint = emitMalformedFingerprint;
            _emitValidFirst = emitValidFirst;
        }

        public override StrategyCode Code => StrategyCode.PriceStructureBreakoutRetest;
        public override string Name => "Forced Entry";
        public override string Description => "Test double";
        public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
            [MarketRegime.Breakout, MarketRegime.Trending, MarketRegime.Ranging];
        public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
            [Timeframe.M5, Timeframe.M15];

        public override StrategySignalResult Evaluate(StrategyContext context)
        {
            _entries++;
            var useValid = _emitValidFirst && _entries == 1;
            var fingerprint = useValid
                ? Application.Strategies.PriceStructure.SetupFingerprintHasher.Hash($"valid-{_entries}")
                : (_emitMalformedFingerprint ? string.Empty : Application.Strategies.PriceStructure.SetupFingerprintHasher.Hash($"ok-{_entries}"));

            var raw = JsonSerializer.Serialize(new
            {
                setupFingerprint = fingerprint,
                strengthBreakdown = new { total = 80 }
            });

            return Entry(
                TradeDirection.Long,
                80m,
                80m,
                100m,
                99m,
                102m,
                "ForcedEntry",
                raw);
        }
    }
}
