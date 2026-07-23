using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Research;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

public interface IStrategyLabRunner
{
    Task ExecuteAsync(long runId, StrategyLabExecutionContext executionContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibility overload for general Strategy Laboratory research only.
    /// Creates <see cref="ExecutionPurpose.GeneralResearch"/>. Must not be used by Validation Laboratory training.
    /// </summary>
    Task ExecuteAsync(long runId, CancellationToken cancellationToken = default);
}

public sealed class StrategyLabRunner : IStrategyLabRunner
{
    private const int DefaultWarmup = 100;
    private const decimal DefaultMinConfidence = 80m;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IStrategyLabRunRepository _runRepository;
    private readonly IStrategyResearchCandidateRepository _candidateRepository;
    private readonly StandardStrategyLabCandleDataSource _standardCandleDataSource;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IHistoricalCandleCoverageService _coverageService;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly StrategyLabRiskObserver _riskObserver;
    private readonly ICandidateConfidenceScorer _confidenceScorer;
    private readonly IStrategyLabCandleWindowFactory _candleWindowFactory;
    private readonly IResearchExecutionContextAccessor _executionContextAccessor;
    private readonly ILogger<StrategyLabRunner> _logger;

    public StrategyLabRunner(
        IStrategyLabRunRepository runRepository,
        IStrategyResearchCandidateRepository candidateRepository,
        IBacktestDataLoader dataLoader,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        IStrategyDataRequirementService requirementService,
        IHistoricalCandleCoverageService coverageService,
        IRiskRuleRepository riskRuleRepository,
        IRiskProfileRepository riskProfileRepository,
        PositionSizingService positionSizingService,
        ICandidateConfidenceScorer confidenceScorer,
        IStrategyLabCandleWindowFactory? candleWindowFactory = null,
        ILogger<StrategyLabRunner>? logger = null,
        StandardStrategyLabCandleDataSource? standardCandleDataSource = null,
        IResearchExecutionContextAccessor? executionContextAccessor = null)
    {
        _ = positionSizingService; // retained for DI compatibility; futures sizing is internal to risk observer
        _runRepository = runRepository;
        _candidateRepository = candidateRepository;
        _standardCandleDataSource = standardCandleDataSource ?? new StandardStrategyLabCandleDataSource(dataLoader);
        _strategyRepository = strategyRepository;
        _strategyRegistry = strategyRegistry;
        _requirementService = requirementService;
        _coverageService = coverageService;
        _riskRuleRepository = riskRuleRepository;
        _riskProfileRepository = riskProfileRepository;
        _riskObserver = new StrategyLabRiskObserver();
        _confidenceScorer = confidenceScorer;
        _candleWindowFactory = candleWindowFactory ?? new CandlePrefixViewStrategyLabCandleWindowFactory();
        _executionContextAccessor = executionContextAccessor ?? new ResearchExecutionContextAccessor();
        _logger = logger ?? NullLogger<StrategyLabRunner>.Instance;
    }

    public Task ExecuteAsync(long runId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(runId, StrategyLabExecutionContext.ForGeneralResearch(), cancellationToken);

    public async Task ExecuteAsync(
        long runId,
        StrategyLabExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        using var ambient = _executionContextAccessor.Enter(executionContext);

        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        HistoricalCandleCoverageResult? coverageDiagnostics = null;
        var isValidationTraining = executionContext.ExecutionPurpose == ExecutionPurpose.ValidationTraining;

        try
        {
            ValidateExecutionContext(executionContext);

            run.Status = isValidationTraining
                ? StrategyLabRunStatus.PreparingStrategy
                : StrategyLabRunStatus.CheckingCoverage;
            run.CurrentStage = isValidationTraining
                ? "Preparing validation training candles..."
                : "Checking candle coverage...";
            run.PercentComplete = 1m;
            run.StartedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await _runRepository.UpdateAsync(run, cancellationToken);

            if (!TimeframeNormalizer.TryNormalize(run.Timeframe, out var canonicalTimeframe)
                || !TimeframeParser.TryParse(canonicalTimeframe, out var parsedTimeframe))
            {
                await FailRunAsync(run, TimeframeNormalizer.UnsupportedTimeframeMessage(run.Timeframe), cancellationToken);
                return;
            }

            run.Timeframe = canonicalTimeframe;

            var strategyEnum = StrategyCodeExtensions.FromCode(run.StrategyCode);
            var strategyEntity = await _strategyRepository.GetByCodeAsync(strategyEnum, cancellationToken);
            var plugin = _strategyRegistry.GetByCode(strategyEnum);
            if (strategyEntity is null || plugin is null)
            {
                await FailRunAsync(run, "Strategy is not registered.", cancellationToken);
                return;
            }

            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(run.ParametersJson) ?? new Dictionary<string, string>();
            var warmup = await ResolveWarmupAsync(strategyEntity.Id, cancellationToken);

            if (isValidationTraining)
            {
                if (executionContext.AllowCoverageImport)
                {
                    throw new ValidationTrainingCoverageImportForbiddenException(
                        executionContext.ValidationExperimentId,
                        executionContext.TrainingBoundaryUtc,
                        executionContext.CallerComponent);
                }
            }
            else
            {
                run.Status = StrategyLabRunStatus.CheckingCoverage;
                run.CurrentStage = "Checking candle coverage...";
                run.PercentComplete = 2m;
                await _runRepository.UpdateAsync(run, cancellationToken);

                async Task OnCoverageProgressAsync(HistoricalCoverageProgress p, CancellationToken ct)
                {
                    run.CurrentStage = p.Message;
                    run.PercentComplete = p.PercentComplete;
                    run.Status = p.Stage switch
                    {
                        "ImportingCandles" => StrategyLabRunStatus.ImportingCandles,
                        "VerifyingCoverage" => StrategyLabRunStatus.VerifyingCoverage,
                        _ => StrategyLabRunStatus.CheckingCoverage
                    };
                    run.UpdatedAtUtc = DateTime.UtcNow;
                    await _runRepository.UpdateAsync(run, ct);
                }

                var allowImport = executionContext.AllowCoverageImport;
                var coverage = await _coverageService.EnsureCoverageAsync(
                    run.ExchangeId,
                    run.SymbolId,
                    canonicalTimeframe,
                    run.FromUtc,
                    run.ToUtc,
                    warmup,
                    allowAutoImport: allowImport,
                    OnCoverageProgressAsync,
                    cancellationToken);

                coverageDiagnostics = coverage.Data ?? coverageDiagnostics;
                if (!coverage.Succeeded)
                {
                    PersistCoverageDiagnostics(run, coverage.Data);
                    await FailRunAsync(run, coverage.ErrorMessage ?? "Candle coverage failed.", cancellationToken);
                    return;
                }

                coverageDiagnostics = coverage.Data;
            }

            run.Status = StrategyLabRunStatus.PreparingStrategy;
            run.CurrentStage = "Preparing strategy...";
            run.PercentComplete = 35m;
            await _runRepository.UpdateAsync(run, cancellationToken);

            var candleSource = isValidationTraining
                ? executionContext.CandleDataSource
                  ?? throw new ValidationTrainingDataSourceMissingException(
                      executionContext.ValidationExperimentId,
                      executionContext.CallerComponent)
                : executionContext.CandleDataSource ?? _standardCandleDataSource;

            StrategyLabDataset labDataset;
            try
            {
                labDataset = await candleSource.LoadAsync(run, warmup, cancellationToken);
            }
            catch (InvalidOperationException ex) when (!isValidationTraining
                && ex.Message.Contains("No candle data", StringComparison.OrdinalIgnoreCase))
            {
                PersistCoverageDiagnostics(run, coverageDiagnostics);
                await FailRunAsync(run, "No candle data available after import verification.", cancellationToken);
                return;
            }

            if (labDataset.Candles.Count == 0)
            {
                PersistCoverageDiagnostics(run, coverageDiagnostics);
                await FailRunAsync(run, "No candle data available after import verification.", cancellationToken);
                return;
            }

            if (isValidationTraining)
            {
                VerifyTrainingBoundary(labDataset, executionContext);
            }

            var dataset = labDataset;

            var legacyMeta = ExperimentFingerprintBuilder.BuildCandleDatasetFingerprint(
                run.ExchangeId,
                run.SymbolId,
                run.Timeframe,
                run.FromUtc,
                run.ToUtc,
                dataset.Candles.Count,
                dataset.Candles.First().OpenTimeUtc,
                dataset.Candles.Last().OpenTimeUtc);
            var contentFp = ExperimentFingerprintBuilder.BuildCandleContentFingerprint(dataset.Candles, legacyMeta);
            run.CandleDatasetFingerprint = contentFp.FullSha256;
            MergeResultSummary(run, "candleContentFingerprint", new
            {
                algorithmVersion = contentFp.AlgorithmVersion,
                fullSha256 = contentFp.FullSha256,
                shortDisplayHash = contentFp.ShortDisplayHash,
                legacyMetadataFingerprint = contentFp.LegacyMetadataFingerprint,
                candleCount = contentFp.CandleCount
            });

            var feeSettings = JsonSerializer.Deserialize<Dictionary<string, decimal>>(run.FeeSettingsJson)
                ?? new Dictionary<string, decimal>();
            var takerFee = feeSettings.GetValueOrDefault("takerFeeRate", 0.0004m);
            var slippageSettings = JsonSerializer.Deserialize<Dictionary<string, decimal>>(run.SlippageSettingsJson)
                ?? new Dictionary<string, decimal>();
            var slippage = slippageSettings.GetValueOrDefault("slippagePercent", 0m);

            var observationSettings = ResolveObservationSettings(run);
            IReadOnlyList<RiskRule> riskRules = [];
            RiskRuleSet riskRuleSet = RiskRuleSet.FromRules([]);
            RiskProfileSnapshotDto? riskSnapshot = null;
            var riskProfileId = observationSettings.RiskProfileId ?? run.RiskProfileId;
            string riskProfileName = "Custom";
            var riskProfileSource = RiskProfileSources.Custom;
            if (riskProfileId.HasValue)
            {
                riskRules = await _riskRuleRepository.GetByProfileIdAsync(riskProfileId.Value, cancellationToken);
                riskRuleSet = RiskRuleSet.FromRules(riskRules);
                var profile = await _riskProfileRepository.GetByIdAsync(riskProfileId.Value, cancellationToken);
                riskProfileName = profile?.Name ?? $"Profile {riskProfileId.Value}";
                riskProfileSource = RiskProfileSources.Saved;
            }

            var observeConfidence = run.ExecutionMode is StrategyLabExecutionMode.StrategyPlusConfidenceObservation
                or StrategyLabExecutionMode.FullPipelineComparison;
            var observeRisk = run.ExecutionMode is StrategyLabExecutionMode.StrategyPlusRiskObservation
                or StrategyLabExecutionMode.FullPipelineComparison;

            // Lab confidence threshold is independent of risk-profile MinConfidenceScore (policy).
            var minConfidence = observationSettings.UseSystemDefaultConfidenceThreshold
                ? DefaultMinConfidence
                : Math.Clamp(observationSettings.CustomConfidenceThreshold ?? DefaultMinConfidence, 0m, 100m);
            observationSettings.EffectiveConfidenceThreshold = minConfidence;

            ChronologicalShadowProcessor.Result? shadowResult = null;
            var preRunWarnings = new List<string>();

            if (observeRisk)
            {
                riskSnapshot = BuildFuturesRiskSnapshot(
                    observationSettings,
                    riskProfileId,
                    riskProfileName,
                    riskProfileSource,
                    riskRuleSet,
                    riskRules);
                run.RiskProfileSnapshotJson = RiskObservationJson.Serialize(riskSnapshot);

                if (riskSnapshot.PolicyMinimumConfidence is { } policyMin
                    && policyMin > minConfidence)
                {
                    preRunWarnings.Add(
                        $"Risk profile minimum confidence is {policyMin:0.##} while the laboratory confidence threshold is {minConfidence:0.##}. These are separate controls.");
                }

                if (riskSnapshot.ExposureSemanticsVersion == ExposureSemanticsVersion.LegacyAmbiguous)
                {
                    preRunWarnings.Add(
                        "Selected risk profile uses LegacyAmbiguous exposure semantics. Ambiguous MaxExposure fields are not enforced as notional or margin until explicitly migrated.");
                }
            }

            PersistObservationSettings(run, observationSettings);
            await _runRepository.UpdateAsync(run, cancellationToken);

            var detector = PriceStructureDetectorFactory.Create(run.StrategyCode);
            detector?.Initialize(parameters);

            var evaluationIndices = dataset.EvaluationIndices
                .Where(i => dataset.Candles[i].OpenTimeUtc >= run.FromUtc && dataset.Candles[i].OpenTimeUtc <= run.ToUtc)
                .ToList();
            var warmupCandlesLoaded = dataset.Candles.Count(c => c.OpenTimeUtc < run.FromUtc);
            var testRangeCandles = dataset.Candles.Count(c => c.OpenTimeUtc >= run.FromUtc && c.OpenTimeUtc <= run.ToUtc);

            if (detector is not null)
            {
                var diagnostics = detector.GetDiagnostics();
                diagnostics.CandlesLoaded = dataset.Candles.Count;
                diagnostics.WarmupCandlesLoaded = warmupCandlesLoaded;
                diagnostics.TestRangeCandles = testRangeCandles;
                diagnostics.EligibleEvaluationCandles = evaluationIndices.Count;
            }

            run.Status = StrategyLabRunStatus.Evaluating;
            run.CurrentStage = "Running strategy...";
            run.PercentComplete = 40m;
            await _runRepository.UpdateAsync(run, cancellationToken);

            var candidates = new List<StrategyResearchCandidate>();
            var evaluations = 0;
            var totalEvals = Math.Max(evaluationIndices.Count, 1);
            var detectedInMemory = 0;
            // Production factory returns CandlePrefixView so we can mutate visible count without reallocating.
            // Copied-list factory (tests) allocates a new window each step via CreateVisibleWindow.
            var candleWindow = _candleWindowFactory.CreateVisibleWindow(dataset.Candles, 0);
            var checkpointEvery = Math.Clamp(50, 10, 500);
            var checkpointCount = 0;
            var evalStartedUtc = DateTime.UtcNow;

            for (var idx = 0; idx < evaluationIndices.Count; idx++)
            {
                var candleIndex = evaluationIndices[idx];
                evaluations++;
                var candle = dataset.Candles[candleIndex];
                IReadOnlyList<Candle> slice;
                if (candleWindow is CandlePrefixView prefixView)
                {
                    prefixView.SetVisibleCount(candleIndex + 1);
                    slice = prefixView;
                }
                else
                {
                    slice = _candleWindowFactory.CreateVisibleWindow(dataset.Candles, candleIndex + 1);
                }

                PriceStructureCandidateDto? structureCandidate = null;
                string? structureJson = null;
                string fingerprint;
                TradeDirection direction;
                decimal? entry;
                decimal? stop;
                decimal? target;
                string reason;
                CandidateConfidenceResult? confidenceResult = null;

                if (detector is not null)
                {
                    var detectorResult = detector.ProcessCandle(
                        slice,
                        run.StrategyCode,
                        run.SymbolId,
                        canonicalTimeframe);
                    structureCandidate = detectorResult.Candidate;
                    reason = detectorResult.Reason;
                    if (structureCandidate is null)
                    {
                        if (idx % checkpointEvery == 0)
                        {
                            checkpointCount++;
                            run.PercentComplete = 40m + Math.Round((decimal)idx / totalEvals * 45m, 1);
                            run.EvaluationsCount = evaluations;
                            await _runRepository.UpdateAsync(run, cancellationToken);
                        }

                        continue;
                    }

                    detectedInMemory++;
                    fingerprint = structureCandidate.SetupFingerprint;
                    direction = structureCandidate.Direction;
                    entry = structureCandidate.EntryPrice;
                    stop = structureCandidate.StopLoss;
                    target = structureCandidate.Target1;
                    reason = structureCandidate.Reason;
                    structureJson = JsonSerializer.Serialize(new
                    {
                        setupFingerprint = fingerprint,
                        structure = structureCandidate.Structure,
                        version = strategyEntity.Version
                    }, JsonOptions);
                }
                else
                {
                    var context = new StrategyContext
                    {
                        ExchangeId = run.ExchangeId,
                        SymbolId = run.SymbolId,
                        Symbol = run.Symbol,
                        Timeframe = parsedTimeframe,
                        HigherTimeframe = parsedTimeframe,
                        MarketRegime = MarketRegime.Trending,
                        Candles = slice,
                        IndicatorSnapshot = dataset.IndicatorSnapshots.GetValueOrDefault(candle.Id),
                        StrategyParameters = parameters,
                        EvaluatedAtUtc = candle.CloseTimeUtc,
                        CurrentCandleIndex = candleIndex
                    };

                    var signal = plugin.Evaluate(context);
                    if (signal.SignalType != SignalType.Entry || signal.Direction == TradeDirection.None)
                    {
                        continue;
                    }

                    detectedInMemory++;
                    structureJson = signal.RawDataJson ?? "{}";
                    fingerprint = ExtractFingerprint(structureJson);
                    if (string.IsNullOrWhiteSpace(fingerprint))
                    {
                        fingerprint = $"legacy-{run.Id}-{candle.CloseTimeUtc:yyyyMMddHHmmss}-{signal.Direction}";
                    }

                    direction = signal.Direction;
                    entry = signal.EntryPrice;
                    stop = signal.SuggestedStopLoss;
                    target = signal.SuggestedTakeProfit;
                    reason = signal.Reason;
                }

                if (string.IsNullOrWhiteSpace(fingerprint))
                {
                    continue;
                }

                var candidate = BuildCandidate(
                    run,
                    strategyEntity,
                    direction,
                    entry ?? candle.Close,
                    stop ?? 0m,
                    target ?? 0m,
                    fingerprint,
                    structureJson ?? "{}",
                    parameters,
                    candle,
                    reason);

                var validationError = ValidateForSimulation(candidate);
                if (validationError is not null)
                {
                    candidate.CandidateStatus = StrategyResearchCandidateStatus.SimulationInvalid;
                    candidate.RawOutcomeStatus = RawOutcomeStatus.Invalid;
                    candidate.RawExitReason = validationError;
                    candidates.Add(candidate);
                    continue;
                }

                candidate.CandidateStatus = StrategyResearchCandidateStatus.StrategyQualified;
                candidate.StopDistancePercent = RiskApprovalScoreCalculator.ComputeStopDistancePercent(
                    candidate.ProposedEntryPrice,
                    candidate.StopLoss,
                    candidate.Direction);

                if (observeConfidence)
                {
                    confidenceResult = ScoreCandidateConfidence(
                        run.StrategyCode,
                        candidate,
                        structureCandidate,
                        slice);
                }

                ApplyConfidenceGate(candidate, confidenceResult, observeConfidence, minConfidence);

                run.Status = StrategyLabRunStatus.SimulatingOutcomes;
                RawOutcomeSimulator.Simulate(new RawOutcomeSimulationRequest
                {
                    Candidate = candidate,
                    Candles = dataset.Candles,
                    EntryCandleIndex = candleIndex,
                    TakerFeeRate = takerFee,
                    SlippagePercent = slippage,
                    Quantity = 1m
                });

                if (!observeRisk)
                {
                    candidate.RiskDecision = ResearchRiskDecision.NotEvaluated;
                    candidate.RiskScoreDecision = ResearchRiskScoreDecision.NotEvaluated;
                    candidate.HardRuleComplianceDecision = ResearchHardRuleComplianceDecision.NotEvaluated;
                    candidate.RiskPolicyEligibilityDecision = ResearchRiskPolicyEligibilityDecision.NotEvaluated;
                    candidate.FinalPipelineDecision = StrategyLabRiskObserver.ResolveFinalDecision(candidate);
                }

                candidates.Add(candidate);

                if (idx % 25 == 0)
                {
                    run.PercentComplete = 40m + Math.Round((decimal)idx / totalEvals * 50m, 1);
                    run.EvaluationsCount = evaluations;
                    run.RawCandidateCount = candidates.Count;
                    run.Status = StrategyLabRunStatus.Evaluating;
                    await _runRepository.UpdateAsync(run, cancellationToken);
                }
            }

            if (observeRisk && riskSnapshot is not null)
            {
                run.Status = StrategyLabRunStatus.Evaluating;
                run.CurrentStage = "Chronological shadow portfolio risk evaluation...";
                await _runRepository.UpdateAsync(run, cancellationToken);

                if (!observationSettings.UseSystemDefaultRiskSettings
                    && observationSettings.RiskPerTradePercent is { } rtp && rtp > 0)
                {
                    riskRuleSet = CloneRuleSet(riskRuleSet, maxRiskPerTrade: rtp);
                }

                var makerFee = feeSettings.GetValueOrDefault("makerFeeRate", 0.0002m);
                var slippageBps = observationSettings.SlippageBasisPoints ?? slippageSettings.GetValueOrDefault("slippageBasisPoints", 0m);
                if (slippageBps <= 0 && slippageSettings.TryGetValue("slippagePercent", out var slipPct) && slipPct > 0)
                {
                    slippageBps = slipPct * 100m; // percent → bps
                }

                var costSnapshot = StrategyLabCostSnapshot.CreateDefault(makerFee, takerFee, slippageBps);
                if (string.Equals(observationSettings.EntryOrderType, "Maker", StringComparison.OrdinalIgnoreCase))
                {
                    costSnapshot = new StrategyLabCostSnapshot
                    {
                        CostModelVersion = costSnapshot.CostModelVersion,
                        MakerFeeRate = costSnapshot.MakerFeeRate,
                        TakerFeeRate = costSnapshot.TakerFeeRate,
                        EntryOrderType = StrategyLabOrderFeeType.Maker,
                        ExitOrderType = string.Equals(observationSettings.ExitOrderType, "Maker", StringComparison.OrdinalIgnoreCase)
                            ? StrategyLabOrderFeeType.Maker
                            : StrategyLabOrderFeeType.Taker,
                        EntryFeeRateUsed = costSnapshot.MakerFeeRate,
                        ExitFeeRateUsed = string.Equals(observationSettings.ExitOrderType, "Maker", StringComparison.OrdinalIgnoreCase)
                            ? costSnapshot.MakerFeeRate
                            : costSnapshot.TakerFeeRate,
                        SlippageBasisPoints = slippageBps,
                        FundingCalculationMode = FundingCalculationMode.NotEvaluated,
                        EstimatedFundingCost = 0m
                    };
                }

                shadowResult = ChronologicalShadowProcessor.Process(
                    candidates,
                    riskSnapshot,
                    riskRuleSet,
                    _riskObserver,
                    run.InitialBalance,
                    costSnapshot);
            }

            if (candidates.Count > 0)
            {
                await _candidateRepository.AddRangeAsync(candidates, cancellationToken);
            }

            var funnelDiagnostics = detector?.GetDiagnostics() ?? new PriceStructureFunnelDiagnostics
            {
                CandlesEvaluated = evaluations,
                CandidatesDetectedInMemory = detectedInMemory,
                CandidatesPersisted = candidates.Count,
                RawCandidatesCreated = candidates.Count
            };

            funnelDiagnostics.CandidatesDetectedInMemory = Math.Max(funnelDiagnostics.CandidatesDetectedInMemory, detectedInMemory);
            funnelDiagnostics.CandidatesSimulationInvalid = candidates.Count(c => c.CandidateStatus == StrategyResearchCandidateStatus.SimulationInvalid);
            funnelDiagnostics.CandidatesPersisted = candidates.Count;
            funnelDiagnostics.SimulationInvalidCandidates = funnelDiagnostics.CandidatesSimulationInvalid;
            funnelDiagnostics.CandlesLoaded = dataset.Candles.Count;
            funnelDiagnostics.WarmupCandlesLoaded = warmupCandlesLoaded;
            funnelDiagnostics.TestRangeCandles = testRangeCandles;
            funnelDiagnostics.EligibleEvaluationCandles = evaluationIndices.Count;

            var syntheticPassed = true;
            PriceStructureZeroCandidateExplainer.Populate(funnelDiagnostics, syntheticPassed);

            if (funnelDiagnostics.CandidatesDetectedInMemory > 0
                && funnelDiagnostics.CandidatesPersisted == 0
                && funnelDiagnostics.CandidatesRejectedAsDuplicate + funnelDiagnostics.CandidatesSimulationInvalid
                    < funnelDiagnostics.CandidatesDetectedInMemory)
            {
                funnelDiagnostics.RuntimeWarnings.Add("CandidatePersistenceBug");
            }

            AssertPersistenceReconciles(funnelDiagnostics);

            var opportunity = StrategyOpportunityMetricsCalculator.Calculate(
                evaluations,
                candidates,
                evaluationIndices.Count,
                run.FromUtc,
                run.ToUtc);
            var closedCount = candidates.Count(c => c.CandidateStatus == StrategyResearchCandidateStatus.Closed);
            var evidence = EvidenceQualityCalculator.Calculate(closedCount);
            var summary = StrategyLabPerformanceCalculator.BuildSummary(candidates, opportunity, evidence, run.InitialBalance);
            var funnel = BuildFunnel(funnelDiagnostics, candidates);
            RawVsGatedComparisonDto? gated = run.ExecutionMode != StrategyLabExecutionMode.RawStrategy
                ? StrategyLabPerformanceCalculator.BuildGatedComparison(candidates)
                : null;

            var zeroExplanation = candidates.Count == 0
                ? new ZeroCandidateExplanationDto
                {
                    Classification = funnelDiagnostics.ZeroCandidateClassification,
                    PrimaryBlocker = funnelDiagnostics.PrimaryBlocker,
                    Details = funnelDiagnostics.PrimaryBlockerDetails,
                    SuggestedNextAction = funnelDiagnostics.SuggestedNextAction
                }
                : null;

            run.ResultSummaryJson = JsonSerializer.Serialize(new
            {
                summary,
                funnel,
                gatedComparison = gated,
                warnings = BuildWarnings(summary, candidates, evidence, funnelDiagnostics, riskSnapshot, minConfidence, preRunWarnings, shadowResult),
                coverageDiagnostics = MapCoverage(coverageDiagnostics),
                zeroCandidateExplanation = zeroExplanation,
                diagnosticEvents = funnelDiagnostics.SampleEvents.Select(MapDiagnosticEvent).ToList(),
                sampleFingerprints = funnelDiagnostics.SampleFingerprints,
                structureFunnel = funnelDiagnostics,
                riskOnlyShadowPortfolio = shadowResult?.RiskOnlySummary,
                fullPipelineShadowPortfolio = shadowResult?.FullPipelineSummary,
                portfolioPathDivergence = shadowResult?.Divergence,
                riskPathAssessmentVersion = IndependentPathsVersions.Current,
                costModelSnapshot = shadowResult?.CostSnapshot,
                costDiagnostics = shadowResult is null
                    ? null
                    : new
                    {
                        diagnostics = shadowResult.Diagnostics,
                        riskOnlyTotalFees = shadowResult.RiskOnlySummary.EntryFees + shadowResult.RiskOnlySummary.ExitFees,
                        fullPipelineTotalFees = shadowResult.FullPipelineSummary.EntryFees + shadowResult.FullPipelineSummary.ExitFees
                    },
                portfolioRiskScoreDiagnostics = shadowResult is null
                    ? null
                    : BuildPortfolioScoreDiagnostics(shadowResult.PortfolioRiskScores),
                drawdownCalculationMode = DrawdownCalculationMode.RealizedOnly.ToString(),
                pathDiagnostics = shadowResult?.Diagnostics
            }, JsonOptions);

            run.EvaluationsCount = evaluations;
            run.RawCandidateCount = candidates.Count;
            run.Status = StrategyLabRunStatus.Completed;
            run.PercentComplete = 100m;
            run.CurrentStage = "Completed";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await _runRepository.UpdateAsync(run, cancellationToken);
        }
        catch (ValidationTrainingBoundaryException ex)
        {
            _logger.LogError(ex, "Strategy lab run {RunId} failed closed ({ErrorCode}).", runId, ex.ErrorCode);
            var failedRun = await _runRepository.GetByIdAsync(runId, cancellationToken);
            if (failedRun is not null)
            {
                PersistCoverageDiagnostics(failedRun, coverageDiagnostics);
                await FailRunAsync(failedRun, ex.Message, cancellationToken);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Strategy lab run {RunId} failed.", runId);
            var failedRun = await _runRepository.GetByIdAsync(runId, cancellationToken);
            if (failedRun is not null)
            {
                PersistCoverageDiagnostics(failedRun, coverageDiagnostics);
                await FailRunAsync(failedRun, ex.Message, cancellationToken);
            }
        }
    }

    private static void ValidateExecutionContext(StrategyLabExecutionContext executionContext)
    {
        if (executionContext.ExecutionPurpose != ExecutionPurpose.ValidationTraining)
        {
            return;
        }

        if (executionContext.CandleDataSource is null)
        {
            throw new ValidationTrainingDataSourceMissingException(
                executionContext.ValidationExperimentId,
                executionContext.CallerComponent);
        }

        if (executionContext.ValidationExperimentId is null or <= 0)
        {
            throw new InvalidOperationException("ValidationTraining requires ValidationExperimentId.");
        }

        if (executionContext.ValidationTrialNumber is null or <= 0)
        {
            throw new InvalidOperationException("ValidationTraining requires ValidationTrialNumber.");
        }

        if (executionContext.TrainingBoundaryUtc is null)
        {
            throw new InvalidOperationException("ValidationTraining requires TrainingBoundaryUtc.");
        }

        if (string.IsNullOrWhiteSpace(executionContext.CorrelationId))
        {
            throw new InvalidOperationException("ValidationTraining requires CorrelationId.");
        }

        if (executionContext.AllowCoverageImport)
        {
            throw new ValidationTrainingCoverageImportForbiddenException(
                executionContext.ValidationExperimentId,
                executionContext.TrainingBoundaryUtc,
                executionContext.CallerComponent);
        }
    }

    private static void VerifyTrainingBoundary(
        StrategyLabDataset dataset,
        StrategyLabExecutionContext executionContext)
    {
        var boundary = DateTime.SpecifyKind(executionContext.TrainingBoundaryUtc!.Value, DateTimeKind.Utc);
        foreach (var candle in dataset.Candles)
        {
            var open = DateTime.SpecifyKind(candle.OpenTimeUtc, DateTimeKind.Utc);
            if (open >= boundary)
            {
                throw new ValidationTrainingBoundaryViolationException(
                    executionContext.ValidationExperimentId,
                    boundary,
                    executionContext.CallerComponent,
                    open,
                    open,
                    $"Training dataset candle at {open:O} is at or beyond ValidationStartUtc {boundary:O}.");
            }
        }

        foreach (var index in dataset.EvaluationIndices)
        {
            if (index < 0 || index >= dataset.Candles.Count)
            {
                throw new ValidationTrainingBoundaryViolationException(
                    executionContext.ValidationExperimentId,
                    boundary,
                    executionContext.CallerComponent,
                    null,
                    null,
                    $"Evaluation index {index} is outside the training dataset.");
            }

            var open = DateTime.SpecifyKind(dataset.Candles[index].OpenTimeUtc, DateTimeKind.Utc);
            if (open >= boundary)
            {
                throw new ValidationTrainingBoundaryViolationException(
                    executionContext.ValidationExperimentId,
                    boundary,
                    executionContext.CallerComponent,
                    open,
                    open,
                    $"Evaluation candle at {open:O} is at or beyond ValidationStartUtc {boundary:O}.");
            }
        }
    }

    private static void AssertPersistenceReconciles(PriceStructureFunnelDiagnostics funnel)
    {
        var accounted =
            funnel.CandidatesRejectedAsDuplicate
            + funnel.CandidatesSimulationInvalid
            + funnel.CandidatesPersisted;
        if (funnel.CandidatesDetectedInMemory > 0
            && accounted < funnel.CandidatesDetectedInMemory
            && !funnel.RuntimeWarnings.Contains("CandidatePersistenceBug"))
        {
            funnel.RuntimeWarnings.Add(
                $"Candidate count mismatch: detected={funnel.CandidatesDetectedInMemory}, duplicate={funnel.CandidatesRejectedAsDuplicate}, invalid={funnel.CandidatesSimulationInvalid}, persisted={funnel.CandidatesPersisted}.");
        }
    }

    private static void PersistCoverageDiagnostics(StrategyLabRun run, HistoricalCandleCoverageResult? coverage)
    {
        if (coverage is null)
        {
            return;
        }

        MergeResultSummary(run, "coverageDiagnostics", MapCoverage(coverage));
    }

    private static void MergeResultSummary(StrategyLabRun run, string key, object? value)
    {
        try
        {
            var existing = string.IsNullOrWhiteSpace(run.ResultSummaryJson) || run.ResultSummaryJson == "{}"
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(run.ResultSummaryJson, JsonOptions)
                  ?? new Dictionary<string, object?>();
            existing[key] = value;
            run.ResultSummaryJson = JsonSerializer.Serialize(existing, JsonOptions);
        }
        catch
        {
            // ignore diagnostics merge failures
        }
    }

    private static CoverageDiagnosticsDto? MapCoverage(HistoricalCandleCoverageResult? coverage)
    {
        if (coverage is null)
        {
            return null;
        }

        return new CoverageDiagnosticsDto
        {
            CoverageCheckStartedAtUtc = coverage.CoverageCheckStartedAtUtc,
            RequestedFromUtc = coverage.RequestedFromUtc,
            RequestedToUtc = coverage.RequestedToUtc,
            RequestedTimeframe = coverage.RequestedTimeframe,
            ExistingCandleCount = coverage.ExistingCandleCount,
            MissingCandleCountEstimate = coverage.Coverage.MissingCandleCountEstimate,
            AutoImportAttempted = coverage.AutoImportAttempted,
            ImportStartedAtUtc = coverage.ImportStartedAtUtc,
            ImportCompletedAtUtc = coverage.ImportCompletedAtUtc,
            ImportedCandleCount = coverage.ImportedCandleCount,
            ImportError = coverage.ImportError,
            FinalCoverageStatus = coverage.FinalCoverageStatus,
            MissingRanges = coverage.MissingRanges.Select(r => new CoverageMissingRangeDto
            {
                FromUtc = r.FromUtc,
                ToUtc = r.ToUtc,
                EstimatedMissingCandles = r.EstimatedMissingCandles
            }).ToList()
        };
    }

    private static DiagnosticEventDto MapDiagnosticEvent(PriceStructureDiagnosticEvent evt) => new()
    {
        Stage = evt.Stage,
        Direction = evt.Direction,
        Level = evt.Level,
        LevelTimestampUtc = evt.LevelTimestampUtc,
        EventTimestampUtc = evt.EventTimestampUtc,
        SecondaryTimestampUtc = evt.SecondaryTimestampUtc,
        EventPrice = evt.EventPrice,
        Outcome = evt.Outcome,
        Reason = evt.Reason
    };

    private static StrategyResearchCandidate BuildCandidate(
        StrategyLabRun run,
        Strategy strategyEntity,
        TradeDirection direction,
        decimal entry,
        decimal stop,
        decimal target,
        string fingerprint,
        string structureJson,
        Dictionary<string, string> parameters,
        Domain.MarketData.Candle candle,
        string reason)
    {
        var risk = direction == TradeDirection.Long ? entry - stop : stop - entry;
        var rr = risk > 0 ? Math.Abs((target - entry) / risk) : 0m;

        return new StrategyResearchCandidate
        {
            StrategyLabRunId = run.Id,
            StrategyCode = run.StrategyCode,
            StrategyVersion = strategyEntity.Version,
            ExchangeId = run.ExchangeId,
            SymbolId = run.SymbolId,
            Symbol = run.Symbol,
            Timeframe = run.Timeframe,
            Direction = direction,
            SetupDetectedAtUtc = candle.CloseTimeUtc,
            ProposedEntryTimeUtc = candle.CloseTimeUtc,
            ProposedEntryPrice = entry,
            StopLoss = stop,
            Target1 = target,
            RewardRisk = rr,
            CandidateStatus = StrategyResearchCandidateStatus.Detected,
            StrategyReason = reason,
            SetupFingerprint = fingerprint,
            ParametersJson = JsonSerializer.Serialize(parameters),
            StructureJson = structureJson,
            RawOutcomeStatus = RawOutcomeStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private void ApplyConfidenceGate(
        StrategyResearchCandidate candidate,
        CandidateConfidenceResult? confidenceResult,
        bool observeConfidence,
        decimal minConfidence)
    {
        if (observeConfidence && confidenceResult is not null)
        {
            var confidenceScore = Math.Clamp(confidenceResult.Score, 0m, 100m);
            var approved = confidenceScore >= minConfidence;
            candidate.ConfidenceScore = confidenceScore;
            candidate.ConfidenceThreshold = minConfidence;
            candidate.ConfidenceMargin = confidenceScore - minConfidence;
            candidate.ConfidenceDecision = approved
                ? ResearchConfidenceDecision.Approved
                : ResearchConfidenceDecision.Rejected;
            candidate.ConfidenceReason = approved
                ? $"Confidence {ConfidenceScoreNormalizer.Format(confidenceScore)} >= {ConfidenceScoreNormalizer.Format(minConfidence)}"
                : $"Confidence {ConfidenceScoreNormalizer.Format(confidenceScore)} < {ConfidenceScoreNormalizer.Format(minConfidence)}";
            candidate.ConfidenceModelVersion = confidenceResult.ModelVersion;
            candidate.ConfidenceComponentsJson = StrategySetupQualityScorer.SerializeComponents(confidenceResult.Components);
            candidate.ConfidenceEvaluatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            candidate.ConfidenceScore = null;
            candidate.ConfidenceThreshold = null;
            candidate.ConfidenceMargin = null;
            candidate.ConfidenceDecision = ResearchConfidenceDecision.NotEvaluated;
            candidate.ConfidenceReason = null;
            candidate.ConfidenceModelVersion = null;
            candidate.ConfidenceComponentsJson = null;
            candidate.ConfidenceEvaluatedAtUtc = null;
        }
    }

    private static RiskProfileSnapshotDto BuildFuturesRiskSnapshot(
        StrategyLabObservationSettingsDto settings,
        long? riskProfileId,
        string riskProfileName,
        string riskProfileSource,
        RiskRuleSet riskRuleSet,
        IReadOnlyList<RiskRule> riskRules)
    {
        var riskPerTrade = settings.RiskPerTradePercent is { } rp && rp > 0 && !settings.UseSystemDefaultRiskSettings
            ? rp
            : (riskRuleSet.MaxRiskPerTradePercent > 0 ? riskRuleSet.MaxRiskPerTradePercent : 0.5m);
        var maxLeverage = settings.MaximumLeverage is { } ml && ml > 0 ? ml : 10m;
        var preferred = settings.PreferredLeverage is { } pl && pl > 0
            ? pl
            : (!settings.UseSystemDefaultRiskSettings ? maxLeverage : (decimal?)null);

        var policyMin = riskRules.Any(r =>
            r.IsEnabled && string.Equals(r.RuleKey, RiskRuleKeys.MinConfidenceScore, StringComparison.OrdinalIgnoreCase))
            ? riskRuleSet.MinConfidenceScore
            : (decimal?)null;

        ExposureSemanticsVersion semantics;
        decimal? maxNotionalPerSymbol;
        decimal? maxTotalNotional;
        decimal? maxMarginPerSymbol;
        decimal? maxTotalMargin;
        decimal? maxConcurrentRisk;

        if (!settings.UseSystemDefaultRiskSettings
            || settings.ExposureSemanticsVersion == ExposureSemanticsVersion.ExplicitFuturesExposureV2
            || riskProfileSource == RiskProfileSources.Custom)
        {
            semantics = ExposureSemanticsVersion.ExplicitFuturesExposureV2;
            // Custom Lab config: use explicit futures fields. Null = disabled.
            // Prefer margin limits when provided; do not silently map legacy MaxExposure to notional.
            maxNotionalPerSymbol = settings.MaxNotionalExposurePerSymbolPercent;
            maxTotalNotional = settings.MaxTotalNotionalExposurePercent;
            maxMarginPerSymbol = settings.MaxMarginUsagePerSymbolPercent;
            maxTotalMargin = settings.MaxTotalMarginUsagePercent;
            maxConcurrentRisk = settings.MaxConcurrentRiskPercent;

            // If caller still only sent legacy ambiguous fields without explicit futures fields,
            // keep semantics Explicit but leave futures limits null (disabled) rather than reinterpret.
            if (maxNotionalPerSymbol is null
                && maxTotalNotional is null
                && maxMarginPerSymbol is null
                && maxTotalMargin is null
                && maxConcurrentRisk is null
                && (settings.MaximumPositionExposurePercent.HasValue || settings.MaximumConcurrentExposurePercent.HasValue))
            {
                semantics = ExposureSemanticsVersion.LegacyAmbiguous;
            }
        }
        else
        {
            // Saved system profile without explicit futures migration.
            semantics = settings.ExposureSemanticsVersion == ExposureSemanticsVersion.LegacyAmbiguous
                        || settings.ExposureSemanticsVersion == 0
                ? ExposureSemanticsVersion.LegacyAmbiguous
                : settings.ExposureSemanticsVersion;

            maxNotionalPerSymbol = null;
            maxTotalNotional = null;
            maxMarginPerSymbol = null;
            maxTotalMargin = null;
            maxConcurrentRisk = null;

            switch (settings.LegacyExposureResolution?.Trim().ToLowerInvariant())
            {
                case "notional":
                case "notionalexposurev1":
                    semantics = ExposureSemanticsVersion.NotionalExposureV1;
                    maxNotionalPerSymbol = riskRuleSet.MaxExposurePerSymbolPercent;
                    maxTotalNotional = riskRuleSet.MaxTotalExposurePercent;
                    break;
                case "margin":
                case "marginusagev1":
                    semantics = ExposureSemanticsVersion.MarginUsageV1;
                    maxMarginPerSymbol = riskRuleSet.MaxExposurePerSymbolPercent;
                    maxTotalMargin = riskRuleSet.MaxTotalExposurePercent;
                    break;
                case "manual":
                case "explicitfuturesexposurev2":
                    semantics = ExposureSemanticsVersion.ExplicitFuturesExposureV2;
                    maxNotionalPerSymbol = settings.MaxNotionalExposurePerSymbolPercent;
                    maxTotalNotional = settings.MaxTotalNotionalExposurePercent;
                    maxMarginPerSymbol = settings.MaxMarginUsagePerSymbolPercent;
                    maxTotalMargin = settings.MaxTotalMarginUsagePercent;
                    maxConcurrentRisk = settings.MaxConcurrentRiskPercent;
                    break;
            }
        }

        var maxOpen = settings.MaxOpenPositions ?? (riskRuleSet.MaxOpenPositions > 0 ? riskRuleSet.MaxOpenPositions : 5);
        var maxDaily = settings.MaxDailyLossPercent ?? riskRuleSet.MaxDailyLossPercent;
        var maxDd = settings.MaxDrawdownPercent ?? riskRuleSet.MaxWeeklyLossPercent;
        var minRr = settings.MinimumRewardRisk ?? (riskRuleSet.MinRewardRiskRatio > 0 ? riskRuleSet.MinRewardRiskRatio : 1m);

        var snapshotId = riskProfileSource == RiskProfileSources.Custom
            ? $"custom-{Guid.NewGuid():N}"
            : $"saved-{riskProfileId ?? 0}-{Guid.NewGuid():N}";

        var active = riskRules
            .Where(r => r.IsEnabled)
            .ToDictionary(r => r.RuleKey, r => r.RuleValue, StringComparer.OrdinalIgnoreCase);

        return new RiskProfileSnapshotDto
        {
            RiskProfileId = riskProfileSource == RiskProfileSources.Custom ? null : riskProfileId,
            RiskProfileName = riskProfileSource == RiskProfileSources.Custom ? "Custom Lab Configuration" : riskProfileName,
            RiskProfileSource = riskProfileSource,
            RiskProfileSnapshotId = snapshotId,
            RiskProfileVersion = RiskObservationVersions.Current,
            ExposureSemanticsVersion = semantics,
            DrawdownCalculationMode = DrawdownCalculationMode.RealizedOnly,
            RiskPerTradePercent = riskPerTrade,
            PreferredLeverage = preferred,
            MaxLeverage = maxLeverage,
            MaxNotionalExposurePerSymbolPercent = maxNotionalPerSymbol,
            MaxTotalNotionalExposurePercent = maxTotalNotional,
            MaxMarginUsagePerSymbolPercent = maxMarginPerSymbol,
            MaxTotalMarginUsagePercent = maxTotalMargin,
            MaxConcurrentRiskPercent = maxConcurrentRisk,
            LegacyMaxExposurePerSymbolPercent = riskRuleSet.MaxExposurePerSymbolPercent,
            LegacyMaxTotalExposurePercent = riskRuleSet.MaxTotalExposurePercent,
            MaxDailyLossPercent = maxDaily,
            MaxDrawdownPercent = maxDd,
            MaxConcurrentPositions = maxOpen,
            MinimumRewardRisk = minRr,
            FeeEfficiencyHardLimitPercent = settings.FeeEfficiencyHardLimitPercent ?? 80m,
            PolicyMinimumConfidence = policyMin,
            ObservationalRiskScoreThreshold = settings.RiskApprovalThreshold ?? 50m,
            ActiveRules = active
        };
    }

    private static RiskRuleSet CloneRuleSet(
        RiskRuleSet source,
        decimal? maxRiskPerTrade = null,
        decimal? maxPos = null,
        decimal? maxTotal = null) =>
        new()
        {
            MaxRiskPerTradePercent = maxRiskPerTrade ?? source.MaxRiskPerTradePercent,
            MaxDailyLossPercent = source.MaxDailyLossPercent,
            MaxWeeklyLossPercent = source.MaxWeeklyLossPercent,
            MaxOpenPositions = source.MaxOpenPositions,
            MaxExposurePerSymbolPercent = maxPos ?? source.MaxExposurePerSymbolPercent,
            MaxTotalExposurePercent = maxTotal ?? source.MaxTotalExposurePercent,
            MaxCorrelatedExposurePercent = source.MaxCorrelatedExposurePercent,
            MaxConsecutiveLosses = source.MaxConsecutiveLosses,
            MinConfidenceScore = source.MinConfidenceScore,
            MaxSpreadPercent = source.MaxSpreadPercent,
            MaxAtrPercent = source.MaxAtrPercent,
            EmergencyStopEnabled = source.EmergencyStopEnabled,
            RequireStopLoss = source.RequireStopLoss,
            MinRewardRiskRatio = source.MinRewardRiskRatio
        };

    private CandidateConfidenceResult ScoreCandidateConfidence(
        string strategyCode,
        StrategyResearchCandidate candidate,
        PriceStructureCandidateDto? structureCandidate,
        IReadOnlyList<Candle> candlesThroughSetup)
    {
        var structure = structureCandidate?.Structure
            ?? TryParseStructure(candidate.StructureJson)
            ?? new PriceStructureSetupDto { SetupType = "Unknown" };

        return _confidenceScorer.Score(new CandidateConfidenceContext
        {
            StrategyCode = strategyCode,
            Direction = candidate.Direction,
            EntryPrice = candidate.ProposedEntryPrice,
            StopLoss = candidate.StopLoss,
            Target1 = candidate.Target1,
            RewardRisk = candidate.RewardRisk,
            Structure = structure,
            CandlesThroughSetup = candlesThroughSetup
        });
    }

    private static PriceStructureSetupDto? TryParseStructure(string structureJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(structureJson);
            if (doc.RootElement.TryGetProperty("structure", out var structureEl)
                || doc.RootElement.TryGetProperty("Structure", out structureEl))
            {
                return JsonSerializer.Deserialize<PriceStructureSetupDto>(structureEl.GetRawText(), JsonOptions);
            }

            return JsonSerializer.Deserialize<PriceStructureSetupDto>(structureJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static StrategyLabObservationSettingsDto ResolveObservationSettings(StrategyLabRun run)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(run.StrategyFeatureFlagsJson) && run.StrategyFeatureFlagsJson != "{}")
            {
                using var doc = JsonDocument.Parse(run.StrategyFeatureFlagsJson);
                if (doc.RootElement.TryGetProperty("observationSettings", out var settingsEl)
                    || doc.RootElement.TryGetProperty("ObservationSettings", out settingsEl))
                {
                    var parsed = JsonSerializer.Deserialize<StrategyLabObservationSettingsDto>(settingsEl.GetRawText(), JsonOptions);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }

                var direct = JsonSerializer.Deserialize<StrategyLabObservationSettingsDto>(run.StrategyFeatureFlagsJson, JsonOptions);
                if (direct is not null && (direct.CustomConfidenceThreshold.HasValue || direct.RiskProfileId.HasValue || !direct.UseSystemDefaultConfidenceThreshold))
                {
                    return direct;
                }
            }
        }
        catch
        {
            // fall through to defaults
        }

        return new StrategyLabObservationSettingsDto
        {
            RiskProfileId = run.RiskProfileId,
            UseSystemDefaultConfidenceThreshold = true,
            UseSystemDefaultRiskSettings = true,
            ConfidenceModel = StrategySetupQualityScorer.ModelVersion
        };
    }

    private static void PersistObservationSettings(StrategyLabRun run, StrategyLabObservationSettingsDto settings)
    {
        run.StrategyFeatureFlagsJson = JsonSerializer.Serialize(new
        {
            observationSettings = settings
        }, JsonOptions);
        run.RiskProfileId = settings.RiskProfileId ?? run.RiskProfileId;
        run.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string? ValidateForSimulation(StrategyResearchCandidate candidate)
    {
        if (candidate.ProposedEntryPrice <= 0) return "Missing entry price.";
        if (candidate.StopLoss <= 0) return "Missing stop loss.";
        if (candidate.Target1 <= 0) return "Missing target.";

        if (candidate.Direction == TradeDirection.Long)
        {
            if (candidate.StopLoss >= candidate.ProposedEntryPrice) return "Stop on wrong side of entry.";
            if (candidate.Target1 <= candidate.ProposedEntryPrice) return "Target on wrong side of entry.";
        }
        else if (candidate.Direction == TradeDirection.Short)
        {
            if (candidate.StopLoss <= candidate.ProposedEntryPrice) return "Stop on wrong side of entry.";
            if (candidate.Target1 >= candidate.ProposedEntryPrice) return "Target on wrong side of entry.";
        }
        else
        {
            return "Unsupported direction.";
        }

        var risk = candidate.Direction == TradeDirection.Long
            ? candidate.ProposedEntryPrice - candidate.StopLoss
            : candidate.StopLoss - candidate.ProposedEntryPrice;
        return risk <= 0 ? "Zero/negative stop distance." : null;
    }

    public static string ExtractFingerprint(string rawDataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawDataJson);
            if (TryGetPropertyIgnoreCase(doc.RootElement, "setupFingerprint", out var fp))
            {
                return fp.GetString() ?? string.Empty;
            }

            if (TryGetPropertyIgnoreCase(doc.RootElement, "SetupFingerprint", out fp))
            {
                return fp.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static CandidateFunnelDto BuildFunnel(
        PriceStructureFunnelDiagnostics diagnostics,
        IReadOnlyList<StrategyResearchCandidate> candidates) => new()
    {
        CandlesLoaded = diagnostics.CandlesLoaded,
        WarmupCandlesLoaded = diagnostics.WarmupCandlesLoaded,
        TestRangeCandles = diagnostics.TestRangeCandles,
        EligibleEvaluationCandles = diagnostics.EligibleEvaluationCandles,
        CandlesEvaluated = diagnostics.CandlesEvaluated,
        ConfirmedSwingHighs = diagnostics.ConfirmedSwingHighs,
        ConfirmedSwingLows = diagnostics.ConfirmedSwingLows,
        BullishBreakoutChecks = diagnostics.BullishBreakoutChecks,
        BearishBreakoutChecks = diagnostics.BearishBreakoutChecks,
        BullishBreakoutsDetected = diagnostics.BullishBreakoutsDetected,
        BearishBreakoutsDetected = diagnostics.BearishBreakoutsDetected,
        RetestChecks = diagnostics.RetestChecks,
        ValidRetests = diagnostics.ValidRetests,
        ConfirmationChecks = diagnostics.ConfirmationChecks,
        ConfirmationsPassed = diagnostics.ConfirmationsPassed,
        ActiveBuySideLiquidityLevels = diagnostics.ActiveBuySideLiquidityLevels,
        ActiveSellSideLiquidityLevels = diagnostics.ActiveSellSideLiquidityLevels,
        BuySideSweepChecks = diagnostics.BuySideSweepChecks,
        SellSideSweepChecks = diagnostics.SellSideSweepChecks,
        BuySideSweepsDetected = diagnostics.BuySideSweepsDetected,
        SellSideSweepsDetected = diagnostics.SellSideSweepsDetected,
        SameCandleReclaims = diagnostics.SameCandleReclaims,
        DelayedReclaims = diagnostics.DelayedReclaims,
        CandidatesDetectedInMemory = diagnostics.CandidatesDetectedInMemory,
        CandidatesRejectedAsDuplicate = diagnostics.CandidatesRejectedAsDuplicate,
        CandidatesSimulationInvalid = diagnostics.CandidatesSimulationInvalid,
        CandidatesPersisted = diagnostics.CandidatesPersisted,
        RawCandidates = candidates.Count,
        SimulationValidCandidates = candidates.Count(c => c.CandidateStatus != StrategyResearchCandidateStatus.SimulationInvalid),
        RawSimulatedTrades = candidates.Count(c => c.CandidateStatus is StrategyResearchCandidateStatus.Simulated or StrategyResearchCandidateStatus.Closed),
        ClosedRawTrades = candidates.Count(c => c.CandidateStatus == StrategyResearchCandidateStatus.Closed),
        ConfidenceApproved = candidates.Count(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved),
        ConfidenceRejected = candidates.Count(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected),
        RiskApproved = candidates.Count(c => c.RiskDecision == ResearchRiskDecision.Approved),
        RiskRejected = candidates.Count(c => c.RiskDecision == ResearchRiskDecision.Rejected),
        FullPipelineApproved = candidates.Count(c => c.FinalPipelineDecision == ResearchFinalPipelineDecision.Approved),
        PrimaryBlocker = diagnostics.PrimaryBlocker,
        PrimaryBlockerDetails = diagnostics.PrimaryBlockerDetails,
        SuggestedNextAction = diagnostics.SuggestedNextAction,
        ZeroCandidateClassification = diagnostics.ZeroCandidateClassification,
        StrategyFamily = diagnostics.StrategyFamily
    };

    private static IReadOnlyList<string> BuildWarnings(
        StrategyLabPerformanceSummaryDto summary,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        StrategyEvidenceQuality evidence,
        PriceStructureFunnelDiagnostics funnel,
        RiskProfileSnapshotDto? riskSnapshot,
        decimal labConfidenceThreshold,
        IReadOnlyList<string> preRunWarnings,
        ChronologicalShadowProcessor.Result? shadowResult)
    {
        var warnings = new List<string>();
        warnings.AddRange(preRunWarnings);
        warnings.AddRange(funnel.RuntimeWarnings);

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(funnel.PrimaryBlocker))
        {
            warnings.Add($"Why no candidates? Primary blocker: {funnel.PrimaryBlocker}");
            if (!string.IsNullOrWhiteSpace(funnel.PrimaryBlockerDetails))
            {
                warnings.Add(funnel.PrimaryBlockerDetails);
            }

            if (!string.IsNullOrWhiteSpace(funnel.SuggestedNextAction))
            {
                warnings.Add($"Suggested next action: {funnel.SuggestedNextAction}");
            }
        }
        else if (candidates.Count == 0)
        {
            warnings.Add("Strategy has produced no raw candidates in this research run.");
        }

        if (summary.Opportunity.CandidatesPer1000Candles < 0.5m && summary.Opportunity.Evaluations > 100 && candidates.Count > 0)
        {
            warnings.Add("Candidate rate is unusually low.");
        }

        if (summary.EvidenceQuality <= StrategyEvidenceQuality.Low)
        {
            warnings.Add($"Evidence Quality: {summary.EvidenceQualityLabel} — only {summary.RawClosedTrades} closed raw trades.");
        }

        if (riskSnapshot?.PolicyMinimumConfidence is { } policyMin && policyMin > 0)
        {
            var eligible = candidates.Count(c =>
                c.RiskPolicyEligibilityDecision == ResearchRiskPolicyEligibilityDecision.Eligible);
            var maxObserved = candidates
                .Where(c => c.ConfidenceScore.HasValue)
                .Select(c => c.ConfidenceScore!.Value)
                .DefaultIfEmpty()
                .Max();

            if (eligible == 0 && candidates.Any(c => c.ConfidenceScore.HasValue))
            {
                warnings.Add(
                    $"Risk policy rejected 100% of candidates. No candidate reached the configured minimum confidence of {policyMin:0.##}.");
            }

            if (maxObserved > 0 && maxObserved < policyMin)
            {
                warnings.Add(
                    $"Policy threshold is above the maximum observed confidence score in this run ({maxObserved:0.##} < {policyMin:0.##}). Lab confidence threshold was {labConfidenceThreshold:0.##}.");
            }
        }

        if (shadowResult is not null)
        {
            if (shadowResult.RiskOnlySummary.TradesAccepted < 5 && candidates.Count >= 50)
            {
                warnings.Add(
                    $"Risk-only shadow portfolio accepted only {shadowResult.RiskOnlySummary.TradesAccepted} trades — financial risk may be overly restrictive or semantics may need review.");
            }

            if (shadowResult.FullPipelineSummary.TradesAccepted < 5
                && candidates.Count(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved) >= 20)
            {
                warnings.Add(
                    $"Full-pipeline shadow portfolio accepted only {shadowResult.FullPipelineSummary.TradesAccepted} trades.");
            }
        }

        return warnings;
    }

    private static ScoreDistributionDiagnosticsDto BuildPortfolioScoreDiagnostics(IReadOnlyList<decimal> scores)
    {
        if (scores.Count == 0)
        {
            return new ScoreDistributionDiagnosticsDto
            {
                UniqueScoreCount = 0,
                DegenerateWarningCode = null
            };
        }

        var unique = scores.Distinct().Count();
        var avg = scores.Average();
        var variance = scores.Sum(s => (s - avg) * (s - avg)) / scores.Count;
        var std = (decimal)Math.Sqrt((double)variance);
        var mostCommon = scores.GroupBy(s => s).OrderByDescending(g => g.Count()).First();
        var degenerate = unique <= 1 && scores.Count >= 10;

        return new ScoreDistributionDiagnosticsDto
        {
            UniqueScoreCount = unique,
            MinScore = scores.Min(),
            MaxScore = scores.Max(),
            AverageScore = Math.Round(avg, 4),
            StandardDeviation = Math.Round(std, 4),
            MostCommonScore = mostCommon.Key,
            MostCommonScorePercent = Math.Round((decimal)mostCommon.Count() / scores.Count * 100m, 2),
            DegenerateWarningCode = degenerate ? "PortfolioRiskModelDegenerate" : null,
            DegenerateWarningMessage = degenerate
                ? "All evaluated portfolio risk scores are identical despite changing portfolio states."
                : null
        };
    }

    private async Task FailRunAsync(StrategyLabRun run, string message, CancellationToken cancellationToken)
    {
        run.Status = StrategyLabRunStatus.Failed;
        run.ErrorMessage = message;
        run.CurrentStage = string.IsNullOrWhiteSpace(run.CurrentStage)
            ? "Failed"
            : $"{run.CurrentStage} — Failed";
        run.CompletedAtUtc = DateTime.UtcNow;
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);
    }

    private async Task<int> ResolveWarmupAsync(long strategyId, CancellationToken cancellationToken)
    {
        try
        {
            var requirements = await _requirementService.GetByStrategyIdAsync(strategyId, cancellationToken);
            return requirements.Succeeded ? requirements.Data?.WarmupCandles ?? DefaultWarmup : DefaultWarmup;
        }
        catch
        {
            return DefaultWarmup;
        }
    }
}
