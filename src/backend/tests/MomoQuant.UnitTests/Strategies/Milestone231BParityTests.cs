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

        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.H4,
            HigherTimeframe = Timeframe.D1,
            HigherTimeframeCandles = htf,
            MarketRegime = MarketRegime.Trending,
            Candles = ltf,
            IndicatorSnapshot = null,
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
        Assert.Equal(direct.Direction, lab.Direction);
        Assert.Equal(direct.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, lab.Target1);
        Assert.Equal(direct.Strength, ExtractStrengthFromStructure(lab.StructureJson) ?? direct.Strength);
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
        var detector = PriceStructureDetectorFactory.Create(StrategyCodes.PriceStructureBreakoutRetest);
        Assert.NotNull(detector);
        detector!.Initialize(new Dictionary<string, string>());

        PriceStructureCandidateDto? detectorCandidate = null;
        for (var i = 0; i < candles.Count; i++)
        {
            var slice = candles.Take(i + 1).ToList();
            var result = detector.ProcessCandle(slice, StrategyCodes.PriceStructureBreakoutRetest, 1, "5m");
            if (result.Candidate is not null)
            {
                detectorCandidate = result.Candidate;
            }
        }

        Assert.NotNull(detectorCandidate);
        Assert.Equal(TradeDirection.Long, detectorCandidate!.Direction);
        Assert.Equal(100.80m, detectorCandidate.EntryPrice);
        Assert.Equal(99.95m, detectorCandidate.StopLoss);
        Assert.Equal(102.50m, detectorCandidate.Target1);
        Assert.False(string.IsNullOrWhiteSpace(detectorCandidate.SetupFingerprint));
        Assert.Equal(candles.Count - 1, detectorCandidate.Structure.ConfirmationIndex);

        var pluginEval = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Breakout,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string> { ["__seenFingerprints"] = "[]" },
            EvaluatedAtUtc = candles[^1].CloseTimeUtc,
            CurrentCandleIndex = candles.Count - 1
        });
        Assert.Equal(SignalType.Entry, pluginEval.SignalType);
        Assert.Equal(detectorCandidate.EntryPrice, pluginEval.EntryPrice);
        Assert.Equal(detectorCandidate.StopLoss, pluginEval.SuggestedStopLoss);
        Assert.Equal(detectorCandidate.Target1, pluginEval.SuggestedTakeProfit);
        Assert.Equal(detectorCandidate.SetupFingerprint, StrategyLabRunner.ExtractFingerprint(pluginEval.RawDataJson ?? "{}"));

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
        var lab = Assert.Single(candidates, c => c.SetupFingerprint == detectorCandidate.SetupFingerprint);
        Assert.Equal(detectorCandidate.Direction, lab.Direction);
        Assert.Equal(detectorCandidate.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(detectorCandidate.StopLoss, lab.StopLoss);
        Assert.Equal(detectorCandidate.Target1, lab.Target1);
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
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
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
        var direct = plugin.Evaluate(BuildAdaptiveContext(ltf, visibleHtf, parameters, evaluationTimeUtc));
        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");

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
        Assert.Equal(SignalType.Entry, backtestResult.SignalType);
        Assert.Equal(direct.Direction, backtestResult.Direction);
        Assert.Equal(direct.EntryPrice, backtestResult.EntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, backtestResult.SuggestedStopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, backtestResult.SuggestedTakeProfit);
        Assert.Equal(direct.Strength, backtestResult.Strength);
        Assert.Equal(directFp, Milestone231BParityFixtures.ExtractFingerprint(backtestResult.RawDataJson));
        Assert.Equal(MarketRegime.Trending.ToString(), backtestResult.Regime);
        Assert.Equal(
            visibleHtf.Select(c => c.Id).ToArray(),
            capture.HigherTimeframeCandles.Select(c => c.Id).ToArray());

        Assert.Equal(direct.Direction, lab.Direction);
        Assert.Equal(direct.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, lab.Target1);
        Assert.Equal(directFp, lab.SetupFingerprint);
        Assert.Equal(backtestResult.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(Milestone231BParityFixtures.ExtractFingerprint(backtestResult.RawDataJson), lab.SetupFingerprint);
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
        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
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
        Assert.Equal(SignalType.Entry, backtestResult.SignalType);
        Assert.Equal(direct.Direction, backtestResult.Direction);
        Assert.Equal(direct.EntryPrice, backtestResult.EntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, backtestResult.SuggestedStopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, backtestResult.SuggestedTakeProfit);
        Assert.Equal(direct.Strength, backtestResult.Strength);
        Assert.Equal(directFp, Milestone231BParityFixtures.ExtractFingerprint(backtestResult.RawDataJson));
        Assert.Equal(MarketRegime.Ranging.ToString(), backtestResult.Regime);

        Assert.Equal(direct.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, lab.Target1);
        Assert.Equal(directFp, lab.SetupFingerprint);
        Assert.Equal(backtestResult.EntryPrice, lab.ProposedEntryPrice);
    }

    [Fact]
    public async Task CrossPath_Psbr_DirectLabBacktest_IdenticalAtSameT()
    {
        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var evalIndex = candles.Count - 1;
        var evaluationTimeUtc = candles[evalIndex].CloseTimeUtc;
        var parameters = new Dictionary<string, string> { ["__seenFingerprints"] = "[]" };
        var plugin = new PriceStructureBreakoutRetestStrategy();

        var direct = plugin.Evaluate(new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Breakout,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = parameters,
            EvaluatedAtUtc = evaluationTimeUtc,
            CurrentCandleIndex = evalIndex
        });
        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.False(string.IsNullOrWhiteSpace(directFp));

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
        Assert.Equal(SignalType.Entry, backtestResult.SignalType);
        Assert.Equal(direct.Direction, backtestResult.Direction);
        Assert.Equal(direct.EntryPrice, backtestResult.EntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, backtestResult.SuggestedStopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, backtestResult.SuggestedTakeProfit);
        Assert.Equal(directFp, Milestone231BParityFixtures.ExtractFingerprint(backtestResult.RawDataJson));

        Assert.Equal(direct.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, lab.Target1);
        Assert.Equal(directFp, lab.SetupFingerprint);
        Assert.Equal(backtestResult.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(Milestone231BParityFixtures.ExtractFingerprint(backtestResult.RawDataJson), lab.SetupFingerprint);
    }

    private static StrategyContext BuildAdaptiveContext(
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        IReadOnlyDictionary<string, string> parameters,
        DateTime evaluatedAtUtc) =>
        new()
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
            EvaluatedAtUtc = evaluatedAtUtc,
            CurrentCandleIndex = ltf.Count - 1
        };

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

    private static decimal? ExtractStrengthFromStructure(string structureJson)
    {
        if (string.IsNullOrWhiteSpace(structureJson))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(structureJson);
        if (doc.RootElement.TryGetProperty("strength", out var s) && s.TryGetDecimal(out var v))
        {
            return v;
        }

        if (doc.RootElement.TryGetProperty("strengthBreakdown", out var b)
            && b.TryGetProperty("total", out var t)
            && t.TryGetDecimal(out var total))
        {
            return total;
        }

        return null;
    }
}
