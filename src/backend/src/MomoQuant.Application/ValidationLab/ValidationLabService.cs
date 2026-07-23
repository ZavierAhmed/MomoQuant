using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationLabService
{
    Task<ServiceResult<ValidationExperimentDto>> CreateExperimentAsync(
        CreateValidationExperimentRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> UpdateExperimentAsync(
        long id,
        UpdateValidationExperimentRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> GetExperimentAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDetailDto>> GetExperimentDetailAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ValidationExperimentDto>>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> PrepareDataAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> RunTrainingAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> ResumeTrainingAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationTrialRecoveryReport>> RecoverTrialsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationTrainingProgressDto>> GetTrainingProgressAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ValidationParameterTrialDto>>> GetTrainingTrialsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> FreezeAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDetailDto>> RunValidationAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetComparisonAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetConfidenceAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetRiskAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResultDto<object>>> GetCandidatesAsync(
        long id,
        ValidationCandidateQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetDiagnosticsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> CloneAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDto>> RerunExactlyAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetReconciliationAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetLeakageAuditAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<object>> GetExclusivityAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationLaboratoryReadinessReport>> GetReadinessAsync(
        CancellationToken cancellationToken = default);

    Task RecordExportVerificationAsync(
        long experimentId,
        ExportVerificationResult result,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationExperimentDetailDto>> RecalculateVerdictAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationSelectionIntegrityReportDto>> GetSelectionIntegrityAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationMetricBasisAuditReportDto>> GetMetricBasisAuditAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RecalculateValidationMetricsResultDto>> RecalculateMetricsAsync(
        long id,
        RecalculateValidationMetricsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed partial class ValidationLabService : IValidationLabService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IValidationExperimentRepository _experiments;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationSegmentResultRepository _segments;
    private readonly IStrategyLabRunRepository _labRuns;
    private readonly IStrategyResearchCandidateRepository _candidates;
    private readonly IStrategyLabRunner _labRunner;
    private readonly ICandleRepository _candles;
    private readonly ISymbolRepository _symbols;
    private readonly IExchangeRepository _exchanges;
    private readonly IStrategyRepository _strategies;
    private readonly IStrategyParameterDefinitionProvider _parameterDefinitions;
    private readonly IHistoricalCandleCoverageService _coverage;
    private readonly IValidationCandidateReconciliationService _reconciliation;
    private readonly IValidationMetricConsistencyService _metricConsistency;
    private readonly IValidationLeakageAuditor _leakageAuditor;
    private readonly IValidationVerdictService _verdictService;
    private readonly IValidationLaboratoryReadinessService _readiness;
    private readonly IValidationTrainingPreflightService _trainingPreflight;
    private readonly IValidationTrainingExecutionLeaseService _trainingLease;
    private readonly IValidationTrialRecoveryService _trialRecovery;
    private readonly IValidationTrainingSelectionService _trainingSelection;
    private readonly IValidationSelectionIntegrityService _selectionIntegrity;
    private readonly IValidationParameterFingerprintService _parameterFingerprint;
    private readonly IValidationRiskBasisService _riskBasis;
    private readonly IValidationCandleAccessAuditRepository _candleAccessAudits;
    private readonly IValidationCandleAccessRecorder _candleAccessRecorder;
    private readonly IValidationTrainingScopeExecution _trainingScopeExecution;
    private readonly IValidationSegmentResultWriter _segmentResultWriter;

    public ValidationLabService(
        IValidationExperimentRepository experiments,
        IValidationParameterTrialRepository trials,
        IValidationSegmentResultRepository segments,
        IStrategyLabRunRepository labRuns,
        IStrategyResearchCandidateRepository candidates,
        IStrategyLabRunner labRunner,
        ICandleRepository candles,
        ISymbolRepository symbols,
        IExchangeRepository exchanges,
        IStrategyRepository strategies,
        IStrategyParameterDefinitionProvider parameterDefinitions,
        IHistoricalCandleCoverageService coverage,
        IValidationCandidateReconciliationService reconciliation,
        IValidationMetricConsistencyService metricConsistency,
        IValidationLeakageAuditor leakageAuditor,
        IValidationVerdictService verdictService,
        IValidationLaboratoryReadinessService readiness,
        IValidationTrainingPreflightService trainingPreflight,
        IValidationTrainingExecutionLeaseService trainingLease,
        IValidationTrialRecoveryService trialRecovery,
        IValidationTrainingSelectionService trainingSelection,
        IValidationSelectionIntegrityService selectionIntegrity,
        IValidationParameterFingerprintService parameterFingerprint,
        IValidationRiskBasisService riskBasis,
        IValidationCandleAccessAuditRepository candleAccessAudits,
        IValidationCandleAccessRecorder candleAccessRecorder,
        IValidationTrainingScopeExecution trainingScopeExecution,
        IValidationSegmentResultWriter segmentResultWriter)
    {
        _experiments = experiments;
        _trials = trials;
        _segments = segments;
        _labRuns = labRuns;
        _candidates = candidates;
        _labRunner = labRunner;
        _candles = candles;
        _symbols = symbols;
        _exchanges = exchanges;
        _strategies = strategies;
        _parameterDefinitions = parameterDefinitions;
        _coverage = coverage;
        _reconciliation = reconciliation;
        _metricConsistency = metricConsistency;
        _leakageAuditor = leakageAuditor;
        _verdictService = verdictService;
        _readiness = readiness;
        _trainingPreflight = trainingPreflight;
        _trainingLease = trainingLease;
        _trialRecovery = trialRecovery;
        _trainingSelection = trainingSelection;
        _selectionIntegrity = selectionIntegrity;
        _parameterFingerprint = parameterFingerprint;
        _riskBasis = riskBasis;
        _candleAccessAudits = candleAccessAudits;
        _candleAccessRecorder = candleAccessRecorder;
        _trainingScopeExecution = trainingScopeExecution;
        _segmentResultWriter = segmentResultWriter;
    }

    public async Task<ServiceResult<ValidationExperimentDto>> CreateExperimentAsync(
        CreateValidationExperimentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.StrategyCode))
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Strategy code is required.", "strategyCode");
        }

        if (request.RequestedEndUtc <= request.RequestedStartUtc)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("RequestedEndUtc must be after RequestedStartUtc.", "requestedEndUtc");
        }

        var symbol = await _symbols.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Symbol was not found.", "symbolId");
        }

        var exchange = await _exchanges.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Exchange was not found.", "exchangeId");
        }

        var strategyEnum = StrategyCodeExtensions.FromCode(request.StrategyCode);
        var strategyEntity = await _strategies.GetByCodeAsync(strategyEnum, cancellationToken);
        var version = string.IsNullOrWhiteSpace(request.StrategyVersion)
            ? strategyEntity?.Version ?? "1.0.0"
            : request.StrategyVersion!;

        var parameters = request.StrategyParameters is not null
            ? new Dictionary<string, string>(request.StrategyParameters, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var observation = NormalizeObservation(request.ObservationSettings);
        var maker = request.MakerFeeRate;
        var taker = request.TakerFeeRate;
        var slippage = request.SlippagePercent;

        if (request.SourceStrategyLabRunId is long sourceId)
        {
            var source = await _labRuns.GetByIdAsync(sourceId, cancellationToken);
            if (source is null)
            {
                return ServiceResult<ValidationExperimentDto>.Fail(
                    "Source Strategy Lab run was not found.",
                    "sourceStrategyLabRunId");
            }

            parameters = DeserializeStringDictionary(source.ParametersJson);
            observation = ExtractObservationSettings(source.StrategyFeatureFlagsJson) ?? observation;
            TryParseFees(source.FeeSettingsJson, out maker, out taker);
            TryParseSlippage(source.SlippageSettingsJson, out slippage);
            if (string.IsNullOrWhiteSpace(request.StrategyVersion))
            {
                version = source.StrategyVersion;
            }
        }

        var qualification = request.QualificationProfile ?? new ValidationQualificationProfileDto
        {
            PrimaryQualificationLayer = request.PrimaryQualificationLayer
        };
        if (request.QualificationProfile is not null && request.QualificationProfile.PrimaryQualificationLayer == default)
        {
            qualification = new ValidationQualificationProfileDto
            {
                ProfileVersion = request.QualificationProfile.ProfileVersion,
                PrimaryQualificationLayer = request.PrimaryQualificationLayer,
                MinimumTrainingClosedTrades = request.QualificationProfile.MinimumTrainingClosedTrades,
                MinimumValidationClosedTrades = request.QualificationProfile.MinimumValidationClosedTrades,
                MinimumTrainingProfitFactor = request.QualificationProfile.MinimumTrainingProfitFactor,
                MinimumValidationProfitFactor = request.QualificationProfile.MinimumValidationProfitFactor,
                MinimumTrainingNetExpectancyR = request.QualificationProfile.MinimumTrainingNetExpectancyR,
                MinimumValidationNetExpectancyR = request.QualificationProfile.MinimumValidationNetExpectancyR,
                MaximumTrainingDrawdownPercent = request.QualificationProfile.MaximumTrainingDrawdownPercent,
                MaximumValidationDrawdownPercent = request.QualificationProfile.MaximumValidationDrawdownPercent,
                MinimumOpportunityRetentionPercent = request.QualificationProfile.MinimumOpportunityRetentionPercent,
                MaximumAllowedExpectancyDegradation = request.QualificationProfile.MaximumAllowedExpectancyDegradation,
                MaximumSingleTradePnlContributionPercent = request.QualificationProfile.MaximumSingleTradePnlContributionPercent,
                RequirePositiveValidationNetPnl = request.QualificationProfile.RequirePositiveValidationNetPnl,
                RequirePositiveValidationNetExpectancy = request.QualificationProfile.RequirePositiveValidationNetExpectancy,
                RequireParameterStability = request.QualificationProfile.RequireParameterStability
            };
        }

        var draft = new DraftConfiguration
        {
            Parameters = parameters,
            ObservationSettings = observation,
            MakerFeeRate = maker,
            TakerFeeRate = taker,
            SlippagePercent = slippage,
            QualificationProfile = qualification,
            ParameterSearchSpaceOverrides = request.ParameterSearchSpaceOverrides is null
                ? null
                : new Dictionary<string, string>(request.ParameterSearchSpaceOverrides, StringComparer.OrdinalIgnoreCase),
            AutoImportMissingCandles = request.AutoImportMissingCandles
        };

        var experiment = new ValidationExperiment
        {
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"{request.StrategyCode} {symbol.SymbolName} Validation"
                : request.Name!,
            Description = request.Description,
            ExperimentType = request.ExperimentType,
            Status = ValidationExperimentStatus.Draft,
            StrategyCode = request.StrategyCode,
            StrategyVersion = version,
            SourceStrategyLabRunId = request.SourceStrategyLabRunId,
            ExchangeId = request.ExchangeId,
            Exchange = exchange.Name,
            SymbolId = request.SymbolId,
            Symbol = symbol.SymbolName,
            Timeframe = request.Timeframe,
            RequestedStartUtc = DateTime.SpecifyKind(request.RequestedStartUtc, DateTimeKind.Utc),
            RequestedEndUtc = DateTime.SpecifyKind(request.RequestedEndUtc, DateTimeKind.Utc),
            SplitRatio = request.SplitRatio <= 0m || request.SplitRatio >= 1m ? 0.70m : request.SplitRatio,
            RequiredWarmupCandles = request.RequiredWarmupCandles < 0 ? 100 : request.RequiredWarmupCandles,
            PrimaryQualificationLayer = request.PrimaryQualificationLayer,
            ValidationRevealStatus = ValidationRevealStatus.Hidden,
            DraftConfigurationJson = SerializeDraft(draft),
            QualificationProfileSnapshotJson = JsonSerializer.Serialize(qualification, JsonOptions),
            OptimizationObjectiveSnapshotJson = JsonSerializer.Serialize(new
            {
                version = ValidationTrainingScoreVersions.Current
            }, JsonOptions),
            ParameterSearchSpaceSnapshotJson = JsonSerializer.Serialize(
                draft.ParameterSearchSpaceOverrides ?? new Dictionary<string, string>(),
                JsonOptions),
            CandleDataSnapshotJson = "{}",
            CandleDataFingerprint = string.Empty,
            WarmupSnapshotJson = "{}",
            DiagnosticsJson = "[]",
            OverlayResultsJson = "{}",
            ComparisonJson = "{}",
            RegimeComparisonJson = "{}",
            ParameterStabilityJson = "{}",
            InitialBalance = request.InitialBalance > 0 ? request.InitialBalance : 10000m,
            MaximumTrials = request.MaximumTrials > 0 ? request.MaximumTrials : 50,
            DeterministicSeed = request.DeterministicSeed,
            PercentComplete = 0m,
            CreatedAtUtc = DateTime.UtcNow,
            CurrentStage = "Draft",
            ValidationMetricsVersion = ValidationMetricsContract.Current,
            RiskBasisVersion = ValidationRiskBasisService.Version,
            ParameterFingerprintVersion = ValidationParameterFingerprintService.Version,
            SelectionIntegrityVersion = "ValidationSelectionIntegrity/v1",
            HoldoutExclusivityPolicyVersion = ValidationHoldoutExclusivityVersions.Current,
            SegmentDetectorContinuityMode = SegmentDetectorContinuityMode.FreshSessionWithWarmup,
            ExpectancyMetric = ExpectancyMetricType.NetExpectancyR,
            ProfitFactorMetric = ProfitFactorMetricType.NetProfitFactor,
            ParameterStabilityApplicability =
                request.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration
                    ? ParameterStabilityApplicability.NotApplicable
                    : ParameterStabilityApplicability.Applicable
        };

        if (request.PrimaryQualificationLayer != ValidationPrimaryQualificationLayer.RawStrategy)
        {
            AppendDiagnostic(experiment, "PrimaryLayerWarning",
                "Validation of a gated layer does not replace proof of raw strategy edge.");
        }

        await _experiments.AddAsync(experiment, cancellationToken);
        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
    }

    public async Task<ServiceResult<ValidationExperimentDto>> UpdateExperimentAsync(
        long id,
        UpdateValidationExperimentRequest request,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.");
        }

        if (experiment.Status != ValidationExperimentStatus.Draft)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Only Draft experiments can be updated.");
        }

        var draft = ParseDraft(experiment.DraftConfigurationJson);

        if (request.Name is not null) experiment.Name = request.Name;
        if (request.Description is not null) experiment.Description = request.Description;
        if (request.RequestedStartUtc is not null)
            experiment.RequestedStartUtc = DateTime.SpecifyKind(request.RequestedStartUtc.Value, DateTimeKind.Utc);
        if (request.RequestedEndUtc is not null)
            experiment.RequestedEndUtc = DateTime.SpecifyKind(request.RequestedEndUtc.Value, DateTimeKind.Utc);
        if (request.SplitRatio is not null && request.SplitRatio > 0m && request.SplitRatio < 1m)
            experiment.SplitRatio = request.SplitRatio.Value;
        if (request.RequiredWarmupCandles is not null && request.RequiredWarmupCandles >= 0)
            experiment.RequiredWarmupCandles = request.RequiredWarmupCandles.Value;
        if (request.InitialBalance is not null && request.InitialBalance > 0)
            experiment.InitialBalance = request.InitialBalance.Value;
        if (request.MaximumTrials is not null && request.MaximumTrials > 0)
            experiment.MaximumTrials = request.MaximumTrials.Value;
        if (request.DeterministicSeed is not null)
            experiment.DeterministicSeed = request.DeterministicSeed.Value;
        if (request.PrimaryQualificationLayer is not null)
            experiment.PrimaryQualificationLayer = request.PrimaryQualificationLayer.Value;

        if (request.StrategyParameters is not null)
            draft.Parameters = new Dictionary<string, string>(request.StrategyParameters, StringComparer.OrdinalIgnoreCase);
        if (request.ParameterSearchSpaceOverrides is not null)
            draft.ParameterSearchSpaceOverrides = new Dictionary<string, string>(
                request.ParameterSearchSpaceOverrides, StringComparer.OrdinalIgnoreCase);
        if (request.ObservationSettings is not null)
            draft.ObservationSettings = NormalizeObservation(request.ObservationSettings);
        if (request.MakerFeeRate is not null) draft.MakerFeeRate = request.MakerFeeRate.Value;
        if (request.TakerFeeRate is not null) draft.TakerFeeRate = request.TakerFeeRate.Value;
        if (request.SlippagePercent is not null) draft.SlippagePercent = request.SlippagePercent.Value;
        if (request.QualificationProfile is not null)
        {
            draft.QualificationProfile = request.QualificationProfile;
            experiment.QualificationProfileSnapshotJson = JsonSerializer.Serialize(request.QualificationProfile, JsonOptions);
        }

        experiment.DraftConfigurationJson = SerializeDraft(draft);
        experiment.ParameterSearchSpaceSnapshotJson = JsonSerializer.Serialize(
            draft.ParameterSearchSpaceOverrides ?? new Dictionary<string, string>(),
            JsonOptions);
        experiment.UpdatedAtUtc = DateTime.UtcNow;

        await _experiments.UpdateAsync(experiment, cancellationToken);
        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
    }

    public async Task<ServiceResult<ValidationExperimentDto>> GetExperimentAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        return experiment is null
            ? ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.")
            : ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
    }

    public async Task<ServiceResult<ValidationExperimentDetailDto>> GetExperimentDetailAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail("Validation experiment was not found.");
        }

        var segments = await _segments.GetByExperimentIdAsync(id, cancellationToken);
        var revealed = ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus);
        return ServiceResult<ValidationExperimentDetailDto>.Ok(MapDetail(experiment, segments, redactValidation: !revealed));
    }

    public async Task<ServiceResult<IReadOnlyList<ValidationExperimentDto>>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var items = await _experiments.GetRecentAsync(Math.Clamp(limit, 1, 200), cancellationToken);
        return ServiceResult<IReadOnlyList<ValidationExperimentDto>>.Ok(items.Select(MapDto).ToList());
    }

    public async Task<ServiceResult<ValidationExperimentDto>> PrepareDataAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.");
        }

        var allowRetry = experiment.Status == ValidationExperimentStatus.Failed
            && string.Equals(experiment.CurrentStage, "PrepareData", StringComparison.OrdinalIgnoreCase);
        if (!ValidationLifecycleGate.CanPrepareData(experiment.Status) && !allowRetry
            && experiment.Status != ValidationExperimentStatus.Draft)
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                $"Cannot prepare data from status {experiment.Status}.");
        }

        if (experiment.Status is not (ValidationExperimentStatus.Draft or ValidationExperimentStatus.Failed
            or ValidationExperimentStatus.DataPreparing))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                $"Cannot prepare data from status {experiment.Status}.");
        }

        experiment.Status = ValidationExperimentStatus.DataPreparing;
        experiment.CurrentStage = "PrepareData";
        experiment.ErrorMessage = null;
        experiment.PercentComplete = 5m;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        try
        {
            if (!TimeframeParser.TryParse(experiment.Timeframe, out var timeframe))
            {
                throw new InvalidOperationException($"Invalid timeframe '{experiment.Timeframe}'.");
            }

            var draft = ParseDraft(experiment.DraftConfigurationJson);
            if (draft.AutoImportMissingCandles)
            {
                var coverage = await _coverage.EnsureCoverageAsync(
                    experiment.ExchangeId,
                    experiment.SymbolId,
                    experiment.Timeframe,
                    experiment.RequestedStartUtc,
                    experiment.RequestedEndUtc,
                    experiment.RequiredWarmupCandles,
                    allowAutoImport: true,
                    cancellationToken: cancellationToken);
                if (!coverage.Succeeded)
                {
                    throw new InvalidOperationException(coverage.ErrorMessage ?? "Candle coverage failed.");
                }
            }

            var candles = await _candles.GetCandlesChronologicalAsync(
                experiment.SymbolId,
                timeframe,
                experiment.RequestedStartUtc,
                experiment.RequestedEndUtc,
                warmUpCount: 0,
                cancellationToken);

            var openTimes = candles.Select(c => c.OpenTimeUtc).ToList();
            var tfMinutes = TimeframeParser.GetDurationMinutes(timeframe);
            var split = ChronologicalHoldoutSplit.Split(
                openTimes,
                experiment.SplitRatio,
                experiment.RequiredWarmupCandles,
                timeframeMinutes: tfMinutes);

            if (!split.IsValid)
            {
                throw new InvalidOperationException(split.FailureReason ?? "Holdout split failed.");
            }

            // Prefer real candle-index warmup when prior candles exist in the loaded set.
            var ordered = candles.OrderBy(c => c.OpenTimeUtc).ToList();
            var trainingStartIndex = ordered.FindIndex(c => c.OpenTimeUtc == split.TrainingStartUtc);
            DateTime trainingWarmupStart;
            if (trainingStartIndex >= 0)
            {
                var warmupIndex = Math.Max(0, trainingStartIndex - experiment.RequiredWarmupCandles);
                trainingWarmupStart = ordered[warmupIndex].OpenTimeUtc;
            }
            else
            {
                trainingWarmupStart = split.TrainingStartUtc;
            }

            var fingerprint = ValidationCandleFingerprint.Build(ordered);
            var snapshotJson = ValidationCandleFingerprint.BuildSnapshotJson(
                experiment.Exchange,
                experiment.Symbol,
                experiment.Timeframe,
                experiment.RequestedStartUtc,
                experiment.RequestedEndUtc,
                ordered,
                dataSource: "local",
                missingCandleCount: 0,
                duplicateCandleCount: 0,
                gapDiagnosticsJson: null,
                importBatchIds: null);

            experiment.TotalEligibleCandleCount = split.TotalEligibleCandleCount;
            experiment.TrainingCandleCount = split.TrainingCandleCount;
            experiment.ValidationCandleCount = split.ValidationCandleCount;
            experiment.TrainingStartUtc = split.TrainingStartUtc;
            experiment.TrainingEndUtc = split.TrainingEndUtc;
            experiment.ValidationStartUtc = split.ValidationStartUtc;
            experiment.ValidationEndUtc = split.ValidationEndUtc;
            experiment.SplitCandleOpenTimeUtc = split.SplitCandleOpenTimeUtc;
            experiment.TrainingWarmupStartUtc = trainingWarmupStart;
            experiment.ValidationWarmupStartUtc = split.ValidationWarmupStartUtc;
            experiment.SplitAlgorithmVersion = split.AlgorithmVersion;
            experiment.CandleDataFingerprint = fingerprint;
            experiment.CandleDataSnapshotJson = snapshotJson;
            experiment.WarmupSnapshotJson = JsonSerializer.Serialize(new
            {
                requiredWarmupCandles = experiment.RequiredWarmupCandles,
                trainingWarmupStartUtc = trainingWarmupStart,
                validationWarmupStartUtc = split.ValidationWarmupStartUtc,
                algorithmVersion = experiment.WarmupAlgorithmVersion
            }, JsonOptions);
            experiment.Status = ValidationExperimentStatus.DataReady;
            experiment.PercentComplete = 20m;
            experiment.CurrentStage = "DataReady";
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
        }
        catch (Exception ex)
        {
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.ErrorMessage = ex.Message;
            experiment.CurrentStage = "PrepareData";
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            AppendDiagnostic(experiment, "PrepareDataFailed", ex.Message);
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(ex.Message);
        }
    }

    public Task<ServiceResult<ValidationExperimentDto>> RunTrainingAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        ExecuteDurableTrainingAsync(id, isResume: false, cancellationToken);

    public Task<ServiceResult<ValidationExperimentDto>> ResumeTrainingAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        ExecuteDurableTrainingAsync(id, isResume: true, cancellationToken);

    public async Task<ServiceResult<IReadOnlyList<ValidationParameterTrialDto>>> GetTrainingTrialsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<IReadOnlyList<ValidationParameterTrialDto>>.Fail("Validation experiment was not found.");
        }

        var trials = await _trials.GetByExperimentIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<ValidationParameterTrialDto>>.Ok(trials.Select(MapTrial).ToList());
    }

    public async Task<ServiceResult<ValidationExperimentDto>> FreezeAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.");
        }

        if (!ValidationLifecycleGate.CanFreeze(experiment.Status))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                $"Freeze requires TrainingCompleted status (current: {experiment.Status}).");
        }

        if (experiment.LeakageAuditStatus == ValidationLeakageAuditStatus.Failed)
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                "Freeze blocked: ValidationDataLeakageDetected. Training optimizer accessed validation-range data.");
        }

        var trialEntities = (await _trials.GetByExperimentIdAsync(id, cancellationToken)).ToList();
        if (!_selectionIntegrity.CanFreeze(experiment, trialEntities, out var freezeBlockReason))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(freezeBlockReason ?? "Freeze blocked by selection integrity.");
        }

        if (string.IsNullOrWhiteSpace(experiment.SelectedTrialParameterSnapshotJson))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                "Freeze blocked: no selected trial parameter snapshot is available.");
        }

        var snapshotStatus = _parameterFingerprint.ValidateParameterSnapshot(
            experiment.SelectedTrialParameterSnapshotJson,
            requireNonEmptyParameters: experiment.ExperimentType == ValidationExperimentType.TrainingSearchHoldoutValidation);
        if (snapshotStatus != FrozenSnapshotValidationStatus.Valid)
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                $"Freeze blocked: selected parameter snapshot is {snapshotStatus}.");
        }

        var frozenFingerprint = _parameterFingerprint.ComputeFingerprintFromSnapshotJson(
            experiment.SelectedTrialParameterSnapshotJson);
        if (_parameterFingerprint.IsEmptyContentFingerprint(frozenFingerprint))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                "Freeze blocked: frozen parameter fingerprint is the empty-content artifact.");
        }

        var draft = ParseDraft(experiment.DraftConfigurationJson);
        draft.Parameters = DeserializeStringDictionary(experiment.SelectedTrialParameterSnapshotJson);
        var paramsJson = experiment.SelectedTrialParameterSnapshotJson;
        experiment.FrozenStrategyParameterSnapshotJson = paramsJson;
        experiment.FrozenParameterFingerprint = frozenFingerprint;
        experiment.FreezeSource = "SelectedEligibleTrainingTrial";
        experiment.FrozenSnapshotValidationStatus = snapshotStatus;
        if (experiment.SelectedTrialParameterFingerprint is not null
            && !string.Equals(experiment.SelectedTrialParameterFingerprint, frozenFingerprint, StringComparison.Ordinal))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                "Freeze blocked: selected and frozen parameter fingerprints do not match.");
        }

        experiment.SelectedTrialParameterFingerprint = frozenFingerprint;
        experiment.FrozenConfidenceSnapshotJson = JsonSerializer.Serialize(draft.ObservationSettings, JsonOptions);
        experiment.FrozenRiskSnapshotJson = JsonSerializer.Serialize(new
        {
            draft.ObservationSettings?.RiskProfileId,
            draft.ObservationSettings?.RiskApprovalThreshold,
            draft.ObservationSettings?.RiskPerTradePercent,
            draft.ObservationSettings?.PreferredLeverage,
            draft.ObservationSettings?.MaximumLeverage,
            draft.ObservationSettings?.UseSystemDefaultRiskSettings
        }, JsonOptions);
        experiment.FrozenCostModelSnapshotJson = JsonSerializer.Serialize(new
        {
            makerFeeRate = draft.MakerFeeRate,
            takerFeeRate = draft.TakerFeeRate,
            slippagePercent = draft.SlippagePercent
        }, JsonOptions);

        var feeJson = JsonSerializer.Serialize(new { makerFeeRate = draft.MakerFeeRate, takerFeeRate = draft.TakerFeeRate }, JsonOptions);
        var slipJson = JsonSerializer.Serialize(new { slippagePercent = draft.SlippagePercent }, JsonOptions);
        var featureFlagsJson = JsonSerializer.Serialize(new { observationSettings = draft.ObservationSettings }, JsonOptions);
        var from = experiment.ValidationStartUtc ?? experiment.TrainingStartUtc ?? experiment.RequestedStartUtc;
        var to = experiment.ValidationEndUtc ?? experiment.TrainingEndUtc ?? experiment.RequestedEndUtc;
        experiment.FrozenStrategyFingerprint = ExperimentFingerprintBuilder.Build(
            experiment.StrategyCode,
            experiment.StrategyVersion,
            experiment.ExchangeId,
            experiment.SymbolId,
            experiment.Symbol,
            experiment.Timeframe,
            from,
            to,
            StrategyLabExecutionMode.FullPipelineComparison,
            draft.Parameters,
            featureFlagsJson,
            experiment.InitialBalance,
            feeJson,
            slipJson);

        experiment.FrozenAtUtc = DateTime.UtcNow;
        experiment.Status = ValidationExperimentStatus.ConfigurationFrozen;
        experiment.ValidationRevealStatus = ValidationRevealStatus.Frozen;
        experiment.CurrentStage = "ConfigurationFrozen";
        experiment.PercentComplete = 80m;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);
        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
    }

    public async Task<ServiceResult<ValidationExperimentDetailDto>> RunValidationAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail("Validation experiment was not found.");
        }

        if (!ValidationLifecycleGate.CanRunValidation(experiment.Status))
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail(
                $"Validation requires ConfigurationFrozen status (current: {experiment.Status}).");
        }

        var trialEntities = (await _trials.GetByExperimentIdAsync(id, cancellationToken)).ToList();
        if (!_selectionIntegrity.CanStartValidation(experiment, trialEntities, out var validationBlockReason))
        {
            AppendDiagnostic(experiment, "ValidationStartedWithoutEligibleTrainingWinner", validationBlockReason ?? string.Empty);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return ServiceResult<ValidationExperimentDetailDto>.Fail(validationBlockReason ?? "Validation blocked by selection integrity.");
        }

        if (experiment.ValidationStartUtc is null || experiment.ValidationEndUtc is null)
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail("Validation date range is missing.");
        }

        if (string.IsNullOrWhiteSpace(experiment.FrozenStrategyParameterSnapshotJson)
            || string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint))
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail("Frozen configuration is incomplete.");
        }

        experiment.Status = ValidationExperimentStatus.ValidationRunning;
        experiment.CurrentStage = "Validation";
        experiment.PercentComplete = 85m;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        // Holdout validation can exceed HTTP client timeouts; ignore request abort after accept.
        cancellationToken = CancellationToken.None;

        try
        {
            var frozenParams = DeserializeStringDictionary(experiment.FrozenStrategyParameterSnapshotJson!);
            var draft = ParseDraft(experiment.DraftConfigurationJson);
            draft.Parameters = frozenParams;
            if (!string.IsNullOrWhiteSpace(experiment.FrozenConfidenceSnapshotJson))
            {
                draft.ObservationSettings = JsonSerializer.Deserialize<StrategyLabObservationSettingsDto>(
                    experiment.FrozenConfidenceSnapshotJson, JsonOptions) ?? draft.ObservationSettings;
            }

            if (!string.IsNullOrWhiteSpace(experiment.FrozenCostModelSnapshotJson))
            {
                TryParseFees(experiment.FrozenCostModelSnapshotJson, out var m, out var t);
                TryParseSlippage(experiment.FrozenCostModelSnapshotJson, out var s);
                draft.MakerFeeRate = m;
                draft.TakerFeeRate = t;
                draft.SlippagePercent = s;
            }

            var run = await CreateAndExecuteLabRunAsync(
                experiment,
                frozenParams,
                draft,
                experiment.ValidationStartUtc.Value,
                ToExclusiveUtc(experiment.ValidationEndUtc.Value, experiment.Timeframe),
                $"VL-Val-{experiment.Id}",
                cancellationToken);

            experiment.ValidationStrategyLabRunId = run.Id;
            await _segmentResultWriter.BuildAndPersistSegmentResultsAsync(
                experiment,
                run.Id,
                ValidationSegmentType.Validation,
                experiment.ValidationCandleCount,
                cancellationToken);

            var allSegments = await _segments.GetByExperimentIdAsync(experiment.Id, cancellationToken);
            var comparisons = new List<LayerComparisonDto>();
            var overlays = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (ValidationLayerType layer in Enum.GetValues(typeof(ValidationLayerType)))
            {
                var train = allSegments.FirstOrDefault(s =>
                    s.SegmentType == ValidationSegmentType.Training && s.LayerType == layer);
                var val = allSegments.FirstOrDefault(s =>
                    s.SegmentType == ValidationSegmentType.Validation && s.LayerType == layer);
                if (train is null || val is null) continue;

                var trainMetrics = DeserializeMetrics(train.MetricsJson) ?? ToMetricsFromSegment(train);
                var valMetrics = DeserializeMetrics(val.MetricsJson) ?? ToMetricsFromSegment(val);
                comparisons.Add(ValidationComparisonCalculator.Compare(layer.ToString(), trainMetrics, valMetrics));

                if (layer != ValidationLayerType.RawStrategy)
                {
                    var rawVal = allSegments.FirstOrDefault(s =>
                        s.SegmentType == ValidationSegmentType.Validation
                        && s.LayerType == ValidationLayerType.RawStrategy);
                    if (rawVal is not null)
                    {
                        var rawMetrics = DeserializeMetrics(rawVal.MetricsJson) ?? ToMetricsFromSegment(rawVal);
                        overlays[layer.ToString()] = new
                        {
                            status = ValidationRobustnessEvaluator.CompareOverlay(
                                rawMetrics, valMetrics, minSample: 5).ToString(),
                            layer = layer.ToString()
                        };
                    }
                }
            }

            experiment.ComparisonJson = ValidationComparisonCalculator.Serialize(comparisons);
            experiment.OverlayResultsJson = JsonSerializer.Serialize(overlays, JsonOptions);

            // Regime comparison
            if (TimeframeParser.TryParse(experiment.Timeframe, out var tf)
                && experiment.TrainingStartUtc is not null
                && experiment.TrainingEndUtc is not null)
            {
                var trainCandles = await _candles.GetCandlesChronologicalAsync(
                    experiment.SymbolId, tf, experiment.TrainingStartUtc,
                    ToExclusiveUtc(experiment.TrainingEndUtc.Value, experiment.Timeframe), 0, cancellationToken);
                var valCandles = await _candles.GetCandlesChronologicalAsync(
                    experiment.SymbolId, tf, experiment.ValidationStartUtc,
                    ToExclusiveUtc(experiment.ValidationEndUtc!.Value, experiment.Timeframe), 0, cancellationToken);
                var regime = ValidationRegimeAnalyzer.Compare(trainCandles, valCandles);
                experiment.RegimeComparisonJson = ValidationRegimeAnalyzer.Serialize(regime);
            }

            var priors = await _experiments.GetByStrategyFingerprintOverlapAsync(
                experiment.StrategyCode,
                experiment.StrategyVersion,
                experiment.Symbol,
                experiment.Timeframe,
                cancellationToken);
            var reuse = ValidationHoldoutReuseDetector.Detect(experiment, priors);
            if (reuse.RepeatedHoldoutExposure)
            {
                AppendDiagnostic(experiment, "RepeatedHoldoutExposure",
                    $"Holdout reuse detected (overlap {reuse.OverlapPercent}%, risk={reuse.ContaminationRisk}).");
            }

            var profile = ToQualificationProfile(
                JsonSerializer.Deserialize<ValidationQualificationProfileDto>(
                    experiment.QualificationProfileSnapshotJson, JsonOptions),
                experiment.PrimaryQualificationLayer);
            profile = ApplyExperimentMetricSelections(profile, experiment);

            var primaryLayer = MapPrimaryToLayer(experiment.PrimaryQualificationLayer);
            var trainPrimary = allSegments.FirstOrDefault(s =>
                s.SegmentType == ValidationSegmentType.Training && s.LayerType == primaryLayer);
            var valPrimary = allSegments.FirstOrDefault(s =>
                s.SegmentType == ValidationSegmentType.Validation && s.LayerType == primaryLayer);

            // Candidate reconciliation vs source full-range run.
            IReadOnlyList<StrategyResearchCandidate> fullCandidates = [];
            IReadOnlyList<StrategyResearchCandidate> trainCandidates = [];
            IReadOnlyList<StrategyResearchCandidate> valCandidates = [];
            if (experiment.SourceStrategyLabRunId is long sourceRunId)
            {
                fullCandidates = await _candidates.GetByRunIdAsync(sourceRunId, cancellationToken);
            }

            if (experiment.TrainingStrategyLabRunId is long trainRunId)
            {
                trainCandidates = await _candidates.GetByRunIdAsync(trainRunId, cancellationToken);
            }

            if (experiment.ValidationStrategyLabRunId is long valRunId)
            {
                valCandidates = await _candidates.GetByRunIdAsync(valRunId, cancellationToken);
            }

            var reconciliation = _reconciliation.Reconcile(
                experiment, fullCandidates, trainCandidates, valCandidates);
            experiment.CandidateReconciliationJson =
                ValidationCandidateReconciliationService.Serialize(reconciliation);
            experiment.CandidateReconciliationStatus = reconciliation.ReconciliationStatus;
            if (reconciliation.OverlappingFingerprintCount > 0)
            {
                AppendDiagnostic(experiment, "DetectorSessionBoundaryEffect",
                    $"Overlapping fingerprints ({reconciliation.OverlappingFingerprintCount}): "
                    + string.Join(", ", reconciliation.OverlappingFingerprints.Take(10)));
            }

            var stabilityOk = true;
            var stabilityApplicability = experiment.ParameterStabilityApplicability
                ?? (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration
                    ? ParameterStabilityApplicability.NotApplicable
                    : ParameterStabilityApplicability.Applicable);
            if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
            {
                stabilityApplicability = ParameterStabilityApplicability.NotApplicable;
                experiment.ParameterStabilityApplicability = ParameterStabilityApplicability.NotApplicable;
                stabilityOk = true;
            }
            else
            {
                try
                {
                    using var stabDoc = JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(experiment.ParameterStabilityJson)
                            ? "{}"
                            : experiment.ParameterStabilityJson);
                    if (stabDoc.RootElement.TryGetProperty("applicability", out var appEl)
                        || stabDoc.RootElement.TryGetProperty("Applicability", out appEl))
                    {
                        if (Enum.TryParse(appEl.GetString(), true, out ParameterStabilityApplicability parsedApp))
                        {
                            stabilityApplicability = parsedApp;
                        }
                    }

                    if (stabDoc.RootElement.TryGetProperty("isStable", out var isStableEl)
                        || stabDoc.RootElement.TryGetProperty("IsStable", out isStableEl))
                    {
                        stabilityOk = isStableEl.GetBoolean();
                    }
                }
                catch
                {
                    stabilityOk = true;
                }
            }

            var trainM = trainPrimary is null
                ? new LayerSegmentMetrics()
                : DeserializeMetrics(trainPrimary.MetricsJson) ?? ToMetricsFromSegment(trainPrimary);
            var valM = valPrimary is null
                ? new LayerSegmentMetrics()
                : DeserializeMetrics(valPrimary.MetricsJson) ?? ToMetricsFromSegment(valPrimary);

            var consistencyTrain = _metricConsistency.Validate(trainM, qualificationExpectancyMetric: experiment.ExpectancyMetric);
            var consistencyVal = _metricConsistency.Validate(valM, qualificationExpectancyMetric: experiment.ExpectancyMetric);
            var metricOk = consistencyTrain.IsConsistent && consistencyVal.IsConsistent;
            foreach (var d in consistencyTrain.Diagnostics.Concat(consistencyVal.Diagnostics))
            {
                AppendDiagnostic(experiment, d.Key, d.Message);
            }

            experiment.MetricConsistencyJson = JsonSerializer.Serialize(new
            {
                training = consistencyTrain,
                validation = consistencyVal,
                isConsistent = metricOk,
                evaluatedAtUtc = DateTime.UtcNow
            }, JsonOptions);
            experiment.MetricConsistencyStatus = metricOk ? "Passed" : "Failed";
            if (!metricOk)
            {
                AppendDiagnostic(experiment, "MetricConsistencyFailed",
                    "Metric consistency failed — Passed verdict is blocked.");
            }

            var verdict = _verdictService.Evaluate(
                trainM,
                valM,
                profile,
                experiment.ExperimentType,
                stabilityApplicability,
                stabilityOk,
                dataQualityOk: true,
                configurationMatch: true,
                reconciliation.ReconciliationStatus,
                experiment.LeakageAuditStatus,
                metricOk);
            experiment.StrategyRobustnessDecision = verdict.Decision;
            experiment.PrimaryFailureReason = verdict.PrimaryFailureReason;
            experiment.FailureReasonsJson = JsonSerializer.Serialize(verdict.FailureReasons, JsonOptions);
            experiment.QualificationRuleResultsJson =
                ValidationVerdictService.SerializeRules(verdict.StructuredRuleResults);
            experiment.DecisionExplanation = verdict.Explanation;
            experiment.DecidedAtUtc = DateTime.UtcNow;
            experiment.BoundaryCensoredCount = valPrimary?.BoundaryCensoredCount ?? 0;
            experiment.ValidationRevealStatus = ValidationRevealStatus.Revealed;
            experiment.ValidationRevealedAtUtc = DateTime.UtcNow;
            experiment.ValidationRevealedBy = "system";
            experiment.Status = ValidationExperimentStatus.Completed;
            experiment.CurrentStage = "Completed";
            experiment.PercentComplete = 100m;
            experiment.ValidationLaboratoryReadinessStatus = _readiness.EvaluateExperiment(experiment);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);

            var segments = await _segments.GetByExperimentIdAsync(experiment.Id, cancellationToken);
            return ServiceResult<ValidationExperimentDetailDto>.Ok(MapDetail(experiment, segments, redactValidation: false));
        }
        catch (Exception ex)
        {
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.ErrorMessage = ex.Message;
            experiment.CurrentStage = "Validation";
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            AppendDiagnostic(experiment, "ValidationFailed", ex.Message);
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return ServiceResult<ValidationExperimentDetailDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<object>> GetComparisonAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        if (!ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus))
        {
            return ServiceResult<object>.Ok(new
            {
                revealed = false,
                message = "Validation performance is hidden until holdout validation is revealed.",
                comparison = (object?)null
            });
        }

        object? comparison = null;
        try
        {
            comparison = JsonSerializer.Deserialize<object>(experiment.ComparisonJson, JsonOptions);
        }
        catch
        {
            comparison = experiment.ComparisonJson;
        }

        return ServiceResult<object>.Ok(new { revealed = true, comparison });
    }

    public async Task<ServiceResult<object>> GetConfidenceAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        var runId = PreferRevealedRunId(experiment);
        if (runId is null)
        {
            return ServiceResult<object>.Ok(new
            {
                frozenConfidence = SafeParseJson(experiment.FrozenConfidenceSnapshotJson),
                analysis = (object?)null
            });
        }

        var run = await _labRuns.GetByIdAsync(runId.Value, cancellationToken);
        var candidates = await _candidates.GetByRunIdAsync(runId.Value, cancellationToken);
        var analysis = run is null ? null : StrategyLabGateAnalysisCalculator.Build(run, candidates);
        return ServiceResult<object>.Ok(new
        {
            frozenConfidence = SafeParseJson(experiment.FrozenConfidenceSnapshotJson),
            revealed = ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus),
            analysis
        });
    }

    public async Task<ServiceResult<object>> GetRiskAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        var runId = PreferRevealedRunId(experiment);
        if (runId is null)
        {
            return ServiceResult<object>.Ok(new
            {
                frozenRisk = SafeParseJson(experiment.FrozenRiskSnapshotJson),
                analysis = (object?)null
            });
        }

        var candidates = await _candidates.GetByRunIdAsync(runId.Value, cancellationToken);
        var analysis = StrategyLabRiskAnalysisCalculator.Build(candidates);
        return ServiceResult<object>.Ok(new
        {
            frozenRisk = SafeParseJson(experiment.FrozenRiskSnapshotJson),
            revealed = ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus),
            analysis
        });
    }

    public async Task<ServiceResult<PagedResultDto<object>>> GetCandidatesAsync(
        long id,
        ValidationCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<PagedResultDto<object>>.Fail("Validation experiment was not found.");
        }

        var wantOverlapOnly = query.CrossSegmentOverlapOnly == true
            || query.Segment == ValidationSegmentClassification.Invalid
            || string.Equals(
                query.MetricClassification,
                nameof(ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(query.MetricClassification, "CrossSegmentOverlap", StringComparison.OrdinalIgnoreCase);

        var segment = query.Segment ?? ValidationSegmentClassification.Training;
        if (wantOverlapOnly)
        {
            // Cross-segment overlaps live on the validation run (audit-only population).
            segment = ValidationSegmentClassification.Validation;
        }

        if (segment == ValidationSegmentClassification.Validation
            && !ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus))
        {
            return ServiceResult<PagedResultDto<object>>.Fail(
                "Validation candidates are hidden until holdout validation is revealed.");
        }

        long? runId = segment switch
        {
            ValidationSegmentClassification.Validation => experiment.ValidationStrategyLabRunId,
            _ => experiment.TrainingStrategyLabRunId
        };

        if (runId is null)
        {
            return ServiceResult<PagedResultDto<object>>.Ok(new PagedResultDto<object>
            {
                Items = [],
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = 0,
                TotalPages = 0
            });
        }

        var candidates = await _candidates.GetByRunIdAsync(runId.Value, cancellationToken);
        IEnumerable<StrategyResearchCandidate> filtered = candidates;
        if (query.Layer is ValidationLayerType layer)
        {
            filtered = layer switch
            {
                ValidationLayerType.ConfidenceQualified =>
                    candidates.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved),
                ValidationLayerType.RiskOnly =>
                    candidates.Where(c => c.RiskDecision == ResearchRiskDecision.Approved),
                ValidationLayerType.FullPipeline =>
                    candidates.Where(c => c.FinalPipelineDecision == ResearchFinalPipelineDecision.Approved),
                _ => candidates
            };
        }

        Dictionary<long, CandidateMetricClassificationRow>? classificationById = null;
        HoldoutExclusivityReport? exclusivityReport = null;
        if (!string.IsNullOrWhiteSpace(experiment.HoldoutExclusivityJson))
        {
            try
            {
                exclusivityReport = JsonSerializer.Deserialize<HoldoutExclusivityReport>(
                    experiment.HoldoutExclusivityJson, JsonOptions);
                if (exclusivityReport?.Classifications is { Count: > 0 })
                {
                    classificationById = exclusivityReport.Classifications
                        .GroupBy(c => c.CandidateId)
                        .ToDictionary(g => g.Key, g => g.First());
                }
            }
            catch
            {
                exclusivityReport = null;
            }
        }

        if (wantOverlapOnly && exclusivityReport is not null)
        {
            var overlapIds = exclusivityReport.Classifications
                .Where(c => c.MetricClassification ==
                            ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation)
                .Select(c => c.CandidateId)
                .ToHashSet();
            filtered = filtered.Where(c => overlapIds.Contains(c.Id));
        }
        else if (!string.IsNullOrWhiteSpace(query.MetricClassification)
                 && exclusivityReport is not null
                 && Enum.TryParse(query.MetricClassification, true, out ValidationCandidateMetricClassification parsedClass))
        {
            var ids = exclusivityReport.Classifications
                .Where(c => c.MetricClassification == parsedClass)
                .Select(c => c.CandidateId)
                .ToHashSet();
            filtered = filtered.Where(c => ids.Contains(c.Id));
        }

        var list = filtered.OrderBy(c => c.SetupDetectedAtUtc).ToList();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var total = list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c =>
            {
                CandidateMetricClassificationRow? row = null;
                if (classificationById is not null)
                {
                    classificationById.TryGetValue(c.Id, out row);
                }

                return (object)new
                {
                    c.Id,
                    c.SetupDetectedAtUtc,
                    Direction = c.Direction.ToString(),
                    c.ProposedEntryPrice,
                    c.StopLoss,
                    c.Target1,
                    c.RewardRisk,
                    RawOutcomeStatus = c.RawOutcomeStatus.ToString(),
                    c.RawNetPnl,
                    c.RawRMultiple,
                    c.ConfidenceScore,
                    ConfidenceDecision = c.ConfidenceDecision?.ToString(),
                    RiskDecision = c.RiskDecision?.ToString(),
                    FinalPipelineDecision = c.FinalPipelineDecision?.ToString(),
                    c.SetupFingerprint,
                    Segment = segment.ToString(),
                    metricClassification = row?.MetricClassification.ToString(),
                    portfolioMutationAllowed = row?.PortfolioMutationAllowed,
                    metricExclusionReason = row?.MetricExclusionReason,
                    exclusivityNote = row is null
                        ? null
                        : row.MetricClassification ==
                          ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation
                            ? "Audit-only: fingerprint already owned by training under holdout exclusivity."
                            : row.MetricClassification == ValidationCandidateMetricClassification.BoundaryCensored
                                ? "Boundary-censored: setup in training, exit at/after validation start."
                                : null
                };
            }).ToList();

        return ServiceResult<PagedResultDto<object>>.Ok(new PagedResultDto<object>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    public async Task<ServiceResult<object>> GetDiagnosticsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        var priors = await _experiments.GetByStrategyFingerprintOverlapAsync(
            experiment.StrategyCode,
            experiment.StrategyVersion,
            experiment.Symbol,
            experiment.Timeframe,
            cancellationToken);
        var reuse = ValidationHoldoutReuseDetector.Detect(experiment, priors);

        return ServiceResult<object>.Ok(new
        {
            diagnostics = SafeParseJson(experiment.DiagnosticsJson),
            holdoutReuse = reuse,
            candleDataFingerprint = experiment.CandleDataFingerprint,
            frozenParameterFingerprint = experiment.FrozenParameterFingerprint,
            frozenStrategyFingerprint = experiment.FrozenStrategyFingerprint,
            errorMessage = experiment.ErrorMessage,
            status = experiment.Status.ToString(),
            revealStatus = experiment.ValidationRevealStatus.ToString()
        });
    }

    public async Task<ServiceResult<object>> GetReconciliationAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        if (string.IsNullOrWhiteSpace(experiment.CandidateReconciliationJson))
        {
            return ServiceResult<object>.Ok(new
            {
                available = false,
                status = experiment.CandidateReconciliationStatus?.ToString(),
                message = "Candidate reconciliation has not been computed yet."
            });
        }

        return ServiceResult<object>.Ok(new
        {
            available = true,
            status = experiment.CandidateReconciliationStatus?.ToString(),
            report = SafeParseJson(experiment.CandidateReconciliationJson)
        });
    }

    public async Task<ServiceResult<object>> GetLeakageAuditAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        if (string.IsNullOrWhiteSpace(experiment.LeakageAuditJson))
        {
            return ServiceResult<object>.Ok(new
            {
                available = false,
                status = experiment.LeakageAuditStatus?.ToString() ?? ValidationLeakageAuditStatus.NotAvailable.ToString(),
                message = "Leakage audit has not been computed yet."
            });
        }

        return ServiceResult<object>.Ok(new
        {
            available = true,
            status = experiment.LeakageAuditStatus?.ToString(),
            report = SafeParseJson(experiment.LeakageAuditJson)
        });
    }

    public async Task<ServiceResult<object>> GetExclusivityAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<object>.Fail("Validation experiment was not found.");
        }

        if (string.IsNullOrWhiteSpace(experiment.HoldoutExclusivityJson))
        {
            return ServiceResult<object>.Ok(new
            {
                available = false,
                policyVersion = experiment.HoldoutExclusivityPolicyVersion,
                crossSegmentOverlapCount = experiment.CrossSegmentOverlapCount,
                message = "Holdout exclusivity has not been computed yet."
            });
        }

        return ServiceResult<object>.Ok(new
        {
            available = true,
            policyVersion = experiment.HoldoutExclusivityPolicyVersion,
            crossSegmentOverlapCount = experiment.CrossSegmentOverlapCount,
            report = SafeParseJson(experiment.HoldoutExclusivityJson)
        });
    }

    public Task<ServiceResult<ValidationLaboratoryReadinessReport>> GetReadinessAsync(
        CancellationToken cancellationToken = default) =>
        _readiness.GetReadinessAsync(cancellationToken);

    public async Task RecordExportVerificationAsync(
        long experimentId,
        ExportVerificationResult result,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(experimentId, cancellationToken);
        if (experiment is null) return;

        experiment.ExportVerificationJson = JsonSerializer.Serialize(result, JsonOptions);
        experiment.ExportVerificationStatus = result.Status;
        experiment.ValidationLaboratoryReadinessStatus = _readiness.EvaluateExperiment(experiment);
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);
    }

    public async Task<ServiceResult<ValidationExperimentDetailDto>> RecalculateVerdictAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail("Validation experiment was not found.");
        }

        // Recompute candidate reconciliation (e.g. TrainingSearch segment-only baseline).
        IReadOnlyList<StrategyResearchCandidate> fullCandidates = [];
        IReadOnlyList<StrategyResearchCandidate> trainCandidates = [];
        IReadOnlyList<StrategyResearchCandidate> valCandidates = [];
        if (experiment.SourceStrategyLabRunId is long sourceRunId)
            fullCandidates = await _candidates.GetByRunIdAsync(sourceRunId, cancellationToken);
        if (experiment.TrainingStrategyLabRunId is long trainRunId)
            trainCandidates = await _candidates.GetByRunIdAsync(trainRunId, cancellationToken);
        if (experiment.ValidationStrategyLabRunId is long valRunId)
            valCandidates = await _candidates.GetByRunIdAsync(valRunId, cancellationToken);
        var reconciliation = _reconciliation.Reconcile(
            experiment, fullCandidates, trainCandidates, valCandidates);
        experiment.CandidateReconciliationJson =
            ValidationCandidateReconciliationService.Serialize(reconciliation);
        experiment.CandidateReconciliationStatus = reconciliation.ReconciliationStatus;

        var persistedRules = ValidationVerdictService.DeserializeRules(experiment.QualificationRuleResultsJson);
        if (persistedRules is null || persistedRules.Count == 0)
        {
            return ServiceResult<ValidationExperimentDetailDto>.Fail(
                "No structured qualification rule results are persisted for recalculation.");
        }

        var persisted = persistedRules.ToList();
        // Refresh data-integrity rule from latest reconciliation before recalculating.
        for (var i = 0; i < persisted.Count; i++)
        {
            if (persisted[i].RuleKey is "CandidateReconciliation")
            {
                var ok = reconciliation.ReconciliationStatus is
                    CandidateReconciliationStatus.ExactMatch
                    or CandidateReconciliationStatus.ExactMatchWithBoundaryCensoring
                    or CandidateReconciliationStatus.ExplainedSessionBoundaryDifference;
                var prev = persisted[i];
                persisted[i] = new QualificationRuleResult
                {
                    RuleKey = prev.RuleKey,
                    RuleName = prev.RuleName,
                    Segment = prev.Segment,
                    Layer = prev.Layer,
                    MetricKey = prev.MetricKey,
                    ActualValue = reconciliation.ReconciliationStatus.ToString(),
                    LimitValue = prev.LimitValue,
                    Unit = prev.Unit,
                    ComparisonOperator = prev.ComparisonOperator,
                    Status = ok ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
                    Applicability = prev.Applicability,
                    Reason = ok
                        ? $"Candidate reconciliation status {reconciliation.ReconciliationStatus} is acceptable."
                        : $"Candidate reconciliation status {reconciliation.ReconciliationStatus} blocks robustness approval.",
                    MetricVersion = prev.MetricVersion
                };
            }
        }

        var verdict = _verdictService.Recalculate(persisted);
        if (experiment.StrategyRobustnessDecision is { } stored
            && stored != verdict.Decision)
        {
            AppendDiagnostic(experiment, "VerdictRuleMismatch",
                $"Stored verdict {stored} is not reproducible from persisted rules (recalculated {verdict.Decision}).");
        }

        experiment.StrategyRobustnessDecision = verdict.Decision;
        experiment.PrimaryFailureReason = verdict.PrimaryFailureReason;
        experiment.FailureReasonsJson = JsonSerializer.Serialize(verdict.FailureReasons, JsonOptions);
        experiment.QualificationRuleResultsJson =
            ValidationVerdictService.SerializeRules(verdict.StructuredRuleResults);
        experiment.DecisionExplanation = verdict.Explanation;
        experiment.DecidedAtUtc = DateTime.UtcNow;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        var segments = await _segments.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        var redact = !ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus);
        return ServiceResult<ValidationExperimentDetailDto>.Ok(MapDetail(experiment, segments, redact));
    }

    public async Task<ServiceResult<ValidationSelectionIntegrityReportDto>> GetSelectionIntegrityAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationSelectionIntegrityReportDto>.Fail("Validation experiment was not found.");
        }

        var trials = await _trials.GetByExperimentIdAsync(id, cancellationToken);
        var report = _selectionIntegrity.Evaluate(experiment, trials);
        return ServiceResult<ValidationSelectionIntegrityReportDto>.Ok(MapSelectionIntegrity(report));
    }

    public async Task<ServiceResult<ValidationMetricBasisAuditReportDto>> GetMetricBasisAuditAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationMetricBasisAuditReportDto>.Fail("Validation experiment was not found.");
        }

        var segments = await _segments.GetByExperimentIdAsync(id, cancellationToken);
        var segmentDtos = new List<ValidationMetricBasisAuditSegmentDto>();
        foreach (var segment in segments)
        {
            if (segment.StrategyLabRunId is not long runId)
            {
                continue;
            }

            var candidates = await _candidates.GetByRunIdAsync(runId, cancellationToken);
            segmentDtos.Add(await BuildMetricBasisSegmentAuditAsync(
                experiment, segment.SegmentType, segment.LayerType, candidates, cancellationToken));
        }

        return ServiceResult<ValidationMetricBasisAuditReportDto>.Ok(new ValidationMetricBasisAuditReportDto
        {
            ExperimentId = experiment.Id,
            ValidationMetricsVersion = experiment.ValidationMetricsVersion,
            RiskBasisVersion = experiment.RiskBasisVersion,
            Segments = segmentDtos
        });
    }

    public async Task<ServiceResult<RecalculateValidationMetricsResultDto>> RecalculateMetricsAsync(
        long id,
        RecalculateValidationMetricsRequest request,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<RecalculateValidationMetricsResultDto>.Fail("Validation experiment was not found.");
        }

        if (experiment.Status != ValidationExperimentStatus.Completed
            && experiment.Status != ValidationExperimentStatus.ConfigurationFrozen
            && experiment.Status != ValidationExperimentStatus.Failed)
        {
            return ServiceResult<RecalculateValidationMetricsResultDto>.Fail(
                "Recalculation is only available for completed or frozen experiments.");
        }

        if (!string.Equals(request.TargetMetricsVersion, ValidationMetricsContract.VersionV13,
                StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<RecalculateValidationMetricsResultDto>.Fail(
                $"Unsupported target metrics version: {request.TargetMetricsVersion}.");
        }

        var segments = await _segments.GetByExperimentIdAsync(id, cancellationToken);
        var recalculated = new List<ValidationMetricBasisAuditSegmentDto>();
        foreach (var segment in segments)
        {
            if (segment.StrategyLabRunId is not long runId)
            {
                continue;
            }

            var candidates = await _candidates.GetByRunIdAsync(runId, cancellationToken);
            recalculated.Add(await BuildMetricBasisSegmentAuditAsync(
                experiment, segment.SegmentType, segment.LayerType, candidates, cancellationToken));
        }

        var result = new RecalculateValidationMetricsResultDto
        {
            ExperimentId = experiment.Id,
            TargetMetricsVersion = request.TargetMetricsVersion,
            TargetRiskBasisVersion = request.TargetRiskBasisVersion,
            RecalculatedAtUtc = DateTime.UtcNow,
            PreserveOriginal = request.PreserveOriginal,
            Reason = request.Reason,
            RecalculatedSegments = recalculated
        };

        AppendDiagnostic(experiment, "RecalculatedMetrics",
            JsonSerializer.Serialize(result, JsonOptions));
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        return ServiceResult<RecalculateValidationMetricsResultDto>.Ok(result);
    }

    private async Task<ValidationMetricBasisAuditSegmentDto> BuildMetricBasisSegmentAuditAsync(
        ValidationExperiment experiment,
        ValidationSegmentType segmentType,
        ValidationLayerType layerType,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var metrics = ValidationMetricsContract.FromCandidatesV13(
            candidates,
            experiment.TrainingCandleCount,
            0,
            layerType,
            _riskBasis);

        var audit = _riskBasis.AuditSegment(candidates, layerType);
        return new ValidationMetricBasisAuditSegmentDto
        {
            SegmentType = segmentType,
            LayerType = layerType,
            IncludedTradeCount = audit.IncludedTradeCount,
            ExcludedTradeCount = audit.ExcludedTradeCount,
            NetExpectancyApplicability = audit.NetExpectancyApplicability,
            ExclusionReasons = audit.ExclusionReasons,
            Diagnostics = audit.Diagnostics,
            GrossExpectancyR = metrics.GrossExpectancyR,
            NetExpectancyR = metrics.NetExpectancyR,
            MetricsVersion = ValidationMetricsContract.VersionV13,
            RiskBasisVersion = ValidationRiskBasisService.Version
        };
    }

    private static ValidationSelectionIntegrityReportDto MapSelectionIntegrity(SelectionIntegrityReport report) =>
        new()
        {
            ExperimentId = report.ExperimentId,
            Status = report.Status,
            SelectedTrialId = report.SelectedTrialId,
            SelectedTrialNumber = report.SelectedTrialNumber,
            SelectedParameterFingerprint = report.SelectedParameterFingerprint,
            FrozenParameterFingerprint = report.FrozenParameterFingerprint,
            SnapshotValidationStatus = report.SnapshotValidationStatus,
            FingerprintsMatch = report.FingerprintsMatch,
            IsEligibleForSelection = report.IsEligibleForSelection,
            Violations = report.Violations,
            Population = report.Population
        };

    public async Task<ServiceResult<ValidationExperimentDto>> CloneAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var source = await _experiments.GetByIdAsync(id, cancellationToken);
        if (source is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.");
        }

        var clone = new ValidationExperiment
        {
            Name = $"{source.Name} (Clone)",
            Description = source.Description,
            ExperimentType = source.ExperimentType,
            Status = ValidationExperimentStatus.Draft,
            StrategyCode = source.StrategyCode,
            StrategyVersion = source.StrategyVersion,
            SourceStrategyLabRunId = source.SourceStrategyLabRunId,
            ExchangeId = source.ExchangeId,
            Exchange = source.Exchange,
            SymbolId = source.SymbolId,
            Symbol = source.Symbol,
            Timeframe = source.Timeframe,
            RequestedStartUtc = source.RequestedStartUtc,
            RequestedEndUtc = source.RequestedEndUtc,
            SplitRatio = source.SplitRatio,
            SplitAlgorithmVersion = source.SplitAlgorithmVersion,
            RequiredWarmupCandles = source.RequiredWarmupCandles,
            WarmupAlgorithmVersion = source.WarmupAlgorithmVersion,
            DraftConfigurationJson = source.DraftConfigurationJson,
            QualificationProfileSnapshotJson = source.QualificationProfileSnapshotJson,
            OptimizationObjectiveSnapshotJson = source.OptimizationObjectiveSnapshotJson,
            ParameterSearchSpaceSnapshotJson = source.ParameterSearchSpaceSnapshotJson,
            PrimaryQualificationLayer = source.PrimaryQualificationLayer,
            ValidationRevealStatus = ValidationRevealStatus.Hidden,
            CandleDataSnapshotJson = "{}",
            CandleDataFingerprint = string.Empty,
            WarmupSnapshotJson = "{}",
            DiagnosticsJson = "[]",
            OverlayResultsJson = "{}",
            ComparisonJson = "{}",
            RegimeComparisonJson = "{}",
            ParameterStabilityJson = "{}",
            InitialBalance = source.InitialBalance,
            MaximumTrials = source.MaximumTrials,
            DeterministicSeed = source.DeterministicSeed,
            PercentComplete = 0m,
            CreatedAtUtc = DateTime.UtcNow,
            CurrentStage = "Draft",
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12,
            HoldoutExclusivityPolicyVersion = ValidationHoldoutExclusivityVersions.Current,
            SegmentDetectorContinuityMode = source.SegmentDetectorContinuityMode,
            ExpectancyMetric = source.ExpectancyMetric,
            ProfitFactorMetric = source.ProfitFactorMetric,
            ParameterStabilityApplicability =
                source.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration
                    ? ParameterStabilityApplicability.NotApplicable
                    : ParameterStabilityApplicability.Applicable
        };

        await _experiments.AddAsync(clone, cancellationToken);
        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(clone));
    }

    public async Task<ServiceResult<ValidationExperimentDto>> RerunExactlyAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var source = await _experiments.GetByIdAsync(id, cancellationToken);
        if (source is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.");
        }

        if (string.IsNullOrWhiteSpace(source.FrozenParameterFingerprint)
            || string.IsNullOrWhiteSpace(source.FrozenStrategyParameterSnapshotJson))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                "Rerun Exactly requires a frozen configuration with fingerprints.");
        }

        // Verify candle fingerprint still matches current data.
        if (TimeframeParser.TryParse(source.Timeframe, out var timeframe)
            && !string.IsNullOrWhiteSpace(source.CandleDataFingerprint))
        {
            var candles = await _candles.GetCandlesChronologicalAsync(
                source.SymbolId,
                timeframe,
                source.RequestedStartUtc,
                source.RequestedEndUtc,
                0,
                cancellationToken);
            var currentFp = ValidationCandleFingerprint.Build(candles);
            if (!string.Equals(currentFp, source.CandleDataFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<ValidationExperimentDto>.Fail(
                    "ValidationDataSnapshotMismatch: candle fingerprint no longer matches frozen experiment data.");
            }
        }

        var clone = new ValidationExperiment
        {
            Name = $"{source.Name} (Rerun Exact)",
            Description = source.Description,
            ExperimentType = source.ExperimentType,
            Status = ValidationExperimentStatus.ConfigurationFrozen,
            StrategyCode = source.StrategyCode,
            StrategyVersion = source.StrategyVersion,
            SourceStrategyLabRunId = source.SourceStrategyLabRunId,
            ExchangeId = source.ExchangeId,
            Exchange = source.Exchange,
            SymbolId = source.SymbolId,
            Symbol = source.Symbol,
            Timeframe = source.Timeframe,
            RequestedStartUtc = source.RequestedStartUtc,
            RequestedEndUtc = source.RequestedEndUtc,
            SplitRatio = source.SplitRatio,
            SplitAlgorithmVersion = source.SplitAlgorithmVersion,
            TotalEligibleCandleCount = source.TotalEligibleCandleCount,
            TrainingCandleCount = source.TrainingCandleCount,
            ValidationCandleCount = source.ValidationCandleCount,
            TrainingStartUtc = source.TrainingStartUtc,
            TrainingEndUtc = source.TrainingEndUtc,
            ValidationStartUtc = source.ValidationStartUtc,
            ValidationEndUtc = source.ValidationEndUtc,
            SplitCandleOpenTimeUtc = source.SplitCandleOpenTimeUtc,
            RequiredWarmupCandles = source.RequiredWarmupCandles,
            TrainingWarmupStartUtc = source.TrainingWarmupStartUtc,
            ValidationWarmupStartUtc = source.ValidationWarmupStartUtc,
            WarmupAlgorithmVersion = source.WarmupAlgorithmVersion,
            CandleDataSnapshotJson = source.CandleDataSnapshotJson,
            CandleDataFingerprint = source.CandleDataFingerprint,
            WarmupSnapshotJson = source.WarmupSnapshotJson,
            ParameterSearchSpaceSnapshotJson = source.ParameterSearchSpaceSnapshotJson,
            OptimizationObjectiveSnapshotJson = source.OptimizationObjectiveSnapshotJson,
            FrozenStrategyParameterSnapshotJson = source.FrozenStrategyParameterSnapshotJson,
            FrozenParameterFingerprint = source.FrozenParameterFingerprint,
            FrozenStrategyFingerprint = source.FrozenStrategyFingerprint,
            FrozenConfidenceSnapshotJson = source.FrozenConfidenceSnapshotJson,
            FrozenRiskSnapshotJson = source.FrozenRiskSnapshotJson,
            FrozenCostModelSnapshotJson = source.FrozenCostModelSnapshotJson,
            QualificationProfileSnapshotJson = source.QualificationProfileSnapshotJson,
            DraftConfigurationJson = source.DraftConfigurationJson,
            PrimaryQualificationLayer = source.PrimaryQualificationLayer,
            ValidationRevealStatus = ValidationRevealStatus.Frozen,
            FrozenAtUtc = source.FrozenAtUtc ?? DateTime.UtcNow,
            DiagnosticsJson = "[]",
            OverlayResultsJson = "{}",
            ComparisonJson = "{}",
            RegimeComparisonJson = "{}",
            ParameterStabilityJson = source.ParameterStabilityJson,
            InitialBalance = source.InitialBalance,
            MaximumTrials = source.MaximumTrials,
            DeterministicSeed = source.DeterministicSeed,
            PercentComplete = 80m,
            CreatedAtUtc = DateTime.UtcNow,
            CurrentStage = "ConfigurationFrozen",
            ValidationMetricsVersion = ValidationMetricsContract.VersionV12,
            HoldoutExclusivityPolicyVersion = ValidationHoldoutExclusivityVersions.Current,
            SegmentDetectorContinuityMode = source.SegmentDetectorContinuityMode,
            ExpectancyMetric = source.ExpectancyMetric,
            ProfitFactorMetric = source.ProfitFactorMetric
        };

        await _experiments.AddAsync(clone, cancellationToken);
        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(clone));
    }

    // ---- helpers ----

    public static string ParameterFingerprint(IReadOnlyDictionary<string, string> parameters) =>
        new ValidationParameterFingerprintService().ComputeFingerprint(parameters);

    private static void AppendDiagnostic(ValidationExperiment experiment, string code, string message)
    {
        var list = new List<object>();
        try
        {
            var existing = JsonSerializer.Deserialize<List<JsonElement>>(
                string.IsNullOrWhiteSpace(experiment.DiagnosticsJson) ? "[]" : experiment.DiagnosticsJson);
            if (existing is not null)
            {
                foreach (var el in existing)
                {
                    list.Add(JsonSerializer.Deserialize<object>(el.GetRawText())!);
                }
            }
        }
        catch
        {
            // start fresh
        }

        list.Add(new
        {
            code,
            message,
            atUtc = DateTime.UtcNow.ToString("O")
        });
        experiment.DiagnosticsJson = JsonSerializer.Serialize(list, JsonOptions);
    }

    private static ValidationExperimentDto MapDto(ValidationExperiment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        ExperimentType = e.ExperimentType,
        Status = e.Status,
        StrategyCode = e.StrategyCode,
        StrategyVersion = e.StrategyVersion,
        SourceStrategyLabRunId = e.SourceStrategyLabRunId,
        ExchangeId = e.ExchangeId,
        Exchange = e.Exchange,
        SymbolId = e.SymbolId,
        Symbol = e.Symbol,
        Timeframe = e.Timeframe,
        RequestedStartUtc = e.RequestedStartUtc,
        RequestedEndUtc = e.RequestedEndUtc,
        SplitRatio = e.SplitRatio,
        SplitAlgorithmVersion = e.SplitAlgorithmVersion,
        TotalEligibleCandleCount = e.TotalEligibleCandleCount,
        TrainingCandleCount = e.TrainingCandleCount,
        ValidationCandleCount = e.ValidationCandleCount,
        TrainingStartUtc = e.TrainingStartUtc,
        TrainingEndUtc = e.TrainingEndUtc,
        ValidationStartUtc = e.ValidationStartUtc,
        ValidationEndUtc = e.ValidationEndUtc,
        SplitCandleOpenTimeUtc = e.SplitCandleOpenTimeUtc,
        RequiredWarmupCandles = e.RequiredWarmupCandles,
        TrainingWarmupStartUtc = e.TrainingWarmupStartUtc,
        ValidationWarmupStartUtc = e.ValidationWarmupStartUtc,
        CandleDataFingerprint = e.CandleDataFingerprint,
        ValidationRevealStatus = e.ValidationRevealStatus,
        PrimaryQualificationLayer = e.PrimaryQualificationLayer,
        PrimaryLayerWarning = e.PrimaryQualificationLayer == ValidationPrimaryQualificationLayer.RawStrategy
            ? null
            : "Validation of a gated layer does not replace proof of raw strategy edge.",
        FrozenAtUtc = e.FrozenAtUtc,
        ValidationRevealedAtUtc = e.ValidationRevealedAtUtc,
        StrategyRobustnessDecision = e.StrategyRobustnessDecision,
        PrimaryFailureReason = e.PrimaryFailureReason,
        DecisionExplanation = e.DecisionExplanation,
        BoundaryCensoredCount = e.BoundaryCensoredCount,
        InitialBalance = e.InitialBalance,
        MaximumTrials = e.MaximumTrials,
        DeterministicSeed = e.DeterministicSeed,
        ErrorMessage = e.ErrorMessage,
        CurrentStage = e.CurrentStage,
        PercentComplete = e.PercentComplete,
        CreatedAtUtc = e.CreatedAtUtc,
        UpdatedAtUtc = e.UpdatedAtUtc,
        TrainingStrategyLabRunId = e.TrainingStrategyLabRunId,
        ValidationStrategyLabRunId = e.ValidationStrategyLabRunId,
        FrozenParameterFingerprint = e.FrozenParameterFingerprint,
        ValidationMetricsVersion = e.ValidationMetricsVersion,
        CandidateReconciliationStatus = e.CandidateReconciliationStatus,
        LeakageAuditStatus = e.LeakageAuditStatus,
        ParameterStabilityApplicability = e.ParameterStabilityApplicability,
        SegmentDetectorContinuityMode = e.SegmentDetectorContinuityMode,
        ExpectancyMetric = e.ExpectancyMetric,
        ProfitFactorMetric = e.ProfitFactorMetric,
        HoldoutExclusivityPolicyVersion = e.HoldoutExclusivityPolicyVersion,
        CrossSegmentOverlapCount = e.CrossSegmentOverlapCount,
        MetricConsistencyStatus = e.MetricConsistencyStatus,
        ExportVerificationStatus = e.ExportVerificationStatus,
        ValidationLaboratoryReadinessStatus = e.ValidationLaboratoryReadinessStatus,
        IsCanonical = e.IsCanonical,
        SupersessionStatus = e.SupersessionStatus,
        SupersededByExperimentId = e.SupersededByExperimentId,
        SupersededAtUtc = e.SupersededAtUtc,
        SupersessionReason = e.SupersessionReason,
        SelectionIntegrityStatus = e.SelectionIntegrityStatus,
        SelectedTrialId = e.SelectedTrialId,
        SelectedTrialNumber = e.SelectedTrialNumber,
        SelectedTrialParameterFingerprint = e.SelectedTrialParameterFingerprint,
        FrozenSnapshotValidationStatus = e.FrozenSnapshotValidationStatus,
        SelectionIntegrityVersion = e.SelectionIntegrityVersion,
        RiskBasisVersion = e.RiskBasisVersion,
        ParameterFingerprintVersion = e.ParameterFingerprintVersion,
        FreezeSource = e.FreezeSource,
        IsQualificationCapable = e.IsQualificationCapable,
        TrialPopulationSummaryJson = e.TrialPopulationSummaryJson,
        CloseoutAuditJson = e.CloseoutAuditJson
    };

    private static ValidationExperimentDetailDto MapDetail(
        ValidationExperiment e,
        IReadOnlyList<ValidationSegmentResult> segments,
        bool redactValidation)
    {
        var baseDto = MapDto(e);
        var segmentDtos = segments
            .Where(s => !redactValidation || s.SegmentType != ValidationSegmentType.Validation)
            .Select(MapSegment)
            .ToList();

        return new ValidationExperimentDetailDto
        {
            Id = baseDto.Id,
            Name = baseDto.Name,
            Description = baseDto.Description,
            ExperimentType = baseDto.ExperimentType,
            Status = baseDto.Status,
            StrategyCode = baseDto.StrategyCode,
            StrategyVersion = baseDto.StrategyVersion,
            SourceStrategyLabRunId = baseDto.SourceStrategyLabRunId,
            ExchangeId = baseDto.ExchangeId,
            Exchange = baseDto.Exchange,
            SymbolId = baseDto.SymbolId,
            Symbol = baseDto.Symbol,
            Timeframe = baseDto.Timeframe,
            RequestedStartUtc = baseDto.RequestedStartUtc,
            RequestedEndUtc = baseDto.RequestedEndUtc,
            SplitRatio = baseDto.SplitRatio,
            SplitAlgorithmVersion = baseDto.SplitAlgorithmVersion,
            TotalEligibleCandleCount = baseDto.TotalEligibleCandleCount,
            TrainingCandleCount = baseDto.TrainingCandleCount,
            ValidationCandleCount = baseDto.ValidationCandleCount,
            TrainingStartUtc = baseDto.TrainingStartUtc,
            TrainingEndUtc = baseDto.TrainingEndUtc,
            ValidationStartUtc = baseDto.ValidationStartUtc,
            ValidationEndUtc = baseDto.ValidationEndUtc,
            SplitCandleOpenTimeUtc = baseDto.SplitCandleOpenTimeUtc,
            RequiredWarmupCandles = baseDto.RequiredWarmupCandles,
            TrainingWarmupStartUtc = baseDto.TrainingWarmupStartUtc,
            ValidationWarmupStartUtc = baseDto.ValidationWarmupStartUtc,
            CandleDataFingerprint = baseDto.CandleDataFingerprint,
            ValidationRevealStatus = baseDto.ValidationRevealStatus,
            PrimaryQualificationLayer = baseDto.PrimaryQualificationLayer,
            PrimaryLayerWarning = baseDto.PrimaryLayerWarning,
            FrozenAtUtc = baseDto.FrozenAtUtc,
            ValidationRevealedAtUtc = baseDto.ValidationRevealedAtUtc,
            StrategyRobustnessDecision = redactValidation ? null : baseDto.StrategyRobustnessDecision,
            PrimaryFailureReason = redactValidation ? null : baseDto.PrimaryFailureReason,
            DecisionExplanation = redactValidation ? null : baseDto.DecisionExplanation,
            BoundaryCensoredCount = baseDto.BoundaryCensoredCount,
            InitialBalance = baseDto.InitialBalance,
            MaximumTrials = baseDto.MaximumTrials,
            DeterministicSeed = baseDto.DeterministicSeed,
            ErrorMessage = baseDto.ErrorMessage,
            CurrentStage = baseDto.CurrentStage,
            PercentComplete = baseDto.PercentComplete,
            CreatedAtUtc = baseDto.CreatedAtUtc,
            UpdatedAtUtc = baseDto.UpdatedAtUtc,
            TrainingStrategyLabRunId = baseDto.TrainingStrategyLabRunId,
            ValidationStrategyLabRunId = redactValidation ? null : baseDto.ValidationStrategyLabRunId,
            FrozenParameterFingerprint = baseDto.FrozenParameterFingerprint,
            ValidationMetricsVersion = baseDto.ValidationMetricsVersion,
            CandidateReconciliationStatus = baseDto.CandidateReconciliationStatus,
            LeakageAuditStatus = redactValidation ? null : baseDto.LeakageAuditStatus,
            ParameterStabilityApplicability = baseDto.ParameterStabilityApplicability,
            SegmentDetectorContinuityMode = baseDto.SegmentDetectorContinuityMode,
            ExpectancyMetric = baseDto.ExpectancyMetric,
            ProfitFactorMetric = baseDto.ProfitFactorMetric,
            HoldoutExclusivityPolicyVersion = baseDto.HoldoutExclusivityPolicyVersion,
            CrossSegmentOverlapCount = baseDto.CrossSegmentOverlapCount,
            MetricConsistencyStatus = redactValidation ? null : baseDto.MetricConsistencyStatus,
            ExportVerificationStatus = baseDto.ExportVerificationStatus,
            ValidationLaboratoryReadinessStatus = baseDto.ValidationLaboratoryReadinessStatus,
            IsCanonical = baseDto.IsCanonical,
            SupersessionStatus = baseDto.SupersessionStatus,
            SupersededByExperimentId = baseDto.SupersededByExperimentId,
            SupersededAtUtc = baseDto.SupersededAtUtc,
            SupersessionReason = baseDto.SupersessionReason,
            SelectionIntegrityStatus = baseDto.SelectionIntegrityStatus,
            SelectedTrialId = baseDto.SelectedTrialId,
            SelectedTrialNumber = baseDto.SelectedTrialNumber,
            SelectedTrialParameterFingerprint = baseDto.SelectedTrialParameterFingerprint,
            FrozenSnapshotValidationStatus = baseDto.FrozenSnapshotValidationStatus,
            SelectionIntegrityVersion = baseDto.SelectionIntegrityVersion,
            RiskBasisVersion = baseDto.RiskBasisVersion,
            ParameterFingerprintVersion = baseDto.ParameterFingerprintVersion,
            FreezeSource = baseDto.FreezeSource,
            IsQualificationCapable = baseDto.IsQualificationCapable,
            TrialPopulationSummaryJson = baseDto.TrialPopulationSummaryJson,
            CloseoutAuditJson = baseDto.CloseoutAuditJson,
            CandleDataSnapshotJson = e.CandleDataSnapshotJson,
            WarmupSnapshotJson = e.WarmupSnapshotJson,
            ParameterSearchSpaceSnapshotJson = e.ParameterSearchSpaceSnapshotJson,
            OptimizationObjectiveSnapshotJson = e.OptimizationObjectiveSnapshotJson,
            FrozenStrategyParameterSnapshotJson = e.FrozenStrategyParameterSnapshotJson,
            FrozenStrategyFingerprint = e.FrozenStrategyFingerprint,
            FrozenConfidenceSnapshotJson = e.FrozenConfidenceSnapshotJson,
            FrozenRiskSnapshotJson = e.FrozenRiskSnapshotJson,
            FrozenCostModelSnapshotJson = e.FrozenCostModelSnapshotJson,
            QualificationProfileSnapshotJson = e.QualificationProfileSnapshotJson,
            FailureReasonsJson = redactValidation ? null : e.FailureReasonsJson,
            QualificationRuleResultsJson = redactValidation ? null : e.QualificationRuleResultsJson,
            DiagnosticsJson = e.DiagnosticsJson,
            OverlayResultsJson = redactValidation ? null : e.OverlayResultsJson,
            ComparisonJson = redactValidation ? null : e.ComparisonJson,
            RegimeComparisonJson = redactValidation ? null : e.RegimeComparisonJson,
            ParameterStabilityJson = e.ParameterStabilityJson,
            CandidateReconciliationJson = redactValidation ? null : e.CandidateReconciliationJson,
            LeakageAuditJson = redactValidation ? null : e.LeakageAuditJson,
            HoldoutExclusivityJson = redactValidation ? null : e.HoldoutExclusivityJson,
            MetricConsistencyJson = redactValidation ? null : e.MetricConsistencyJson,
            ExportVerificationJson = e.ExportVerificationJson,
            DraftConfigurationJson = e.DraftConfigurationJson,
            SegmentResults = segmentDtos
        };
    }

    private static ValidationSegmentResultDto MapSegment(ValidationSegmentResult s)
    {
        var metrics = DeserializeMetrics(s.MetricsJson);
        return new ValidationSegmentResultDto
        {
            Id = s.Id,
            SegmentType = s.SegmentType,
            LayerType = s.LayerType,
            StrategyLabRunId = s.StrategyLabRunId,
            MetricsJson = s.MetricsJson,
            CandleCount = s.CandleCount,
            CandidateCount = s.CandidateCount,
            ClosedTradeCount = s.ClosedTradeCount,
            NetExpectancyR = s.NetExpectancyR,
            ProfitFactor = s.ProfitFactor,
            NetPnl = s.NetPnl,
            NetReturnPercent = s.NetReturnPercent,
            MaximumDrawdownPercent = s.MaximumDrawdownPercent,
            TransactionCosts = s.TransactionCosts,
            BoundaryCensoredCount = s.BoundaryCensoredCount,
            ResultFingerprint = s.ResultFingerprint,
            ResultCalculationVersion = s.ResultCalculationVersion,
            GrossExpectancyR = s.GrossExpectancyR,
            GrossProfitFactor = s.GrossProfitFactor,
            NetProfitFactor = s.NetProfitFactor,
            GrossAverageR = s.GrossAverageR,
            NetAverageR = s.NetAverageR,
            GrossPnl = s.GrossPnl,
            PersistedCandidateRowCount = s.PersistedCandidateRowCount,
            MetricIncludedCandidateCount = s.MetricIncludedCandidateCount,
            MetricExcludedCandidateCount = s.MetricExcludedCandidateCount,
            CrossSegmentOverlapCount = s.CrossSegmentOverlapCount,
            GrossProfit = s.GrossProfit,
            GrossLoss = s.GrossLoss,
            NetProfit = s.NetProfit,
            NetLoss = s.NetLoss,
            MetricWarningBearingIncludedTradeCount =
                metrics?.MetricWarningBearingIncludedTradeCount ?? 0,
            MetricWarningCodes = metrics?.MetricWarningCodes
        };
    }

    private static ValidationParameterTrialDto MapTrial(ValidationParameterTrial t) => new()
    {
        Id = t.Id,
        TrialNumber = t.TrialNumber,
        ParameterSnapshotJson = t.ParameterSnapshotJson,
        ParameterFingerprint = t.ParameterFingerprint,
        Status = t.Status,
        StartedAtUtc = t.StartedAtUtc,
        CompletedAtUtc = t.CompletedAtUtc,
        RawCandidateCount = t.RawCandidateCount,
        ClosedTradeCount = t.ClosedTradeCount,
        WinnerCount = t.WinnerCount,
        LoserCount = t.LoserCount,
        ExpiredCount = t.ExpiredCount,
        NetExpectancyR = t.NetExpectancyR,
        GrossPnl = t.GrossPnl,
        NetPnl = t.NetPnl,
        ProfitFactor = t.ProfitFactor,
        MaximumDrawdownPercent = t.MaximumDrawdownPercent,
        FeeImpactPercent = t.FeeImpactPercent,
        TrainingScore = t.TrainingScore,
        GuardrailDecision = t.GuardrailDecision,
        GuardrailFailureReasonsJson = t.GuardrailFailureReasonsJson,
        Rank = t.Rank,
        StrategyLabRunId = t.StrategyLabRunId,
        ErrorMessage = t.ErrorMessage,
        RecoverySource = t.RecoverySource
    };

    internal static DraftConfiguration ParseDraft(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return new DraftConfiguration();
        }

        return JsonSerializer.Deserialize<DraftConfiguration>(json, JsonOptions) ?? new DraftConfiguration();
    }

    private static string SerializeDraft(DraftConfiguration draft) =>
        JsonSerializer.Serialize(draft, JsonOptions);

    private static StrategyLabObservationSettingsDto NormalizeObservation(StrategyLabObservationSettingsDto? settings)
    {
        var s = settings ?? new StrategyLabObservationSettingsDto();
        if (string.IsNullOrWhiteSpace(s.ConfidenceModel))
        {
            s.ConfidenceModel = "StrategySetupQuality/v1";
        }

        return s;
    }

    private static Dictionary<string, string> DeserializeStringDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static StrategyLabObservationSettingsDto? ExtractObservationSettings(string featureFlagsJson)
    {
        if (string.IsNullOrWhiteSpace(featureFlagsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(featureFlagsJson);
            if (doc.RootElement.TryGetProperty("observationSettings", out var obs)
                || doc.RootElement.TryGetProperty("ObservationSettings", out obs))
            {
                return JsonSerializer.Deserialize<StrategyLabObservationSettingsDto>(obs.GetRawText(), JsonOptions);
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    internal static void TryParseFees(string? json, out decimal maker, out decimal taker)
    {
        maker = 0.0002m;
        taker = 0.0004m;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("makerFeeRate", out var m) || doc.RootElement.TryGetProperty("MakerFeeRate", out m))
                maker = m.GetDecimal();
            if (doc.RootElement.TryGetProperty("takerFeeRate", out var t) || doc.RootElement.TryGetProperty("TakerFeeRate", out t))
                taker = t.GetDecimal();
        }
        catch
        {
            // keep defaults
        }
    }

    internal static void TryParseSlippage(string? json, out decimal slippage)
    {
        slippage = 0m;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("slippagePercent", out var s)
                || doc.RootElement.TryGetProperty("SlippagePercent", out s))
            {
                slippage = s.GetDecimal();
            }
        }
        catch
        {
            // keep default
        }
    }

    private List<Dictionary<string, string>> BuildTrainingCombinations(
        ValidationExperiment experiment,
        DraftConfiguration draft)
    {
        if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            return [new Dictionary<string, string>(draft.Parameters, StringComparer.OrdinalIgnoreCase)];
        }

        var overrides = draft.ParameterSearchSpaceOverrides;
        var grid = _parameterDefinitions.GenerateGridCombinations(
            experiment.StrategyCode,
            experiment.MaximumTrials,
            overrides);
        var list = grid
            .Select(c => new Dictionary<string, string>(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (list.Count == 0)
        {
            list.Add(new Dictionary<string, string>(draft.Parameters, StringComparer.OrdinalIgnoreCase));
        }

        DeterministicShuffle(list, experiment.DeterministicSeed);
        if (list.Count > experiment.MaximumTrials)
        {
            list = list.Take(experiment.MaximumTrials).ToList();
        }

        return list;
    }

    private static void DeterministicShuffle<T>(IList<T> items, int seed)
    {
        var rng = new Random(seed);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }


    /// <summary>
    /// Candle repository filters OpenTimeUtc &lt; toUtc (exclusive). EndUtc values from the holdout split
    /// are inclusive last-candle open times, so add one timeframe bar for Strategy Lab loads.
    /// </summary>
    private static DateTime ToExclusiveUtc(DateTime inclusiveLastOpenUtc, string timeframe)
    {
        if (!TimeframeParser.TryGetDurationMinutes(timeframe, out var minutes) || minutes <= 0)
        {
            throw new InvalidOperationException($"Unable to resolve timeframe duration for '{timeframe}'.");
        }

        return inclusiveLastOpenUtc.AddMinutes(minutes);
    }
    private async Task<StrategyLabRun> CreateAndExecuteLabRunAsync(
        ValidationExperiment experiment,
        IReadOnlyDictionary<string, string> parameters,
        DraftConfiguration draft,
        DateTime fromUtc,
        DateTime toUtc,
        string name,
        CancellationToken cancellationToken)
    {
        var feeJson = JsonSerializer.Serialize(new
        {
            makerFeeRate = draft.MakerFeeRate,
            takerFeeRate = draft.TakerFeeRate
        }, JsonOptions);
        var slipJson = JsonSerializer.Serialize(new { slippagePercent = draft.SlippagePercent }, JsonOptions);
        var featureFlagsJson = JsonSerializer.Serialize(new { observationSettings = draft.ObservationSettings }, JsonOptions);
        var fingerprint = ExperimentFingerprintBuilder.Build(
            experiment.StrategyCode,
            experiment.StrategyVersion,
            experiment.ExchangeId,
            experiment.SymbolId,
            experiment.Symbol,
            experiment.Timeframe,
            fromUtc,
            toUtc,
            StrategyLabExecutionMode.FullPipelineComparison,
            parameters,
            featureFlagsJson,
            experiment.InitialBalance,
            feeJson,
            slipJson);

        var run = new StrategyLabRun
        {
            Name = name,
            StrategyCode = experiment.StrategyCode,
            StrategyVersion = experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId,
            SymbolId = experiment.SymbolId,
            Symbol = experiment.Symbol,
            Timeframe = experiment.Timeframe,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            ExecutionMode = StrategyLabExecutionMode.FullPipelineComparison,
            ParametersJson = JsonSerializer.Serialize(parameters, JsonOptions),
            StrategyFeatureFlagsJson = featureFlagsJson,
            InitialBalance = experiment.InitialBalance,
            FeeSettingsJson = feeJson,
            SlippageSettingsJson = slipJson,
            Status = StrategyLabRunStatus.Created,
            ExperimentFingerprint = fingerprint,
            AppVersion = "1.0.0",
            StrategyCodeFingerprint = fingerprint,
            RiskProfileId = draft.ObservationSettings?.RiskProfileId,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _labRuns.AddAsync(run, cancellationToken);
        await _labRunner.ExecuteAsync(run.Id, cancellationToken);
        var refreshed = await _labRuns.GetByIdAsync(run.Id, cancellationToken) ?? run;
        return refreshed;
    }

    internal static (StrategyLabPerformanceSummaryDto? Summary, ShadowPortfolioSummaryDto? RiskOnly, ShadowPortfolioSummaryDto? FullPipeline)
        ParseResultSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return (null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            StrategyLabPerformanceSummaryDto? summary = null;
            ShadowPortfolioSummaryDto? riskOnly = null;
            ShadowPortfolioSummaryDto? fullPipeline = null;

            if (TryGetJsonProperty(root, "summary", out var summaryEl) && summaryEl.ValueKind != JsonValueKind.Null)
            {
                summary = JsonSerializer.Deserialize<StrategyLabPerformanceSummaryDto>(summaryEl.GetRawText(), JsonOptions);
            }

            if (TryGetJsonProperty(root, "riskOnlyShadowPortfolio", out var ro) && ro.ValueKind != JsonValueKind.Null)
            {
                riskOnly = JsonSerializer.Deserialize<ShadowPortfolioSummaryDto>(ro.GetRawText(), JsonOptions);
            }

            if (TryGetJsonProperty(root, "fullPipelineShadowPortfolio", out var fp) && fp.ValueKind != JsonValueKind.Null)
            {
                fullPipeline = JsonSerializer.Deserialize<ShadowPortfolioSummaryDto>(fp.GetRawText(), JsonOptions);
            }

            return (summary, riskOnly, fullPipeline);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
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

    private static ValidationQualificationProfile ToQualificationProfile(
        ValidationQualificationProfileDto? dto,
        ValidationPrimaryQualificationLayer primary)
    {
        dto ??= new ValidationQualificationProfileDto();
        return new ValidationQualificationProfile
        {
            ProfileVersion = dto.ProfileVersion,
            PrimaryQualificationLayer = primary,
            MinimumTrainingClosedTrades = dto.MinimumTrainingClosedTrades,
            MinimumValidationClosedTrades = dto.MinimumValidationClosedTrades,
            MinimumTrainingProfitFactor = dto.MinimumTrainingProfitFactor,
            MinimumValidationProfitFactor = dto.MinimumValidationProfitFactor,
            MinimumTrainingNetExpectancyR = dto.MinimumTrainingNetExpectancyR,
            MinimumValidationNetExpectancyR = dto.MinimumValidationNetExpectancyR,
            MaximumTrainingDrawdownPercent = dto.MaximumTrainingDrawdownPercent,
            MaximumValidationDrawdownPercent = dto.MaximumValidationDrawdownPercent,
            MinimumOpportunityRetentionPercent = dto.MinimumOpportunityRetentionPercent,
            MaximumAllowedExpectancyDegradation = dto.MaximumAllowedExpectancyDegradation,
            MaximumSingleTradePnlContributionPercent = dto.MaximumSingleTradePnlContributionPercent,
            RequirePositiveValidationNetPnl = dto.RequirePositiveValidationNetPnl,
            RequirePositiveValidationNetExpectancy = dto.RequirePositiveValidationNetExpectancy,
            RequireParameterStability = dto.RequireParameterStability,
            ExpectancyMetric = dto.ExpectancyMetric ?? ExpectancyMetricType.NetExpectancyR,
            ProfitFactorMetric = dto.ProfitFactorMetric ?? ProfitFactorMetricType.NetProfitFactor,
            ExpiredTradeMetricPolicy = dto.ExpiredTradeMetricPolicy ?? ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry
        };
    }

    private static ValidationQualificationProfile ApplyExperimentMetricSelections(
        ValidationQualificationProfile profile,
        ValidationExperiment experiment) =>
        new()
        {
            ProfileVersion = profile.ProfileVersion,
            PrimaryQualificationLayer = profile.PrimaryQualificationLayer,
            MinimumTrainingClosedTrades = profile.MinimumTrainingClosedTrades,
            MinimumValidationClosedTrades = profile.MinimumValidationClosedTrades,
            MinimumTrainingProfitFactor = profile.MinimumTrainingProfitFactor,
            MinimumValidationProfitFactor = profile.MinimumValidationProfitFactor,
            MinimumTrainingNetExpectancyR = profile.MinimumTrainingNetExpectancyR,
            MinimumValidationNetExpectancyR = profile.MinimumValidationNetExpectancyR,
            MaximumTrainingDrawdownPercent = profile.MaximumTrainingDrawdownPercent,
            MaximumValidationDrawdownPercent = profile.MaximumValidationDrawdownPercent,
            MinimumOpportunityRetentionPercent = profile.MinimumOpportunityRetentionPercent,
            MaximumAllowedExpectancyDegradation = profile.MaximumAllowedExpectancyDegradation,
            MaximumSingleTradePnlContributionPercent = profile.MaximumSingleTradePnlContributionPercent,
            RequirePositiveValidationNetPnl = profile.RequirePositiveValidationNetPnl,
            RequirePositiveValidationNetExpectancy = profile.RequirePositiveValidationNetExpectancy,
            RequireParameterStability = profile.RequireParameterStability,
            ExpectancyMetric = experiment.ExpectancyMetric,
            ProfitFactorMetric = experiment.ProfitFactorMetric,
            ExpiredTradeMetricPolicy = profile.ExpiredTradeMetricPolicy
        };

    private static ValidationLayerType MapPrimaryToLayer(ValidationPrimaryQualificationLayer primary) =>
        primary switch
        {
            ValidationPrimaryQualificationLayer.ConfidenceQualified => ValidationLayerType.ConfidenceQualified,
            ValidationPrimaryQualificationLayer.RiskOnly => ValidationLayerType.RiskOnly,
            ValidationPrimaryQualificationLayer.FullPipeline => ValidationLayerType.FullPipeline,
            _ => ValidationLayerType.RawStrategy
        };

    private static LayerSegmentMetrics? DeserializeMetrics(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            return JsonSerializer.Deserialize<LayerSegmentMetrics>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static LayerSegmentMetrics ToMetricsFromSegment(ValidationSegmentResult s) => new()
    {
        CandleCount = s.CandleCount,
        CandidateCount = s.CandidateCount,
        ClosedTradeCount = s.ClosedTradeCount,
        NetExpectancyR = s.NetExpectancyR,
        GrossExpectancyR = s.GrossExpectancyR,
        ProfitFactor = s.NetProfitFactor ?? s.ProfitFactor,
        GrossProfitFactor = s.GrossProfitFactor,
        NetProfitFactor = s.NetProfitFactor ?? s.ProfitFactor,
        GrossAverageR = s.GrossAverageR,
        NetAverageR = s.NetAverageR,
        AverageR = s.GrossAverageR,
        GrossPnl = s.GrossPnl,
        NetPnl = s.NetPnl,
        NetReturnPercent = s.NetReturnPercent,
        MaximumRealizedDrawdownPercent = s.MaximumDrawdownPercent,
        TransactionCosts = s.TransactionCosts,
        BoundaryCensoredCount = s.BoundaryCensoredCount,
        OpportunityRatePer1000Candles = s.CandleCount > 0
            ? Math.Round(s.CandidateCount * 1000m / s.CandleCount, 4)
            : 0m,
        MetricsVersion = s.ResultCalculationVersion
    };

    private static long? PreferRevealedRunId(ValidationExperiment experiment)
    {
        if (ValidationLifecycleGate.IsValidationPerformanceRevealed(experiment.ValidationRevealStatus)
            && experiment.ValidationStrategyLabRunId is not null)
        {
            return experiment.ValidationStrategyLabRunId;
        }

        return experiment.TrainingStrategyLabRunId;
    }

    private static object? SafeParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(json, JsonOptions);
        }
        catch
        {
            return json;
        }
    }

    internal sealed class DraftConfiguration
    {
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public StrategyLabObservationSettingsDto? ObservationSettings { get; set; }
        public decimal MakerFeeRate { get; set; } = 0.0002m;
        public decimal TakerFeeRate { get; set; } = 0.0004m;
        public decimal SlippagePercent { get; set; }
        public ValidationQualificationProfileDto? QualificationProfile { get; set; }
        public Dictionary<string, string>? ParameterSearchSpaceOverrides { get; set; }
        public bool AutoImportMissingCandles { get; set; } = true;
    }
}
