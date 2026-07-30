using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;
using Moq;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1B §12 — Lab/Backtest/plugin parity, HTF integrity, Adaptive mapping.
/// </summary>
public sealed class Milestone231BParityTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AdaptiveLab_H4ToD1_ProductionPath_MatchesDirectPluginCandidate()
    {
        var (ltf, htf) = Milestone231BParityFixtures.RemapAdaptiveToH4D1(AdaptiveDefaultFixtures.BuildValidLong(Start));
        Assert.Equal(Timeframe.H4, ltf[0].Timeframe);
        Assert.Equal(Timeframe.D1, htf[0].Timeframe);
        Assert.Equal(Timeframe.D1, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H4));

        var plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var parameters = new Dictionary<string, string>(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract());
        parameters["__seenFingerprints"] = JsonSerializer.Serialize(new HashSet<string>());
        var snapshot = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf).GetValueOrDefault(ltf[^1].Id);
        var regime = DeterministicMarketRegimeClassifier.Classify(snapshot, ltf[^1]);

        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.H4,
            HigherTimeframe = Timeframe.D1,
            HigherTimeframeCandles = htf,
            MarketRegime = regime,
            Candles = ltf,
            IndicatorSnapshot = snapshot,
            StrategyParameters = parameters,
            EvaluatedAtUtc = ltf[^1].CloseTimeUtc,
            CurrentCandleIndex = ltf.Count - 1
        });

        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.False(string.IsNullOrWhiteSpace(directFp));

        var from = ltf[^5].OpenTimeUtc;
        var to = ltf[^1].OpenTimeUtc.AddHours(4);
        var evalIndices = Enumerable.Range(0, ltf.Count)
            .Where(i => ltf[i].OpenTimeUtc >= from && ltf[i].OpenTimeUtc < to)
            .ToList();
        Assert.NotEmpty(evalIndices);

        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.H4,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = evalIndices,
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.D1] = htf
            }
        };

        var run = Milestone231BParityFixtures.CreateRun(
            930, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "4h", from, to);
        var persisted = new List<StrategyResearchCandidate>();
        var runner = Milestone231BParityFixtures.CreateRunner(
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
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BParityTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.NotEmpty(persisted);
        var lab = Assert.Single(persisted, c => c.SetupFingerprint == directFp);
        Assert.Equal(direct.Strength, ParityAssertionHelper.ExtractStrengthForTest(lab.StructureJson));
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(lab.StructureJson));
    }

    [Fact]
    public async Task AdaptiveLab_FuturePollutedHtf_MatchesClean_ClosedThroughOnly()
    {
        var (ltf, cleanHtf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        Milestone231BParityFixtures.AssignSequentialIds(ltf);
        Milestone231BParityFixtures.AssignSequentialIds(cleanHtf, 10_000);
        var evaluationTimeUtc = ltf[^1].CloseTimeUtc;
        var pollutedHtf = Milestone231BParityFixtures.PolluteHtf(cleanHtf, evaluationTimeUtc, Timeframe.H1);

        Assert.True(pollutedHtf.Count > cleanHtf.Count);
        Assert.Contains(pollutedHtf, c => c.Id == 90_001 && !c.IsClosed);
        Assert.Contains(pollutedHtf, c => c.Id == 90_002 && c.IsClosed && c.CloseTimeUtc > evaluationTimeUtc);

        var cleanVisible = StrategyHigherTimeframeSupport.SliceHigherTimeframeCandles(
            new Dictionary<Timeframe, IReadOnlyList<Candle>> { [Timeframe.H1] = cleanHtf },
            Timeframe.H1,
            evaluationTimeUtc);
        var pollutedVisible = StrategyHigherTimeframeSupport.SliceHigherTimeframeCandles(
            new Dictionary<Timeframe, IReadOnlyList<Candle>> { [Timeframe.H1] = pollutedHtf },
            Timeframe.H1,
            evaluationTimeUtc);

        Assert.Equal(cleanVisible.Count, pollutedVisible.Count);
        Assert.Equal(
            cleanVisible.Select(c => c.Id).ToArray(),
            pollutedVisible.Select(c => c.Id).ToArray());
        Assert.All(pollutedVisible, c =>
        {
            Assert.True(c.IsClosed);
            Assert.True(c.CloseTimeUtc <= evaluationTimeUtc);
        });
        Assert.DoesNotContain(pollutedVisible, c => c.Id is 90_001 or 90_002);

        var plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var parameters = new Dictionary<string, string>(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };

        var cleanDirect = plugin.Evaluate(BuildAdaptiveContext(ltf, cleanVisible, parameters, evaluationTimeUtc));
        var pollutedDirect = plugin.Evaluate(BuildAdaptiveContext(ltf, pollutedVisible, parameters, evaluationTimeUtc));
        Assert.Equal(SignalType.Entry, cleanDirect.SignalType);
        Assert.Equal(cleanDirect.SignalType, pollutedDirect.SignalType);
        Assert.Equal(cleanDirect.Direction, pollutedDirect.Direction);
        Assert.Equal(cleanDirect.EntryPrice, pollutedDirect.EntryPrice);
        Assert.Equal(cleanDirect.SuggestedStopLoss, pollutedDirect.SuggestedStopLoss);
        Assert.Equal(cleanDirect.SuggestedTakeProfit, pollutedDirect.SuggestedTakeProfit);
        Assert.Equal(cleanDirect.Strength, pollutedDirect.Strength);
        Assert.Equal(
            StrategyLabRunner.ExtractFingerprint(cleanDirect.RawDataJson ?? "{}"),
            StrategyLabRunner.ExtractFingerprint(pollutedDirect.RawDataJson ?? "{}"));

        var from = ltf[^3].OpenTimeUtc;
        var to = ltf[^1].OpenTimeUtc.AddMinutes(5);
        var evalIndices = Enumerable.Range(ltf.Count - 3, 3).ToList();

        var cleanPersisted = await RunAdaptiveLabAsync(931, ltf, cleanHtf, from, to, evalIndices);
        var pollutedPersisted = await RunAdaptiveLabAsync(932, ltf, pollutedHtf, from, to, evalIndices);

        Assert.NotEmpty(cleanPersisted);
        Assert.Equal(
            cleanPersisted.Select(c => (c.SetupFingerprint, c.ProposedEntryPrice, c.StopLoss, c.Target1, c.Direction)).ToArray(),
            pollutedPersisted.Select(c => (c.SetupFingerprint, c.ProposedEntryPrice, c.StopLoss, c.Target1, c.Direction)).ToArray());

        // Backtest capture proves polluted source is filtered before the plugin.
        var prepared = ClosedHtfCaptureHarness.CreateAdaptivePrepared();
        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = Milestone231BParityFixtures.CreateBacktestEngine(
            recording,
            MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract());
        var dataset = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = [ltf.Count - 1],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = pollutedHtf
            }
        };
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            dataset,
            [prepared],
            evaluationIndex: 0);

        var capture = Assert.Single(recording.Capture.Records);
        Assert.All(capture.HigherTimeframeCandles, c =>
        {
            Assert.True(c.IsClosed);
            Assert.True(c.CloseTimeUtc <= evaluationTimeUtc);
        });
        Assert.DoesNotContain(capture.HigherTimeframeCandles, c => c.Id is 90_001 or 90_002);
        Assert.Equal(
            cleanVisible.Select(c => c.Id).ToArray(),
            capture.HigherTimeframeCandles.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task AdaptiveLab_LtfAsHtf_FailsClosed_NoAdaptiveCandidate()
    {
        var (ltf, _) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        Milestone231BParityFixtures.AssignSequentialIds(ltf);
        var from = ltf[^3].OpenTimeUtc;
        var to = ltf[^1].OpenTimeUtc.AddMinutes(5);

        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = Enumerable.Range(ltf.Count - 3, 3).ToList(),
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                // LTF series substituted under the mapped H1 key.
                [Timeframe.H1] = ltf
            }
        };

        var run = Milestone231BParityFixtures.CreateRun(
            933, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "5m", from, to);
        var persisted = new List<StrategyResearchCandidate>();
        var runner = Milestone231BParityFixtures.CreateRunner(
            run,
            new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy(),
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            dataset,
            persisted);

        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BParityTests"
        });

        Assert.Equal(StrategyLabRunStatus.Failed, run.Status);
        Assert.Contains(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, run.ErrorMessage ?? string.Empty);
        Assert.Contains("LTF must never substitute", run.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(persisted);
    }

    [Fact]
    public async Task RangeLab_RangingFixture_ExactCandidate_TrendingRejectsViaRegime()
    {
        var plugin = new MomoVolatilityRangeReversionStrategy();
        var rangingCandles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        Milestone231BParityFixtures.AssignSequentialIds(rangingCandles);
        var tf = rangingCandles[0].Timeframe;
        var tfApi = TimeframeParser.ToApiString(tf);
        var from = rangingCandles[Math.Max(0, rangingCandles.Count - 5)].OpenTimeUtc;
        var to = rangingCandles[^1].OpenTimeUtc.AddMinutes(5);

        var directParams = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };
        var rangingSnapshot = Milestone231BParityFixtures.BuildRangingSnapshots(rangingCandles).GetValueOrDefault(rangingCandles[^1].Id);
        var rangingRegime = DeterministicMarketRegimeClassifier.Classify(rangingSnapshot, rangingCandles[^1]);
        var rangeHtf = StrategyHigherTimeframeSupport.ResolveGeneralHigherTimeframe(tf);
        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = tf,
            HigherTimeframe = rangeHtf,
            MarketRegime = rangingRegime,
            Candles = rangingCandles,
            IndicatorSnapshot = rangingSnapshot,
            StrategyParameters = directParams,
            EvaluatedAtUtc = rangingCandles[^1].CloseTimeUtc,
            CurrentCandleIndex = rangingCandles.Count - 1
        });

        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.Equal("43E14ED345E566C3", directFp);
        Assert.Equal(2868m, direct.EntryPrice);
        Assert.Equal(2837.4362555235449367318813733m, direct.SuggestedStopLoss);
        Assert.Equal(3000m, direct.SuggestedTakeProfit); // midpoint target

        var rangingDataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = tf,
            Candles = rangingCandles,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildRangingSnapshots(rangingCandles),
            EvaluationIndices = Enumerable.Range(Math.Max(0, rangingCandles.Count - 5), Math.Min(5, rangingCandles.Count)).ToList(),
            WarmupCandleCount = 0
        };

        var rangingRun = Milestone231BParityFixtures.CreateRun(
            940, StrategyCodes.MomoVolatilityRangeReversion, tfApi, from, to,
            MomoVolatilityRangeReversionStrategy.Version);
        var rangingCandidates = new List<StrategyResearchCandidate>();
        var rangingRunner = Milestone231BParityFixtures.CreateRunner(
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
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(rangingDataset),
            CallerComponent = "Milestone231BParityTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, rangingRun.Status);
        Assert.NotEmpty(rangingCandidates);
        var lab = Assert.Single(rangingCandidates, c => c.SetupFingerprint == directFp);
        Assert.Equal(direct.Direction, lab.Direction);
        Assert.Equal(direct.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, lab.Target1);
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(lab.StructureJson));

        // Trending snapshots → regime rejection (not hard-coded Trending manufacturing).
        var trendingDataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = tf,
            Candles = rangingCandles,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(rangingCandles),
            EvaluationIndices = rangingDataset.EvaluationIndices,
            WarmupCandleCount = 0
        };
        var trendingRun = Milestone231BParityFixtures.CreateRun(
            941, StrategyCodes.MomoVolatilityRangeReversion, tfApi, from, to,
            MomoVolatilityRangeReversionStrategy.Version);
        var trendingCandidates = new List<StrategyResearchCandidate>();
        var trendingRunner = Milestone231BParityFixtures.CreateRunner(
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
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(trendingDataset),
            CallerComponent = "Milestone231BParityTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, trendingRun.Status);
        Assert.DoesNotContain(trendingCandidates, c =>
            c.CandidateStatus is StrategyResearchCandidateStatus.StrategyQualified
                or StrategyResearchCandidateStatus.Closed
                or StrategyResearchCandidateStatus.Simulated);
        using var doc = JsonDocument.Parse(trendingRun.ResultSummaryJson);
        var funnel = doc.RootElement.GetProperty("rejectionFunnel").GetProperty("counts");
        Assert.True(funnel.TryGetProperty(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, out _));
        Assert.False(funnel.TryGetProperty("Trending", out _));
        Assert.False(
            rangingRun.ResultSummaryJson.Contains("\"forcedRegime\"", StringComparison.OrdinalIgnoreCase)
            || rangingRun.ResultSummaryJson.Contains("MarketRegime.Trending", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PsbrLab_V11_BuildLongScenario_MatchesPlugin_AndV10RerunBlocked()
    {
        var plugin = new PriceStructureBreakoutRetestStrategy();
        Assert.Equal("1.1.0", PriceStructureBreakoutRetestStrategy.Version);

        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var regime = DeterministicMarketRegimeClassifier.Classify(null, candles[^1]);
        Assert.Equal(MarketRegime.Unknown, regime);

        var pluginEval = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = StrategyHigherTimeframeSupport.ResolveGeneralHigherTimeframe(Timeframe.M5),
            MarketRegime = regime,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string> { ["__seenFingerprints"] = "[]" },
            EvaluatedAtUtc = candles[^1].CloseTimeUtc,
            CurrentCandleIndex = candles.Count - 1
        });
        Assert.Equal(SignalType.Entry, pluginEval.SignalType);
        Assert.Equal(TradeDirection.Long, pluginEval.Direction);
        Assert.Equal(100.80m, pluginEval.EntryPrice);
        Assert.Equal(99.95m, pluginEval.SuggestedStopLoss);
        Assert.Equal(102.50m, pluginEval.SuggestedTakeProfit);
        var pluginFp = StrategyLabRunner.ExtractFingerprint(pluginEval.RawDataJson ?? "{}");
        Assert.True(StrategyLabRunner.IsCanonicalSetupFingerprint(pluginFp));
        Assert.True(
            Milestone231BParityFixtures.HasStrengthBreakdown(pluginEval.RawDataJson ?? "{}"),
            "Plugin RawDataJson must include strengthBreakdown (detector structure-only JSON does not).");

        var from = candles[^1].OpenTimeUtc;
        var to = from.AddMinutes(5);
        var evalIndex = candles.Count - 1;
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [evalIndex],
            WarmupCandleCount = 0
        };

        var run = Milestone231BParityFixtures.CreateRun(
            950,
            StrategyCodes.PriceStructureBreakoutRetest,
            "5m",
            from,
            to,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion);
        var candidates = new List<StrategyResearchCandidate>();
        var runner = Milestone231BParityFixtures.CreateRunner(
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
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BParityTests"
        });

        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.NotEmpty(candidates);
        var lab = Assert.Single(candidates, c => c.SetupFingerprint == pluginFp);
        Assert.Equal(pluginEval.Direction, lab.Direction);
        Assert.Equal(pluginEval.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(pluginEval.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(pluginEval.SuggestedTakeProfit, lab.Target1);
        Assert.True(Milestone231BParityFixtures.HasStrengthBreakdown(lab.StructureJson));
        Assert.Contains(PriceStructureBreakoutRetestEvaluator.StrategyVersion, run.StrategyVersion);

        using var summary = JsonDocument.Parse(run.ResultSummaryJson);
        Assert.True(summary.RootElement.TryGetProperty("regimeDistribution", out var regimes));
        Assert.True(regimes.TryGetProperty(MarketRegime.Unknown.ToString(), out _));

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
                Timeframe = "5m",
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
    public async Task CrossPath_Adaptive_DirectLabBacktest_IdenticalAtSameT()
    {
        var (fullLtf, fullHtf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Align with BacktestEngine's 600-candle visible window so absolute indices in RawDataJson match.
        var ltf = fullLtf.Count <= 600 ? fullLtf : fullLtf.TakeLast(600).ToList();
        var htf = fullHtf;
        Milestone231BParityFixtures.AssignSequentialIds(ltf);
        Milestone231BParityFixtures.AssignSequentialIds(htf, 10_000);
        var evalIndex = ltf.Count - 1;
        var evaluationTimeUtc = ltf[evalIndex].CloseTimeUtc;
        var visibleHtf = StrategyHigherTimeframeSupport.SliceHigherTimeframeCandles(
            new Dictionary<Timeframe, IReadOnlyList<Candle>> { [Timeframe.H1] = htf },
            Timeframe.H1,
            evaluationTimeUtc);
        Assert.NotEmpty(visibleHtf);

        var parameters = new Dictionary<string, string>(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };
        var plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var context = BuildAdaptiveContext(ltf, visibleHtf, parameters, evaluationTimeUtc);
        var direct = plugin.Evaluate(context);
        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        var classifiedRegime = context.MarketRegime;

        var from = ltf[evalIndex].OpenTimeUtc;
        var to = from.AddMinutes(5);
        var labCandidates = await RunAdaptiveLabAsync(960, ltf, htf, from, to, [evalIndex]);
        Assert.NotEmpty(labCandidates);
        var lab = Assert.Single(labCandidates, c => c.SetupFingerprint == directFp);

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = Milestone231BParityFixtures.CreateBacktestEngine(recording, parameters);
        var dataset = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = [evalIndex],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = htf
            }
        };
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            dataset,
            [ClosedHtfCaptureHarness.CreateAdaptivePrepared()],
            evaluationIndex: 0);

        var backtestResult = Assert.Single(recording.Results);
        var capture = Assert.Single(recording.Capture.Records);
        Assert.False(backtestResult.Skipped);
        ParityAssertionHelper.AssertPositiveEntryParity(direct, lab, backtestResult, new ParityAssertionHelper.PositiveParityEvidence
        {
            Capture = capture,
            ExpectedRegime = classifiedRegime,
            ExpectedHigherTimeframe = Timeframe.H1,
            ExpectedTimeframe = Timeframe.M5,
            ExpectedSymbol = "BTCUSDT",
            ExpectedSymbolId = 1,
            ExpectedExchangeId = 1,
            ExpectedEvaluationTimestamp = evaluationTimeUtc,
            ExpectedCurrentCandleIndex = evalIndex,
            ExpectedExecutionCandleIds = ltf.Take(evalIndex + 1).Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = visibleHtf.Select(c => c.Id).ToArray()
        });
    }

    [Fact]
    public async Task CrossPath_Range_DirectLabBacktest_IdenticalAtSameT()
    {
        // BacktestEngine windows to 600 recent candles — use the same visible window on all three paths.
        var full = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var candles = full.Count <= 600 ? full : full.TakeLast(600).ToList();
        Milestone231BParityFixtures.AssignSequentialIds(candles);
        var evalIndex = candles.Count - 1;
        var evaluationTimeUtc = candles[evalIndex].CloseTimeUtc;
        var snapshots = Milestone231BParityFixtures.BuildRangingSnapshots(candles);
        var regime = DeterministicMarketRegimeClassifier.Classify(snapshots[candles[evalIndex].Id], candles[evalIndex]);
        Assert.Equal(MarketRegime.Ranging, regime);

        var parameters = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };
        var plugin = new MomoVolatilityRangeReversionStrategy();
        var productionHtf = StrategyHigherTimeframeSupport.ResolveGeneralHigherTimeframe(Timeframe.M5);
        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = productionHtf,
            MarketRegime = regime,
            Candles = candles,
            IndicatorSnapshot = snapshots[candles[evalIndex].Id],
            StrategyParameters = parameters,
            EvaluatedAtUtc = evaluationTimeUtc,
            CurrentCandleIndex = evalIndex
        });
        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.False(string.IsNullOrWhiteSpace(directFp));

        var from = candles[evalIndex].OpenTimeUtc;
        var to = from.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = [evalIndex],
            WarmupCandleCount = 0
        };
        var run = Milestone231BParityFixtures.CreateRun(
            961, StrategyCodes.MomoVolatilityRangeReversion, "5m", from, to,
            MomoVolatilityRangeReversionStrategy.Version);
        var labCandidates = new List<StrategyResearchCandidate>();
        var runner = Milestone231BParityFixtures.CreateRunner(
            run, plugin, StrategyCode.MomoVolatilityRangeReversion,
            MomoVolatilityRangeReversionStrategy.Version, dataset, labCandidates);
        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BParityTests"
        });
        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.NotEmpty(labCandidates);
        var lab = Assert.Single(labCandidates, c => c.SetupFingerprint == directFp);

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = Milestone231BParityFixtures.CreateBacktestEngine(recording, parameters);
        var prepared = new PreparedStrategy
        {
            Strategy = new Strategy
            {
                Id = 42,
                Code = StrategyCode.MomoVolatilityRangeReversion,
                Name = plugin.Name,
                IsEnabled = true,
                Version = MomoVolatilityRangeReversionStrategy.Version
            },
            Plugin = plugin
        };
        var backtestDataset = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "ETHUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = [evalIndex],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>()
        };
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            backtestDataset,
            [prepared],
            evaluationIndex: 0);

        var backtestResult = Assert.Single(recording.Results);
        var capture = Assert.Single(recording.Capture.Records);
        ParityAssertionHelper.AssertPositiveEntryParity(direct, lab, backtestResult, new ParityAssertionHelper.PositiveParityEvidence
        {
            Capture = capture,
            ExpectedRegime = regime,
            ExpectedHigherTimeframe = productionHtf,
            ExpectedTimeframe = Timeframe.M5,
            ExpectedSymbol = "ETHUSDT",
            ExpectedSymbolId = 1,
            ExpectedExchangeId = 1,
            ExpectedEvaluationTimestamp = evaluationTimeUtc,
            ExpectedCurrentCandleIndex = evalIndex,
            ExpectedExecutionCandleIds = candles.Take(evalIndex + 1).Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = Array.Empty<long>()
        });
    }

    [Fact]
    public async Task CrossPath_Psbr_DirectLabBacktest_IdenticalAtSameT()
    {
        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var evalIndex = candles.Count - 1;
        var evaluationTimeUtc = candles[evalIndex].CloseTimeUtc;
        var parameters = new Dictionary<string, string> { ["__seenFingerprints"] = "[]" };
        var plugin = new PriceStructureBreakoutRetestStrategy();
        var productionHtf = StrategyHigherTimeframeSupport.ResolveGeneralHigherTimeframe(Timeframe.M5);
        var regime = DeterministicMarketRegimeClassifier.Classify(null, candles[evalIndex]);

        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = productionHtf,
            MarketRegime = regime,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = parameters,
            EvaluatedAtUtc = evaluationTimeUtc,
            CurrentCandleIndex = evalIndex
        });
        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.True(StrategyLabRunner.IsCanonicalSetupFingerprint(directFp));
        Assert.True(Milestone231BParityFixtures.HasStrengthBreakdown(direct.RawDataJson ?? "{}"));

        var from = candles[evalIndex].OpenTimeUtc;
        var to = from.AddMinutes(5);
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [evalIndex],
            WarmupCandleCount = 0
        };
        var run = Milestone231BParityFixtures.CreateRun(
            962, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion);
        var labCandidates = new List<StrategyResearchCandidate>();
        var runner = Milestone231BParityFixtures.CreateRunner(
            run, plugin, StrategyCode.PriceStructureBreakoutRetest,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion, dataset, labCandidates);
        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BParityTests"
        });
        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.NotEmpty(labCandidates);
        var lab = Assert.Single(labCandidates, c => c.SetupFingerprint == directFp);

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = Milestone231BParityFixtures.CreateBacktestEngine(recording, parameters);
        var prepared = new PreparedStrategy
        {
            Strategy = new Strategy
            {
                Id = 42,
                Code = StrategyCode.PriceStructureBreakoutRetest,
                Name = plugin.Name,
                IsEnabled = true,
                Version = PriceStructureBreakoutRetestEvaluator.StrategyVersion
            },
            Plugin = plugin
        };
        var backtestDataset = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = candles,
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [evalIndex],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>()
        };
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            backtestDataset,
            [prepared],
            evaluationIndex: 0);

        var backtestResult = Assert.Single(recording.Results);
        var capture = Assert.Single(recording.Capture.Records);
        ParityAssertionHelper.AssertPositiveEntryParity(direct, lab, backtestResult, new ParityAssertionHelper.PositiveParityEvidence
        {
            Capture = capture,
            ExpectedRegime = regime,
            ExpectedHigherTimeframe = productionHtf,
            ExpectedTimeframe = Timeframe.M5,
            ExpectedSymbol = "BTCUSDT",
            ExpectedSymbolId = 1,
            ExpectedExchangeId = 1,
            ExpectedEvaluationTimestamp = evaluationTimeUtc,
            ExpectedCurrentCandleIndex = evalIndex,
            ExpectedExecutionCandleIds = candles.Take(evalIndex + 1).Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = Array.Empty<long>()
        });
    }

    private static StrategyContext BuildAdaptiveContext(
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        IReadOnlyDictionary<string, string> parameters,
        DateTime evaluatedAtUtc)
    {
        var snapshot = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf).GetValueOrDefault(ltf[^1].Id);
        var regime = DeterministicMarketRegimeClassifier.Classify(snapshot, ltf[^1]);
        return new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            HigherTimeframeCandles = htf,
            MarketRegime = regime,
            Candles = ltf,
            IndicatorSnapshot = snapshot,
            StrategyParameters = parameters,
            EvaluatedAtUtc = evaluatedAtUtc,
            CurrentCandleIndex = ltf.Count - 1
        };
    }

    private static async Task<List<StrategyResearchCandidate>> RunAdaptiveLabAsync(
        long runId,
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        DateTime from,
        DateTime to,
        IReadOnlyList<int> evalIndices)
    {
        var dataset = new StrategyLabDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
            EvaluationIndices = evalIndices,
            WarmupCandleCount = 0,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = htf
            }
        };
        var run = Milestone231BParityFixtures.CreateRun(
            runId, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "5m", from, to);
        var persisted = new List<StrategyResearchCandidate>();
        var runner = Milestone231BParityFixtures.CreateRunner(
            run,
            new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy(),
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            dataset,
            persisted);
        await runner.ExecuteAsync(run.Id, new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
            CallerComponent = "Milestone231BParityTests"
        });
        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        return persisted;
    }
}
