using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
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
/// Milestone 23.1B — Canonical Research Runner Parity and Multi-Timeframe Dataset Integrity.
/// </summary>
public sealed class Milestone231BTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Capability_StrategyLabNewRunCodes_ExactlyThreeCanonical()
    {
        Assert.Equal(3, CanonicalStrategyPortfolio.StrategyLabNewRunCodes.Count);
        Assert.Contains(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, CanonicalStrategyPortfolio.StrategyLabNewRunCodes);
        Assert.Contains(StrategyCodes.PriceStructureBreakoutRetest, CanonicalStrategyPortfolio.StrategyLabNewRunCodes);
        Assert.Contains(StrategyCodes.MomoVolatilityRangeReversion, CanonicalStrategyPortfolio.StrategyLabNewRunCodes);
    }

    [Fact]
    public void Capability_SupportsStrategyLab_TrueForThreeCanonical()
    {
        Assert.True(StrategyCapabilityPolicy.SupportsStrategyLab(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.True(StrategyCapabilityPolicy.SupportsStrategyLab(StrategyCode.PriceStructureBreakoutRetest));
        Assert.True(StrategyCapabilityPolicy.SupportsStrategyLab(StrategyCode.MomoVolatilityRangeReversion));
    }

    [Fact]
    public void Capability_AdaptiveAndRange_OptAndValRemainFalse()
    {
        Assert.False(StrategyCapabilityPolicy.SupportsOptimization(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.False(StrategyCapabilityPolicy.SupportsValidation(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.False(StrategyCapabilityPolicy.SupportsOptimization(StrategyCode.MomoVolatilityRangeReversion));
        Assert.False(StrategyCapabilityPolicy.SupportsValidation(StrategyCode.MomoVolatilityRangeReversion));
    }

    [Fact]
    public void StrategyLabDataset_HtfSeries_RoundTripsThroughFromBacktestToBacktest()
    {
        var ltf = new List<Candle> { Candle(1, Timeframe.M5, Start, 100m) };
        var htf = new List<Candle> { Candle(2, Timeframe.H1, Start, 100m) };
        var backtest = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [0],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = htf
            }
        };

        var lab = StrategyLabDataset.FromBacktest(backtest);
        Assert.True(lab.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.H1));
        Assert.Single(lab.HigherTimeframeSeriesByTimeframe[Timeframe.H1]);

        var roundTrip = lab.ToBacktest();
        Assert.True(roundTrip.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.H1));
        Assert.Equal(htf[0].OpenTimeUtc, roundTrip.HigherTimeframeSeriesByTimeframe[Timeframe.H1][0].OpenTimeUtc);
    }

    [Fact]
    public void DeterministicMarketRegimeClassifier_ParityWithBacktestHeuristic()
    {
        var candle = Candle(10, Timeframe.M5, Start, 100m);
        var trending = new IndicatorSnapshot
        {
            CandleId = 10,
            Ema20 = 110m,
            Ema50 = 105m,
            Ema200 = 100m,
            Atr14 = 1m
        };
        var ranging = new IndicatorSnapshot
        {
            CandleId = 10,
            Ema20 = 101m,
            Ema50 = 105m,
            Ema200 = 100m,
            Atr14 = 0.5m
        };

        Assert.Equal(MarketRegime.Trending, DeterministicMarketRegimeClassifier.Classify(trending, candle));
        Assert.Equal(MarketRegime.Ranging, DeterministicMarketRegimeClassifier.Classify(ranging, candle));
        Assert.Equal(MarketRegime.Unknown, DeterministicMarketRegimeClassifier.Classify(null, candle));
    }

    [Fact]
    public async Task AdaptiveLab_ProductionPath_MatchesDirectPluginCandidate()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var parameters = new Dictionary<string, string>(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract());
        var seen = new HashSet<string>();
        parameters["__seenFingerprints"] = JsonSerializer.Serialize(seen);

        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            HigherTimeframeCandles = htf,
            MarketRegime = MarketRegime.Trending,
            Candles = ltf,
            IndicatorSnapshot = null,
            StrategyParameters = parameters,
            EvaluatedAtUtc = ltf[^1].CloseTimeUtc,
            CurrentCandleIndex = ltf.Count - 1
        });

        Assert.Equal(SignalType.Entry, direct.SignalType);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.False(string.IsNullOrWhiteSpace(directFp));

        var from = ltf[^5].OpenTimeUtc;
        var to = ltf[^1].OpenTimeUtc.AddMinutes(5);
        var evalIndices = Enumerable.Range(0, ltf.Count)
            .Where(i => ltf[i].OpenTimeUtc >= from && ltf[i].OpenTimeUtc < to)
            .ToList();
        Assert.NotEmpty(evalIndices);

        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = BuildTrendingSnapshots(ltf),
            EvaluationIndices = evalIndices,
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = htf
            }
        };

        var run = CreateRun(901, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "5m", from, to);
        var persisted = new List<StrategyResearchCandidate>();
        var runner = CreateRunner(
            run,
            plugin,
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            dataset,
            persisted);

        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.NotEmpty(persisted);
        Assert.Contains(persisted, c => c.SetupFingerprint == directFp);
    }

    [Fact]
    public async Task AdaptiveLab_MissingHtf_FailsClosed_NoLtfSubstitution()
    {
        var (ltf, _) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var from = ltf[^3].OpenTimeUtc;
        var to = ltf[^1].OpenTimeUtc.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(ltf.Count - 3, 3).ToList(),
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>()
        };

        var run = CreateRun(902, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "5m", from, to);
        var runner = CreateRunner(
            run,
            new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy(),
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            dataset,
            []);

        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BTests"
        });

        Assert.Equal(StrategyLabRunStatus.Failed, run.Status);
        Assert.Contains(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, run.ErrorMessage ?? string.Empty);
        Assert.DoesNotContain("legacy-", run.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RangeLab_RangingFixture_ProducesCandidate_TrendingRejectsViaRegime()
    {
        var plugin = new MomoVolatilityRangeReversionStrategy();
        var rangingCandles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        // Align timeframe with Lab run (fixture may be M5/M15 — normalize OpenTime spacing already set on candles).
        var tf = rangingCandles[0].Timeframe;
        var tfApi = TimeframeParser.ToApiString(tf);
        var from = rangingCandles[Math.Max(0, rangingCandles.Count - 20)].OpenTimeUtc;
        var to = rangingCandles[^1].OpenTimeUtc.Add(
            tf == Timeframe.M15 ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5));

        var rangingDataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = tf,
            Candles = rangingCandles,
            IndicatorSnapshots = BuildRangingSnapshots(rangingCandles),
            EvaluationIndices = Enumerable.Range(Math.Max(0, rangingCandles.Count - 20), Math.Min(20, rangingCandles.Count)).ToList(),
            WarmupCandleCount = 0
        };

        var rangingRun = CreateRun(910, StrategyCodes.MomoVolatilityRangeReversion, tfApi, from, to);
        var rangingCandidates = new List<StrategyResearchCandidate>();
        var rangingRunner = CreateRunner(
            rangingRun,
            plugin,
            StrategyCode.MomoVolatilityRangeReversion,
            MomoVolatilityRangeReversionStrategy.Version,
            rangingDataset,
            rangingCandidates);

        await rangingRunner.ExecuteAsync(rangingRun.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new FixedStrategyLabCandleDataSource(rangingDataset),
            CallerComponent = "Milestone231BTests"
        });

        var directParams = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };
        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = tf,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Ranging,
            Candles = rangingCandles,
            IndicatorSnapshot = null,
            StrategyParameters = directParams,
            EvaluatedAtUtc = rangingCandles[^1].CloseTimeUtc,
            CurrentCandleIndex = rangingCandles.Count - 1
        });

        Assert.Equal(StrategyLabRunStatus.Completed, rangingRun.Status);
        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.True(
            rangingCandidates.Count > 0,
            "Expected ranging Lab path (plugin + DeterministicMarketRegimeClassifier) to persist at least one candidate.");

        var trendingDataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = tf,
            Candles = rangingCandles,
            IndicatorSnapshots = BuildTrendingSnapshots(rangingCandles),
            EvaluationIndices = Enumerable.Range(Math.Max(0, rangingCandles.Count - 20), Math.Min(20, rangingCandles.Count)).ToList(),
            WarmupCandleCount = 0
        };
        var trendingRun = CreateRun(911, StrategyCodes.MomoVolatilityRangeReversion, tfApi, from, to);
        var trendingCandidates = new List<StrategyResearchCandidate>();
        var trendingRunner = CreateRunner(
            trendingRun,
            plugin,
            StrategyCode.MomoVolatilityRangeReversion,
            MomoVolatilityRangeReversionStrategy.Version,
            trendingDataset,
            trendingCandidates);

        await trendingRunner.ExecuteAsync(trendingRun.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new FixedStrategyLabCandleDataSource(trendingDataset),
            CallerComponent = "Milestone231BTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, trendingRun.Status);
        Assert.DoesNotContain(trendingCandidates, c => c.CandidateStatus == StrategyResearchCandidateStatus.StrategyQualified
            || c.CandidateStatus == StrategyResearchCandidateStatus.Closed
            || c.CandidateStatus == StrategyResearchCandidateStatus.Simulated);
        using var doc = JsonDocument.Parse(trendingRun.ResultSummaryJson);
        var funnel = doc.RootElement.GetProperty("rejectionFunnel").GetProperty("counts");
        Assert.True(funnel.TryGetProperty(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, out _));
        Assert.False(funnel.TryGetProperty("Trending", out _));
    }

    [Fact]
    public async Task PsbrLab_V11_MatchesPlugin_AndV10RerunBlocked()
    {
        var plugin = new PriceStructureBreakoutRetestStrategy();
        Assert.Equal("1.1.0", PriceStructureBreakoutRetestStrategy.Version);

        var candles = new List<Candle>();
        var t = Start;
        for (var i = 0; i < 30; i++)
        {
            candles.Add(new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = t,
                CloseTimeUtc = t.AddMinutes(15),
                Open = 100m,
                High = 100.5m,
                Low = 99.5m,
                Close = 100.2m,
                Volume = 10m,
                IsClosed = true,
                CreatedAtUtc = t
            });
            t = t.AddMinutes(15);
        }

        var from = candles[0].OpenTimeUtc;
        var to = candles[^1].OpenTimeUtc.AddMinutes(15);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M15,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = Enumerable.Range(0, candles.Count).ToList(),
            WarmupCandleCount = 0
        };

        var run = CreateRun(920, StrategyCodes.PriceStructureBreakoutRetest, "15m", from, to);
        run.StrategyVersion = PriceStructureBreakoutRetestEvaluator.StrategyVersion;
        var candidates = new List<StrategyResearchCandidate>();
        var runner = CreateRunner(
            run,
            plugin,
            StrategyCode.PriceStructureBreakoutRetest,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion,
            dataset,
            candidates);

        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.Contains(PriceStructureBreakoutRetestEvaluator.StrategyVersion, run.StrategyVersion);

        var service = new StrategyLabService(
            Mock.Of<IStrategyLabRunRepository>(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()) == Task.FromResult<StrategyLabRun?>(new StrategyLabRun
            {
                Id = 1,
                Name = "v10",
                StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
                StrategyVersion = PriceStructureBreakoutRetestEvaluator.StrategyVersionV10,
                ExchangeId = 1,
                SymbolId = 1,
                Symbol = "BTCUSDT",
                Timeframe = "15m",
                FromUtc = from,
                ToUtc = to,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                FeeSettingsJson = "{}",
                SlippageSettingsJson = "{}",
                InitialBalance = 10000m
            })),
            Mock.Of<IStrategyResearchCandidateRepository>(),
            Mock.Of<IStrategyRepository>(),
            Mock.Of<IStrategyRegistry>(),
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IStrategyLabQueue>(),
            Mock.Of<IStrategyDataRequirementService>());

        var blocked = await service.GetRerunConfigAsync(1);
        Assert.False(blocked.Succeeded);
        Assert.Contains("v1.0.0", blocked.ErrorMessage);
    }

    [Fact]
    public void CrossPath_DirectAndLabFingerprintExtraction_Consistent()
    {
        var canonical = Application.Strategies.PriceStructure.SetupFingerprintHasher.Hash("parity-probe");
        var raw = JsonSerializer.Serialize(new { setupFingerprint = canonical, version = "1.0.0" });
        Assert.Equal(canonical, StrategyLabRunner.ExtractFingerprint(raw));
        Assert.True(StrategyLabRunner.IsCanonicalSetupFingerprint(canonical));
        Assert.Equal(string.Empty, StrategyLabRunner.ExtractFingerprint("{}"));
        Assert.False(StrategyLabRunner.IsCanonicalSetupFingerprint("ABC123"));
        Assert.False(StrategyLabRunner.IsCanonicalSetupFingerprint("missing-fp-1"));
        Assert.DoesNotContain("legacy-", StrategyLabRunner.ExtractFingerprint("{}"));
    }

    [Fact]
    public void DatasetFingerprint_ExecutionAndHtfAreDistinct()
    {
        var ltf = Enumerable.Range(0, 5).Select(i => Candle(i + 1, Timeframe.M5, Start.AddMinutes(i * 5), 100m + i)).ToList();
        var htf = Enumerable.Range(0, 3).Select(i => Candle(100 + i, Timeframe.H1, Start.AddHours(i), 200m + i)).ToList();
        var ltfFp = ExperimentFingerprintBuilder.BuildCandleContentFingerprint(ltf).FullSha256;
        var htfFp = ExperimentFingerprintBuilder.BuildCandleContentFingerprint(htf).FullSha256;
        Assert.NotEqual(ltfFp, htfFp);
    }

    [Fact]
    public void ValidationPartition_AdaptiveMissingHtf_FailsClosed()
    {
        var start = Start;
        var boundary = start.AddHours(2);
        var candles = Enumerable.Range(0, 24)
            .Select(i => Candle(i + 1, Timeframe.M5, start.AddMinutes(i * 5), 100m))
            .ToList();
        var scope = new ValidationTrainingCandleScope(42, start, boundary, candles);
        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = candles[0].SymbolId,
            SymbolName = "BTCUSDT",
            Timeframe = "5m",
            EvaluationFromUtc = start,
            EvaluationToExclusiveUtc = boundary,
            WarmupCandleCount = 0,
            CallerComponent = "Milestone231BTests",
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Contains("HTF", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationPartition_HtfBeyondBoundary_Rejected()
    {
        var start = Start;
        var boundary = start.AddHours(2);
        var candles = Enumerable.Range(0, 24)
            .Select(i => Candle(i + 1, Timeframe.M5, start.AddMinutes(i * 5), 100m))
            .ToList();
        var scope = new ValidationTrainingCandleScope(43, start, boundary, candles);
        var poisonedHtf = new List<Candle>
        {
            Candle(500, Timeframe.H1, start, 100m),
            Candle(501, Timeframe.H1, boundary, 101m) // open at boundary — rejected
        };
        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = candles[0].SymbolId,
            SymbolName = "BTCUSDT",
            Timeframe = "5m",
            EvaluationFromUtc = start,
            EvaluationToExclusiveUtc = boundary,
            WarmupCandleCount = 0,
            CallerComponent = "Milestone231BTests",
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = poisonedHtf
            }
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Contains("beyond", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtfMappingContract_AdaptiveUsesCanonicalMappings()
    {
        Assert.Equal(Timeframe.H1, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.M5));
        Assert.Equal(Timeframe.H4, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.M15));
        Assert.Equal(Timeframe.H4, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H1));
        Assert.Equal(Timeframe.D1, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H4));
        Assert.Equal("AdaptiveHtfMapping/v1", StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion);
    }

    private static StrategyLabRun CreateRun(long id, string code, string timeframe, DateTime from, DateTime to) =>
        new()
        {
            Id = id,
            Name = $"m231b-{id}",
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
            .ReturnsAsync(new Strategy
            {
                Id = 1,
                Code = code,
                Name = plugin.Name,
                Version = version,
                IsEnabled = true,
                Description = plugin.Description
            });

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(code)).Returns(plugin);

        var requirements = new Mock<IStrategyDataRequirementService>();
        requirements.Setup(r => r.GetByStrategyIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.ServiceResult<Application.Strategies.Dtos.StrategyDataRequirementDto>.Ok(
                new Application.Strategies.Dtos.StrategyDataRequirementDto
                {
                    StrategyId = 1,
                    StrategyCode = code.ToCode(),
                    StrategyName = plugin.Name,
                    PreferredExecutionTimeframe = run.Timeframe,
                    AllowedExecutionTimeframes = [run.Timeframe],
                    RequiredDataTimeframes = [run.Timeframe],
                    OptionalDataTimeframes = [],
                    AnchorTimeframes = [],
                    HigherTimeframeFilters = [],
                    WarmupCandles = 0,
                    RequiredIndicators = [],
                    RequiredIndicatorTimeframes = [],
                    Warnings = []
                }));

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
                Coverage = new MomoQuant.Application.Validation.Dtos.CandleCoverageDto
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

    private static Dictionary<long, IndicatorSnapshot> BuildTrendingSnapshots(IReadOnlyList<Candle> candles)
    {
        var map = new Dictionary<long, IndicatorSnapshot>();
        foreach (var c in candles)
        {
            map[c.Id] = new IndicatorSnapshot
            {
                CandleId = c.Id,
                Ema20 = c.Close + 30m,
                Ema50 = c.Close + 20m,
                Ema200 = c.Close + 10m,
                Atr14 = Math.Max(1m, c.Close * 0.01m)
            };
        }

        return map;
    }

    private static Dictionary<long, IndicatorSnapshot> BuildRangingSnapshots(IReadOnlyList<Candle> candles)
    {
        var map = new Dictionary<long, IndicatorSnapshot>();
        foreach (var c in candles)
        {
            map[c.Id] = new IndicatorSnapshot
            {
                CandleId = c.Id,
                Ema20 = c.Close + 1m,
                Ema50 = c.Close + 5m,
                Ema200 = c.Close,
                Atr14 = Math.Max(0.5m, c.Close * 0.005m)
            };
        }

        return map;
    }

    private static Candle Candle(long id, Timeframe tf, DateTime open, decimal px) =>
        new()
        {
            Id = id,
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = tf,
            OpenTimeUtc = open,
            CloseTimeUtc = tf == Timeframe.H1 ? open.AddHours(1) : open.AddMinutes(5),
            Open = px,
            High = px + 1m,
            Low = px - 1m,
            Close = px,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = open
        };

    private sealed class FixedStrategyLabCandleDataSource : IStrategyLabCandleDataSource
    {
        private readonly StrategyLabDataset _dataset;

        public FixedStrategyLabCandleDataSource(StrategyLabDataset dataset) => _dataset = dataset;

        public Task<StrategyLabDataset> LoadAsync(StrategyLabRun run, int warmupCandles, CancellationToken cancellationToken = default) =>
            Task.FromResult(_dataset);
    }
}
