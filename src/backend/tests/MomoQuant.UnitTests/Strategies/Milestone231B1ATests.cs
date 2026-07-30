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
using MomoQuant.Domain.ValidationLab;
using Moq;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1B1A — partition-owned HTF, complete HTF evidence, mixed fingerprint reconciliation,
/// catalog metadata without fabricated versions, rejection parity.
/// </summary>
public sealed class Milestone231B1ATests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task B1A_MixedValidAndMalformedFingerprint_Reconciles()
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

        var run = CreateRun(23150, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to);
        var sink = new List<StrategyResearchCandidate>();
        var plugin = new MixedFingerprintStrategy(validEntries: 1, malformedEntries: 3);
        await CreateRunner(run, plugin, StrategyCode.PriceStructureBreakoutRetest, "1.1.0", dataset, sink)
            .ExecuteAsync(run.Id, GeneralResearchContext(dataset));

        Assert.Equal(StrategyLabRunStatus.Failed, run.Status);
        Assert.Empty(sink);
        using var doc = JsonDocument.Parse(run.ResultSummaryJson);
        var funnel = doc.RootElement.GetProperty("rejectionFunnel");
        Assert.Equal(4, funnel.GetProperty("evaluations").GetInt32());
        Assert.Equal(1, funnel.GetProperty("entryConfirmed").GetInt32());
        Assert.Equal(3, funnel.GetProperty("counts").GetProperty("MissingSetupFingerprint").GetInt32());
        Assert.True(funnel.GetProperty("reconciled").GetBoolean());
    }

    [Fact]
    public void B1A_CallerSuppliedHtf_RejectedAsUntrusted()
    {
        var (scope, request) = CreateAdaptiveScopeRequest(
            htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1)]);
        request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = request.SymbolId,
            SymbolName = request.SymbolName,
            Timeframe = request.Timeframe,
            EvaluationFromUtc = request.EvaluationFromUtc,
            EvaluationToExclusiveUtc = request.EvaluationToExclusiveUtc,
            WarmupCandleCount = request.WarmupCandleCount,
            CallerComponent = request.CallerComponent,
            StrategyCode = request.StrategyCode,
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = [HtfCandle(99, Timeframe.H1, Start, Start.AddHours(1), 1, 1)]
            }
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.UntrustedCallerHtf, ex.DenialCode);
        Assert.Contains(scope.AccessLog, a =>
            a.WasDenied
            && a.DenialCode == ValidationCandlePartitionDenialCodes.UntrustedCallerHtf
            && a.AccessPurpose == ValidationCandleAccessPurpose.HigherTimeframeAccess);
    }

    [Fact]
    public void B1A_ScopeOwnedHtf_Materializes()
    {
        var valid = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        var (scope, request) = CreateAdaptiveScopeRequest(htf: [valid]);
        var dataset = scope.CreateStrategyLabDataset(request);
        Assert.True(dataset.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.H1));
        Assert.Single(dataset.HigherTimeframeSeriesByTimeframe[Timeframe.H1]);
    }

    [Fact]
    public void B1A_SpoofedStrategyIdentity_Rejected()
    {
        var (scope, request) = CreateAdaptiveScopeRequest(
            htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1)]);
        request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = request.SymbolId,
            SymbolName = request.SymbolName,
            Timeframe = request.Timeframe,
            EvaluationFromUtc = request.EvaluationFromUtc,
            EvaluationToExclusiveUtc = request.EvaluationToExclusiveUtc,
            WarmupCandleCount = request.WarmupCandleCount,
            CallerComponent = request.CallerComponent,
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest
        };

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(ValidationCandlePartitionDenialCodes.SpoofedStrategyIdentity, ex.DenialCode);
        Assert.Contains(scope.AccessLog, a =>
            a.WasDenied && a.DenialCode == ValidationCandlePartitionDenialCodes.SpoofedStrategyIdentity);
    }

    [Fact]
    public void B1A_AllowedHtfEvidence_UsesPlannedRange_AndAuditLinkage()
    {
        var auditId = Guid.NewGuid();
        var valid = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        var (scope, request) = CreateAdaptiveScopeRequest(htf: [valid], boundAuditExecutionId: auditId);
        _ = scope.CreateStrategyLabDataset(request);

        var htf = Assert.Single(scope.AccessLog, a =>
            a.AccessPurpose == ValidationCandleAccessPurpose.HigherTimeframeAccess && !a.WasDenied);
        Assert.Equal(Start, htf.RequestedStartUtc);
        Assert.Equal(Start.AddHours(2), htf.RequestedEndUtc);
        Assert.Equal(valid.OpenTimeUtc, htf.ReturnedStartUtc);
        Assert.Equal(valid.CloseTimeUtc, htf.ReturnedEndUtc);
        Assert.Equal(1, htf.ReturnedCandleCount);
        Assert.False(string.IsNullOrWhiteSpace(htf.CandleContentFingerprint));
        Assert.Equal(auditId, htf.AuditExecutionId);
        Assert.Equal(scope.ScopeExecutionId, htf.ScopeExecutionId);
        Assert.StartsWith("HTF:", htf.DatasetPartition, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("closeBeyond")]
    [InlineData("openCandle")]
    [InlineData("wrongSymbol")]
    [InlineData("wrongExchange")]
    [InlineData("wrongTimeframe")]
    [InlineData("unordered")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void B1A_DeniedHtfEvidence_EachRejectionClass(string kind)
    {
        ValidationTrainingCandleScope scope;
        ValidationDatasetMaterializationRequest request;
        string expectedCode;

        switch (kind)
        {
            case "closeBeyond":
                (scope, request) = CreateAdaptiveScopeRequest(
                    htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(3), 1, 1)]);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfCloseBeyondBoundary;
                break;
            case "openCandle":
                var open = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
                open.IsClosed = false;
                (scope, request) = CreateAdaptiveScopeRequest(htf: [open]);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfOpenCandle;
                break;
            case "wrongSymbol":
                (scope, request) = CreateAdaptiveScopeRequest(
                    htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 99, 1)]);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfWrongSymbol;
                break;
            case "wrongExchange":
                (scope, request) = CreateAdaptiveScopeRequest(
                    htf: [HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 99)]);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfWrongExchange;
                break;
            case "wrongTimeframe":
                (scope, request) = CreateAdaptiveScopeRequest(
                    htf: [HtfCandle(1, Timeframe.H4, Start, Start.AddHours(4), 1, 1)],
                    htfKey: Timeframe.H1);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfWrongTimeframe;
                break;
            case "unordered":
                (scope, request) = CreateAdaptiveScopeRequest(htf:
                [
                    HtfCandle(1, Timeframe.H1, Start.AddHours(1), Start.AddHours(2), 1, 1),
                    HtfCandle(2, Timeframe.H1, Start, Start.AddHours(1), 1, 1)
                ]);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfUnordered;
                break;
            case "duplicate":
                (scope, request) = CreateAdaptiveScopeRequest(htf:
                [
                    HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1),
                    HtfCandle(2, Timeframe.H1, Start, Start.AddHours(1), 1, 1)
                ]);
                expectedCode = ValidationCandlePartitionDenialCodes.HtfDuplicate;
                break;
            default:
                (scope, request) = CreateAdaptiveScopeRequest();
                expectedCode = ValidationCandlePartitionDenialCodes.MissingPartitionHtf;
                break;
        }

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        Assert.Equal(expectedCode, ex.DenialCode);
        var denied = Assert.Single(scope.AccessLog, a => a.WasDenied && a.DenialCode == expectedCode);
        Assert.False(string.IsNullOrWhiteSpace(denied.DenialReason));
        Assert.Equal(ValidationCandleAccessPurpose.HigherTimeframeAccess, denied.AccessPurpose);
    }

    [Fact]
    public async Task B1A_HtfEvidence_SurvivesFlushRestart()
    {
        var valid = HtfCandle(1, Timeframe.H1, Start, Start.AddHours(1), 1, 1);
        // Legacy flush path (no BoundAuditExecutionId) — durable audit path needs execution repos.
        var (scope, request) = CreateAdaptiveScopeRequest(htf: [valid]);
        _ = scope.CreateStrategyLabDataset(request);

        var allowed = Assert.Single(scope.AccessLog, a =>
            a.AccessPurpose == ValidationCandleAccessPurpose.HigherTimeframeAccess && !a.WasDenied);
        var eventId = allowed.AccessEventId;
        var fingerprint = allowed.CandleContentFingerprint;
        var returnedEnd = allowed.ReturnedEndUtc;

        var repo = new CapturingAuditRepo();
        var recorder = new ValidationCandleAccessRecorder(repo);
        var flush = await recorder.FlushAsync(scope);
        Assert.True(flush.IsFullyConfirmed);
        Assert.NotNull(allowed.PersistedAtUtc);

        var mapped = Assert.Single(repo.LastSubmitted!, e => e.AccessEventId == eventId);
        Assert.Equal("HigherTimeframeAccess", mapped.AccessPurpose);
        Assert.Equal(fingerprint is { Length: > 64 } ? fingerprint[..64] : fingerprint, mapped.CandleContentFingerprint);
        Assert.Equal(returnedEnd, mapped.ReturnedEndUtc);
        Assert.Equal(Start, mapped.RequestedStartUtc);
        Assert.Equal(Start.AddHours(2), mapped.RequestedEndUtc);
        Assert.False(mapped.WasDenied);

        // Restart path: same AccessEventId retained on scope; second flush is no-op.
        var noop = await recorder.FlushAsync(scope);
        Assert.Empty(noop.RequestedEventIds);
        Assert.Equal(1, repo.PersistCalls);
    }

    [Fact]
    public async Task B1A_DeniedHtfEvidence_SurvivesFlush()
    {
        var (scope, request) = CreateAdaptiveScopeRequest();
        Assert.Throws<ValidationCandlePartitionViolationException>(() => scope.CreateStrategyLabDataset(request));
        var denied = Assert.Single(scope.AccessLog, a =>
            a.WasDenied && a.DenialCode == ValidationCandlePartitionDenialCodes.MissingPartitionHtf);

        var repo = new CapturingAuditRepo();
        var recorder = new ValidationCandleAccessRecorder(repo);
        var flush = await recorder.FlushAsync(scope);
        Assert.True(flush.IsFullyConfirmed);

        var mapped = Assert.Single(repo.LastSubmitted!, e => e.AccessEventId == denied.AccessEventId);
        Assert.True(mapped.WasDenied);
        Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, mapped.DenialCode);
        Assert.Equal(denied.DenialReason, mapped.DenialReason);
    }

    [Fact]
    public async Task B1A_GetLabStrategies_OmitsBlankVersion_NoFabricate100()
    {
        var requirements = new List<StrategyDataRequirementDto>
        {
            BuildRequirementDto(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, ["5m"], [], 600),
            BuildRequirementDto(StrategyCodes.PriceStructureBreakoutRetest, ["5m"], [], 100),
            BuildRequirementDto(StrategyCodes.MomoVolatilityRangeReversion, ["5m"], [], 158)
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
            .ReturnsAsync(Entity(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, "Adaptive", MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version));
        strategyRepo.Setup(s => s.GetByCodeAsync(StrategyCode.PriceStructureBreakoutRetest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity(StrategyCode.PriceStructureBreakoutRetest, "PSBR", "")); // blank → omit
        strategyRepo.Setup(s => s.GetByCodeAsync(StrategyCode.MomoVolatilityRangeReversion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity(StrategyCode.MomoVolatilityRangeReversion, "Range", MomoVolatilityRangeReversionStrategy.Version));

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
        Assert.Equal(2, result.Data!.Count);
        Assert.DoesNotContain(result.Data, s => s.Code == StrategyCodes.PriceStructureBreakoutRetest);
        Assert.DoesNotContain(result.Data, s => s.Version == "1.0.0" && s.Code == StrategyCodes.PriceStructureBreakoutRetest);
        Assert.Contains("Omitted", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.All(result.Data, s => Assert.False(string.IsNullOrWhiteSpace(s.Version)));
        Assert.DoesNotContain(result.Data, s => s.Version == "1.0.0" && s.Code != StrategyCodes.PriceStructureBreakoutRetest
            && s.Version != MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version
            && s.Version != MomoVolatilityRangeReversionStrategy.Version);
    }

    [Fact]
    public async Task B1A_RejectionParity_Adaptive_NoCandidate()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        Milestone231BParityFixtures.AssignSequentialIds(ltf);
        Milestone231BParityFixtures.AssignSequentialIds(htf, 10_000);
        var evalIndex = Math.Min(20, ltf.Count / 4);
        await AssertRejectionParityAdaptiveAsync(ltf, htf, evalIndex);
    }

    [Fact]
    public async Task B1A_RejectionParity_Range_NoCandidate()
    {
        var full = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var candles = full.Count <= 600 ? full : full.TakeLast(600).ToList();
        Milestone231BParityFixtures.AssignSequentialIds(candles);
        var evalIndex = candles.Count - 1;
        var evaluationTimeUtc = candles[evalIndex].CloseTimeUtc;
        // Trending snapshots force non-ranging regime → no entry.
        var snapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(candles);
        var regime = DeterministicMarketRegimeClassifier.Classify(snapshots[candles[evalIndex].Id], candles[evalIndex]);
        Assert.NotEqual(MarketRegime.Ranging, regime);
        var rawDataContract = ParityEvidenceContracts.CreateRangeRejectionEnvelopeContract(
            StrategyCodes.MomoVolatilityRangeReversion,
            MomoVolatilityRangeReversionStrategy.Version,
            MomoVolatilityRangeRejectionCodes.TrendFilterFailed,
            symbolId: 1,
            symbol: "ETHUSDT",
            timeframe: "5m",
            marketRegime: regime.ToString(),
            evaluatedAtUtc: evaluationTimeUtc);

        var parameters = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };
        var plugin = new MomoVolatilityRangeReversionStrategy();
        var rangeHtf = StrategyHigherTimeframeSupport.ResolveGeneralHigherTimeframe(Timeframe.M5);
        var directContext = new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = rangeHtf,
            MarketRegime = regime,
            Candles = candles,
            IndicatorSnapshot = snapshots[candles[evalIndex].Id],
            StrategyParameters = parameters,
            EvaluatedAtUtc = candles[evalIndex].CloseTimeUtc,
            CurrentCandleIndex = evalIndex
        };
        var direct = plugin.Evaluate(directContext);
        Assert.NotEqual(SignalType.Entry, direct.SignalType);

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
            23161, StrategyCodes.MomoVolatilityRangeReversion, "5m", from, to,
            MomoVolatilityRangeReversionStrategy.Version);
        var labCandidates = new List<StrategyResearchCandidate>();
        var recordingPlugin = new RecordingTradingStrategyDecorator(plugin);
        await Milestone231BParityFixtures.CreateRunner(
                run, recordingPlugin, StrategyCode.MomoVolatilityRangeReversion,
                MomoVolatilityRangeReversionStrategy.Version, dataset, labCandidates)
            .ExecuteAsync(run.Id, new StrategyLabExecutionContext
            {
                ExecutionPurpose = ExecutionPurpose.GeneralResearch,
                AllowCoverageImport = true,
                CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
                CallerComponent = "Milestone231B1ATests"
            });
        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.Empty(labCandidates);

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
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            new BacktestDataset
            {
                SymbolId = 1,
                SymbolName = "ETHUSDT",
                Timeframe = Timeframe.M5,
                Candles = candles,
                IndicatorSnapshots = snapshots,
                EvaluationIndices = [evalIndex],
                HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>()
            },
            [prepared],
            evaluationIndex: 0);

        var backtest = Assert.Single(recording.Results);
        ParityAssertionHelper.AssertRejectionThreePathParity(
            directContext,
            direct,
            backtest,
            new ParityAssertionHelper.RejectionThreePathEvidence
            {
                LabEvaluations = recordingPlugin.Evaluations,
                BacktestCaptures = recording.Capture.Records,
                LabCandidates = labCandidates,
                PersistedLabRun = run,
                ExpectedStrategyLabRunId = run.Id,
                ExpectedStrategyCode = StrategyCode.MomoVolatilityRangeReversion,
                ExpectedRegime = regime,
                ExpectedLabRejectionCode = MomoVolatilityRangeRejectionCodes.TrendFilterFailed,
                LabResultSummaryJson = run.ResultSummaryJson!,
                ExpectedEvaluationTimestamp = evaluationTimeUtc,
                ExpectedCurrentCandleIndex = evalIndex,
                ExpectedExecutionCandleIds = candles.Take(evalIndex + 1).Select(c => c.Id).ToArray(),
                ExpectedHtfCandleIds = Array.Empty<long>(),
                ExpectedExchangeId = 1,
                ExpectedSymbolId = 1,
                ExpectedSymbol = "ETHUSDT",
                ExpectedTimeframe = Timeframe.M5,
                ExpectedHigherTimeframe = rangeHtf,
                ExpectedParameters = parameters,
                ExpectedIndicatorSnapshot = snapshots[candles[evalIndex].Id],
                Fingerprint = ParityEvidenceContracts.RejectionFingerprintAbsent,
                RawDataContract = rawDataContract,
                RequiredRawDataJsonProperties =
                    ["strategyCode", "version", "reason", "symbolId", "symbol", "timeframe", "marketRegime", "evaluatedAtUtc"]
            });
    }

    [Fact]
    public async Task B1A_RejectionParity_Psbr_NoCandidate()
    {
        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var evalIndex = 5; // before breakout/retest forms
        var evaluationTimeUtc = candles[evalIndex].CloseTimeUtc;
        var parameters = new Dictionary<string, string>
        {
            ["swingLeftBars"] = "2",
            ["swingRightBars"] = "2",
            ["minSwingDistanceBars"] = "3",
            ["useWicksForSwing"] = "true",
            ["minBreakoutClosePercent"] = "0",
            ["breakoutMustCloseBeyondLevel"] = "true",
            ["maxRetestBars"] = "20",
            ["retestTolerancePercent"] = "0.15",
            ["retestToleranceMode"] = "Percent",
            ["retestToleranceAtrMultiplier"] = "0.25",
            ["allowWickThroughLevel"] = "true",
            ["maxRetestPenetrationPercent"] = "0.30",
            ["confirmationMode"] = "ReactionClose",
            ["fixedRewardRisk"] = "2.0",
            ["stopBufferPercent"] = "0.05",
            ["__seenFingerprints"] = "[]"
        };
        var rawDataContract = ParityAssertionHelper.RawDataJsonContract.Create(
            ParityAssertionHelper.RawDataJsonRootState.Null);
        var plugin = new PriceStructureBreakoutRetestStrategy();
        var productionHtf = StrategyHigherTimeframeSupport.ResolveGeneralHigherTimeframe(Timeframe.M5);
        var regime = DeterministicMarketRegimeClassifier.Classify(null, candles[evalIndex]);

        var directContext = new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = productionHtf,
            MarketRegime = regime,
            Candles = candles.Take(evalIndex + 1).ToList(),
            IndicatorSnapshot = null,
            StrategyParameters = parameters,
            EvaluatedAtUtc = evaluationTimeUtc,
            CurrentCandleIndex = evalIndex
        };
        var direct = plugin.Evaluate(directContext);
        Assert.NotEqual(SignalType.Entry, direct.SignalType);

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
            23162, StrategyCodes.PriceStructureBreakoutRetest, "5m", from, to,
            PriceStructureBreakoutRetestEvaluator.StrategyVersion);
        var labCandidates = new List<StrategyResearchCandidate>();
        var recordingPlugin = new RecordingTradingStrategyDecorator(plugin);
        await Milestone231BParityFixtures.CreateRunner(
                run, recordingPlugin, StrategyCode.PriceStructureBreakoutRetest,
                PriceStructureBreakoutRetestEvaluator.StrategyVersion, dataset, labCandidates)
            .ExecuteAsync(run.Id, new StrategyLabExecutionContext
            {
                ExecutionPurpose = ExecutionPurpose.GeneralResearch,
                AllowCoverageImport = true,
                CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
                CallerComponent = "Milestone231B1ATests"
            });
        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.Empty(labCandidates);

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
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            new BacktestDataset
            {
                SymbolId = 1,
                SymbolName = "BTCUSDT",
                Timeframe = Timeframe.M5,
                Candles = candles,
                IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
                EvaluationIndices = [evalIndex],
                HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>()
            },
            [prepared],
            evaluationIndex: 0);

        var backtest = Assert.Single(recording.Results);
        Assert.False(string.IsNullOrWhiteSpace(direct.Reason));
        ParityAssertionHelper.AssertRejectionThreePathParity(
            directContext,
            direct,
            backtest,
            new ParityAssertionHelper.RejectionThreePathEvidence
            {
                LabEvaluations = recordingPlugin.Evaluations,
                BacktestCaptures = recording.Capture.Records,
                LabCandidates = labCandidates,
                PersistedLabRun = run,
                ExpectedStrategyLabRunId = run.Id,
                ExpectedStrategyCode = StrategyCode.PriceStructureBreakoutRetest,
                ExpectedRegime = regime,
                ExpectedLabRejectionCode = PriceStructureRejectionCodes.InsufficientData,
                LabResultSummaryJson = run.ResultSummaryJson!,
                ExpectedEvaluationTimestamp = evaluationTimeUtc,
                ExpectedCurrentCandleIndex = evalIndex,
                ExpectedExecutionCandleIds = candles.Take(evalIndex + 1).Select(c => c.Id).ToArray(),
                ExpectedHtfCandleIds = Array.Empty<long>(),
                ExpectedExchangeId = 1,
                ExpectedSymbolId = 1,
                ExpectedSymbol = "BTCUSDT",
                ExpectedTimeframe = Timeframe.M5,
                ExpectedHigherTimeframe = productionHtf,
                ExpectedParameters = parameters,
                ExpectedIndicatorSnapshot = null,
                Fingerprint = ParityEvidenceContracts.RejectionFingerprintAbsent,
                RawDataContract = rawDataContract,
                RequiredRawDataJsonProperties = Array.Empty<string>()
            });
    }

    private static async Task AssertRejectionParityAdaptiveAsync(
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        int evalIndex)
    {
        var rawDataContract = ParityAssertionHelper.RawDataJsonContract.Create(
            ParityAssertionHelper.RawDataJsonRootState.Null);
        var evaluationTimeUtc = ltf[evalIndex].CloseTimeUtc;
        var visibleHtf = StrategyHigherTimeframeSupport.SliceHigherTimeframeCandles(
            new Dictionary<Timeframe, IReadOnlyList<Candle>> { [Timeframe.H1] = htf },
            Timeframe.H1,
            evaluationTimeUtc);
        var parameters = new Dictionary<string, string>(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract())
        {
            ["__seenFingerprints"] = "[]"
        };
        var plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var snapshot = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf).GetValueOrDefault(ltf[evalIndex].Id);
        var regime = DeterministicMarketRegimeClassifier.Classify(snapshot, ltf[evalIndex]);
        var directContext = new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            HigherTimeframeCandles = visibleHtf,
            MarketRegime = regime,
            Candles = ltf.Take(evalIndex + 1).ToList(),
            IndicatorSnapshot = snapshot,
            StrategyParameters = parameters,
            EvaluatedAtUtc = evaluationTimeUtc,
            CurrentCandleIndex = evalIndex
        };
        var direct = plugin.Evaluate(directContext);
        Assert.NotEqual(SignalType.Entry, direct.SignalType);

        var from = ltf[evalIndex].OpenTimeUtc;
        var to = from.AddMinutes(5);
        var dataset = new StrategyLabDataset
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
        var run = Milestone231BParityFixtures.CreateRun(
            23160, StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, "5m", from, to);
        var labCandidates = new List<StrategyResearchCandidate>();
        var recordingPlugin = new RecordingTradingStrategyDecorator(plugin);
        await Milestone231BParityFixtures.CreateRunner(
                run, recordingPlugin, StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version, dataset, labCandidates)
            .ExecuteAsync(run.Id, new StrategyLabExecutionContext
            {
                ExecutionPurpose = ExecutionPurpose.GeneralResearch,
                AllowCoverageImport = true,
                CandleDataSource = new Milestone231BParityFixtures.FixedStrategyLabCandleDataSource(dataset),
                CallerComponent = "Milestone231B1ATests"
            });
        Assert.Equal(StrategyLabRunStatus.Completed, run.Status);
        Assert.Empty(labCandidates);

        var recording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var engine = Milestone231BParityFixtures.CreateBacktestEngine(recording, parameters);
        await engine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            new BacktestDataset
            {
                SymbolId = 1,
                SymbolName = "BTCUSDT",
                Timeframe = Timeframe.M5,
                Candles = ltf,
                IndicatorSnapshots = Milestone231BParityFixtures.BuildTrendingSnapshots(ltf),
                EvaluationIndices = [evalIndex],
                HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>> { [Timeframe.H1] = htf }
            },
            [ClosedHtfCaptureHarness.CreateAdaptivePrepared()],
            evaluationIndex: 0);

        var backtest = Assert.Single(recording.Results);
        Assert.False(string.IsNullOrWhiteSpace(direct.Reason));
        ParityAssertionHelper.AssertRejectionThreePathParity(
            directContext,
            direct,
            backtest,
            new ParityAssertionHelper.RejectionThreePathEvidence
            {
                LabEvaluations = recordingPlugin.Evaluations,
                BacktestCaptures = recording.Capture.Records,
                LabCandidates = labCandidates,
                PersistedLabRun = run,
                ExpectedStrategyLabRunId = run.Id,
                ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                ExpectedRegime = regime,
                ExpectedLabRejectionCode = MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable,
                LabResultSummaryJson = run.ResultSummaryJson!,
                ExpectedEvaluationTimestamp = evaluationTimeUtc,
                ExpectedCurrentCandleIndex = evalIndex,
                ExpectedExecutionCandleIds = ltf.Take(evalIndex + 1).Select(c => c.Id).ToArray(),
                ExpectedHtfCandleIds = visibleHtf.Select(c => c.Id).ToArray(),
                ExpectedExchangeId = 1,
                ExpectedSymbolId = 1,
                ExpectedSymbol = "BTCUSDT",
                ExpectedTimeframe = Timeframe.M5,
                ExpectedHigherTimeframe = Timeframe.H1,
                ExpectedParameters = parameters,
                ExpectedIndicatorSnapshot = snapshot,
                Fingerprint = ParityEvidenceContracts.RejectionFingerprintAbsent,
                RawDataContract = rawDataContract,
                RequiredRawDataJsonProperties = ParityEvidenceContracts.AdaptiveRejectionRawData
            });
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
            CallerComponent = "Milestone231B1ATests",
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
            Name = $"m231b1a-{id}",
            StrategyCode = code,
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
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
            CallerComponent = "Milestone231B1ATests"
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
                BuildRequirementDto(code.ToCode(), [run.Timeframe], [], 0)));

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
        IReadOnlyList<string> allowed,
        IReadOnlyList<string> htf,
        int warmup) =>
        new()
        {
            StrategyId = 1,
            StrategyCode = code,
            StrategyName = code,
            PreferredExecutionTimeframe = allowed[0],
            AllowedExecutionTimeframes = allowed,
            RequiredDataTimeframes = allowed,
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

    private sealed class MixedFingerprintStrategy : StrategyBase
    {
        private readonly int _validEntries;
        private readonly int _malformedEntries;
        private int _entries;

        public MixedFingerprintStrategy(int validEntries, int malformedEntries)
        {
            _validEntries = validEntries;
            _malformedEntries = malformedEntries;
        }

        public override StrategyCode Code => StrategyCode.PriceStructureBreakoutRetest;
        public override string Name => "Mixed Fingerprint";
        public override string Description => "Test double";
        public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
            [MarketRegime.Breakout, MarketRegime.Trending, MarketRegime.Ranging];
        public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
            [Timeframe.M5, Timeframe.M15];

        public override StrategySignalResult Evaluate(StrategyContext context)
        {
            _entries++;
            var useValid = _entries <= _validEntries;
            var fingerprint = useValid
                ? Application.Strategies.PriceStructure.SetupFingerprintHasher.Hash($"valid-{_entries}")
                : string.Empty;

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

    private sealed class CapturingAuditRepo : IValidationCandleAccessAuditRepository
    {
        private static readonly ValidationAccessPayloadCanonicalizer Canonicalizer = new();
        public int PersistCalls { get; private set; }
        public IReadOnlyList<ValidationCandleAccessAudit>? LastSubmitted { get; private set; }

        public Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            PersistCalls++;
            LastSubmitted = audits;
            var ids = audits.Select(a => a.AccessEventId).ToList();
            var hashes = audits.ToDictionary(
                a => a.AccessEventId,
                a => a.AccessPayloadHash ?? Canonicalizer.ComputeSha256(a));
            return Task.FromResult(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = ids,
                ConfirmedMatchingEventIds = ids,
                ConfirmedPayloadHashes = hashes,
                CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
                CompletedAtUtc = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long validationExperimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(Array.Empty<ValidationCandleAccessAudit>());
    }
}
