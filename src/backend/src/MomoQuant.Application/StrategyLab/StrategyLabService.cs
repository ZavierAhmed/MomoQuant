using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.StrategyLab.Synthetic;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

public interface IStrategyLabService
{
    Task<ServiceResult<StrategyLabRunDto>> CreateRunAsync(CreateStrategyLabRunRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabRunDto>> GetRunAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabRunDetailDto>> GetRunDetailAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResultDto<StrategyResearchCandidateDto>>> GetCandidatesAsync(
        long runId,
        StrategyLabCandidateQuery query,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabCandidateDetailDto>> GetCandidateDetailAsync(
        long runId,
        long candidateId,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<PortfolioPathComparisonDto>> GetPortfolioPathComparisonAsync(
        long runId,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabGateAnalysisDto>> GetGateAnalysisAsync(long runId, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabRiskAnalysisDto>> GetRiskAnalysisAsync(long runId, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabRiskProfileComparisonDto>> CompareRiskProfilesAsync(
        long runId,
        long otherRunId,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<StrategyLabRunDto>>> GetRunsByStrategyAsync(string strategyCode, int limit = 20, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<StrategyLabRunDto>>> GetRecentRunsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<ServiceResult<CreateStrategyLabRunRequest>> GetRerunConfigAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SyntheticTestResultDto>>> RunSyntheticTestsAsync(string strategyCode, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyHealthDto>> GetStrategyHealthAsync(string strategyCode, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<StrategyLabStrategyDto>>> GetLabStrategiesAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyLabStartupHealthDto>> GetStartupHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class StrategyLabStrategyDto
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Category { get; init; }
    public required IReadOnlyList<string> AllowedTimeframes { get; init; }
    public string? PreferredTimeframe { get; init; }
}

public sealed class StrategyLabService : IStrategyLabService
{
    private static readonly HashSet<string> LabStrategyCodes =
    [
        StrategyCodes.PriceStructureBreakoutRetest,
        StrategyCodes.PriceStructureLiquiditySweepReclaim
    ];

    private readonly IStrategyLabRunRepository _runRepository;
    private readonly IStrategyResearchCandidateRepository _candidateRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyLabQueue _queue;
    private readonly SyntheticCandleScenarioRunner _syntheticRunner = new();

    public StrategyLabService(
        IStrategyLabRunRepository runRepository,
        IStrategyResearchCandidateRepository candidateRepository,
        IStrategyRepository strategyRepository,
        IStrategyRegistry strategyRegistry,
        ISymbolRepository symbolRepository,
        IStrategyLabQueue queue)
    {
        _runRepository = runRepository;
        _candidateRepository = candidateRepository;
        _strategyRepository = strategyRepository;
        _strategyRegistry = strategyRegistry;
        _symbolRepository = symbolRepository;
        _queue = queue;
    }

    public Task<ServiceResult<IReadOnlyList<StrategyLabStrategyDto>>> GetLabStrategiesAsync(CancellationToken cancellationToken = default)
    {
        var strategies = LabStrategyCodes.Select(code =>
        {
            var entity = _strategyRegistry.GetByCode(StrategyCodeExtensions.FromCode(code));
            return new StrategyLabStrategyDto
            {
                Code = code,
                Name = entity?.Name ?? code,
                Version = "1.0.0",
                Category = code.Contains("LIQUIDITY") ? "Price Action / Liquidity" : "Price Action / Market Structure",
                AllowedTimeframes = ["5m", "15m", "30m", "1h", "4h"],
                PreferredTimeframe = "15m"
            };
        }).ToList();

        return Task.FromResult(ServiceResult<IReadOnlyList<StrategyLabStrategyDto>>.Ok(strategies));
    }

    public async Task<ServiceResult<StrategyLabRunDto>> CreateRunAsync(CreateStrategyLabRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!LabStrategyCodes.Contains(request.StrategyCode))
        {
            return ServiceResult<StrategyLabRunDto>.Fail("Strategy is not enabled for Strategy Laboratory.");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<StrategyLabRunDto>.Fail("Symbol not found.");
        }

        var strategyEnum = StrategyCodeExtensions.FromCode(request.StrategyCode);
        var strategyEntity = await _strategyRepository.GetByCodeAsync(strategyEnum, cancellationToken);
        var version = strategyEntity?.Version ?? "1.0.0";
        var parameters = request.Parameters ?? new Dictionary<string, string>();
        var feeJson = JsonSerializer.Serialize(new { makerFeeRate = request.MakerFeeRate, takerFeeRate = request.TakerFeeRate });
        var slippageJson = JsonSerializer.Serialize(new { slippagePercent = request.SlippagePercent });
        var observationSettings = NormalizeObservationSettings(request);
        var featureFlagsJson = JsonSerializer.Serialize(new { observationSettings });

        var fingerprint = ExperimentFingerprintBuilder.Build(
            request.StrategyCode,
            version,
            request.ExchangeId,
            request.SymbolId,
            symbol.SymbolName,
            request.Timeframe,
            request.FromUtc,
            request.ToUtc,
            request.ExecutionMode,
            parameters,
            featureFlagsJson,
            request.InitialBalance,
            feeJson,
            slippageJson);

        var run = new StrategyLabRun
        {
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"{request.StrategyCode} {symbol.SymbolName} {request.Timeframe}"
                : request.Name!,
            StrategyCode = request.StrategyCode,
            StrategyVersion = version,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Symbol = symbol.SymbolName,
            Timeframe = request.Timeframe,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            ExecutionMode = request.ExecutionMode,
            ParametersJson = JsonSerializer.Serialize(parameters),
            StrategyFeatureFlagsJson = featureFlagsJson,
            InitialBalance = request.InitialBalance,
            FeeSettingsJson = feeJson,
            SlippageSettingsJson = slippageJson,
            Status = StrategyLabRunStatus.Created,
            ExperimentFingerprint = fingerprint,
            AppVersion = "1.0.0",
            StrategyCodeFingerprint = fingerprint,
            RiskProfileId = observationSettings.RiskProfileId ?? request.RiskProfileId,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _runRepository.AddAsync(run, cancellationToken);
        _queue.Enqueue(run.Id);
        return ServiceResult<StrategyLabRunDto>.Ok(MapRun(run, strategyEntity?.Version));
    }

    public async Task<ServiceResult<StrategyLabRunDto>> GetRunAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyLabRunDto>.Fail("Strategy lab run not found.");
        }

        var currentVersion = await GetCurrentVersionAsync(run.StrategyCode, cancellationToken);
        return ServiceResult<StrategyLabRunDto>.Ok(MapRun(run, currentVersion));
    }

    public async Task<ServiceResult<StrategyLabRunDetailDto>> GetRunDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyLabRunDetailDto>.Fail("Strategy lab run not found.");
        }

        var candidates = await _candidateRepository.GetByRunIdAsync(id, cancellationToken);
        var currentVersion = await GetCurrentVersionAsync(run.StrategyCode, cancellationToken);

        StrategyLabPerformanceSummaryDto? summary = null;
        CandidateFunnelDto? funnel = null;
        RawVsGatedComparisonDto? gated = null;
        IReadOnlyList<string> warnings = [];
        CoverageDiagnosticsDto? coverageDiagnostics = null;
        ZeroCandidateExplanationDto? zeroExplanation = null;
        IReadOnlyList<DiagnosticEventDto> diagnosticEvents = [];
        IReadOnlyList<string> sampleFingerprints = [];

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsedFromSummary = false;

        if (!string.IsNullOrWhiteSpace(run.ResultSummaryJson) && run.ResultSummaryJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(run.ResultSummaryJson);
                var root = doc.RootElement;

                // In-progress/failed runs may only have coverageDiagnostics written yet.
                if (TryGetJsonProperty(root, "summary", out var summaryEl)
                    && TryGetJsonProperty(root, "funnel", out var funnelEl))
                {
                    var parsedSummary = JsonSerializer.Deserialize<StrategyLabPerformanceSummaryDto>(summaryEl.GetRawText(), jsonOptions);
                    var parsedFunnel = JsonSerializer.Deserialize<CandidateFunnelDto>(funnelEl.GetRawText(), jsonOptions);
                    if (parsedSummary is not null && parsedFunnel is not null)
                    {
                        summary = parsedSummary;
                        funnel = parsedFunnel;
                        gated = TryGetJsonProperty(root, "gatedComparison", out var gatedEl) && gatedEl.ValueKind != JsonValueKind.Null
                            ? JsonSerializer.Deserialize<RawVsGatedComparisonDto>(gatedEl.GetRawText(), jsonOptions)
                            : null;
                        warnings = TryGetJsonProperty(root, "warnings", out var warnEl)
                            ? JsonSerializer.Deserialize<List<string>>(warnEl.GetRawText(), jsonOptions) ?? []
                            : [];
                        zeroExplanation = TryGetJsonProperty(root, "zeroCandidateExplanation", out var zeroEl) && zeroEl.ValueKind != JsonValueKind.Null
                            ? JsonSerializer.Deserialize<ZeroCandidateExplanationDto>(zeroEl.GetRawText(), jsonOptions)
                            : null;
                        diagnosticEvents = TryGetJsonProperty(root, "diagnosticEvents", out var diagEl) && diagEl.ValueKind != JsonValueKind.Null
                            ? JsonSerializer.Deserialize<List<DiagnosticEventDto>>(diagEl.GetRawText(), jsonOptions) ?? []
                            : [];
                        sampleFingerprints = TryGetJsonProperty(root, "sampleFingerprints", out var fpEl) && fpEl.ValueKind != JsonValueKind.Null
                            ? JsonSerializer.Deserialize<List<string>>(fpEl.GetRawText(), jsonOptions) ?? []
                            : [];
                        parsedFromSummary = true;
                    }
                }

                if (TryGetJsonProperty(root, "coverageDiagnostics", out var covEl) && covEl.ValueKind != JsonValueKind.Null)
                {
                    coverageDiagnostics = JsonSerializer.Deserialize<CoverageDiagnosticsDto>(covEl.GetRawText(), jsonOptions);
                }
            }
            catch (JsonException)
            {
                parsedFromSummary = false;
            }
        }

        if (!parsedFromSummary || summary is null || funnel is null)
        {
            var opportunity = StrategyOpportunityMetricsCalculator.Calculate(run.EvaluationsCount, candidates, run.EvaluationsCount, run.FromUtc, run.ToUtc);
            var closed = candidates.Count(c => c.CandidateStatus == StrategyResearchCandidateStatus.Closed);
            var evidence = EvidenceQualityCalculator.Calculate(closed);
            summary = StrategyLabPerformanceCalculator.BuildSummary(candidates, opportunity, evidence, run.InitialBalance);
            funnel = new CandidateFunnelDto();
            if (!parsedFromSummary)
            {
                gated = null;
                warnings = run.ErrorMessage is null ? [] : [run.ErrorMessage];
            }
        }

        // Candidates are loaded via paginated GET /runs/{id}/candidates — detail keeps a small preview only.
        var candidatePreview = candidates.Take(25).Select(MapCandidate).ToList();

        ShadowPortfolioSummaryDto? riskOnlyShadow = null;
        ShadowPortfolioSummaryDto? fullPipelineShadow = null;
        ScoreDistributionDiagnosticsDto? portfolioScoreDiag = null;
        PortfolioPathDivergenceDto? pathDivergence = null;
        IReadOnlyList<string> pathDiagnostics = [];
        string? riskPathVersion = null;
        string? drawdownMode = null;
        if (!string.IsNullOrWhiteSpace(run.ResultSummaryJson) && run.ResultSummaryJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(run.ResultSummaryJson);
                var root = doc.RootElement;
                if (TryGetJsonProperty(root, "riskOnlyShadowPortfolio", out var ro) && ro.ValueKind != JsonValueKind.Null)
                {
                    riskOnlyShadow = JsonSerializer.Deserialize<ShadowPortfolioSummaryDto>(ro.GetRawText(), jsonOptions);
                }

                if (TryGetJsonProperty(root, "fullPipelineShadowPortfolio", out var fp) && fp.ValueKind != JsonValueKind.Null)
                {
                    fullPipelineShadow = JsonSerializer.Deserialize<ShadowPortfolioSummaryDto>(fp.GetRawText(), jsonOptions);
                }

                if (TryGetJsonProperty(root, "portfolioPathDivergence", out var div) && div.ValueKind != JsonValueKind.Null)
                {
                    pathDivergence = JsonSerializer.Deserialize<PortfolioPathDivergenceDto>(div.GetRawText(), jsonOptions);
                }

                if (TryGetJsonProperty(root, "pathDiagnostics", out var pd) && pd.ValueKind == JsonValueKind.Array)
                {
                    pathDiagnostics = JsonSerializer.Deserialize<List<string>>(pd.GetRawText(), jsonOptions) ?? [];
                }

                if (TryGetJsonProperty(root, "riskPathAssessmentVersion", out var rpv) && rpv.ValueKind == JsonValueKind.String)
                {
                    riskPathVersion = rpv.GetString();
                }

                if (TryGetJsonProperty(root, "portfolioRiskScoreDiagnostics", out var prd) && prd.ValueKind != JsonValueKind.Null)
                {
                    portfolioScoreDiag = JsonSerializer.Deserialize<ScoreDistributionDiagnosticsDto>(prd.GetRawText(), jsonOptions);
                }

                if (TryGetJsonProperty(root, "drawdownCalculationMode", out var ddm) && ddm.ValueKind == JsonValueKind.String)
                {
                    drawdownMode = ddm.GetString();
                }
            }
            catch (JsonException)
            {
                // ignore optional shadow summary parse failures
            }
        }

        return ServiceResult<StrategyLabRunDetailDto>.Ok(new StrategyLabRunDetailDto
        {
            Run = MapRun(run, currentVersion),
            Summary = summary!,
            Funnel = funnel!,
            GatedComparison = gated,
            Candidates = candidatePreview,
            Warnings = warnings,
            CoverageDiagnostics = coverageDiagnostics,
            ZeroCandidateExplanation = zeroExplanation,
            DiagnosticEvents = diagnosticEvents,
            SampleFingerprints = sampleFingerprints,
            RiskOnlyShadowPortfolio = riskOnlyShadow,
            FullPipelineShadowPortfolio = fullPipelineShadow,
            PortfolioPathDivergence = pathDivergence,
            PathDiagnostics = pathDiagnostics,
            RiskPathAssessmentVersion = riskPathVersion,
            PortfolioRiskScoreDiagnostics = portfolioScoreDiag,
            DrawdownCalculationMode = drawdownMode
        });
    }

    public async Task<ServiceResult<PagedResultDto<StrategyResearchCandidateDto>>> GetCandidatesAsync(
        long runId,
        StrategyLabCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<PagedResultDto<StrategyResearchCandidateDto>>.Fail("Strategy lab run not found.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize is 25 or 50 or 100 or 250 ? query.PageSize : 50;
        var (items, total) = await _candidateRepository.QueryByRunIdAsync(
            runId,
            new StrategyLabCandidateQuerySpec
            {
                Page = page,
                PageSize = pageSize,
                SortBy = query.SortBy,
                SortDirection = query.SortDirection,
                Search = query.Search,
                Direction = query.Direction,
                RawOutcome = query.RawOutcome,
                ConfidenceDecision = query.ConfidenceDecision,
                ConfidenceMin = query.ConfidenceMin,
                ConfidenceMax = query.ConfidenceMax,
                RiskDecision = query.RiskDecision,
                RiskMin = query.RiskMin,
                RiskMax = query.RiskMax,
                ProfitableOnly = query.ProfitableOnly,
                FromUtc = query.FromUtc,
                ToUtc = query.ToUtc,
                QuickFilter = query.QuickFilter,
                RiskOnlyEntryDecision = query.RiskOnlyEntryDecision,
                FullPipelineEntryDecision = query.FullPipelineEntryDecision,
                PathDecisionDifference = query.PathDecisionDifference,
                RiskOnlyFailedRule = query.RiskOnlyFailedRule,
                FullPipelineFailedRule = query.FullPipelineFailedRule,
                RiskOnlyDrawdownMin = query.RiskOnlyDrawdownMin,
                FullPipelineDrawdownMin = query.FullPipelineDrawdownMin
            },
            cancellationToken);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return ServiceResult<PagedResultDto<StrategyResearchCandidateDto>>.Ok(new PagedResultDto<StrategyResearchCandidateDto>
        {
            Items = items.Select(MapCandidate).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = totalPages
        });
    }

    public async Task<ServiceResult<StrategyLabCandidateDetailDto>> GetCandidateDetailAsync(
        long runId,
        long candidateId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyLabCandidateDetailDto>.Fail("Strategy lab run not found.");
        }

        var candidates = await _candidateRepository.GetByRunIdAsync(runId, cancellationToken);
        var candidate = candidates.FirstOrDefault(c => c.Id == candidateId);
        if (candidate is null)
        {
            return ServiceResult<StrategyLabCandidateDetailDto>.Fail("Candidate not found.");
        }

        var mapped = MapCandidate(candidate);
        var available = mapped.RiskOnlyAssessment is not null || mapped.FullPipelineAssessment is not null
            || !string.IsNullOrWhiteSpace(candidate.RiskPathAssessmentVersion);

        return ServiceResult<StrategyLabCandidateDetailDto>.Ok(new StrategyLabCandidateDetailDto
        {
            Candidate = mapped,
            RiskOnlyAssessment = mapped.RiskOnlyAssessment,
            FullPipelineAssessment = mapped.FullPipelineAssessment,
            FinalPipelineDecision = candidate.FinalPipelineDecision,
            PathComparison = BuildPathComparison(mapped.RiskOnlyAssessment, mapped.FullPipelineAssessment),
            PathAssessmentAvailability = available ? "Available" : "Legacy/Unavailable"
        });
    }

    public async Task<ServiceResult<PortfolioPathComparisonDto>> GetPortfolioPathComparisonAsync(
        long runId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetRunDetailAsync(runId, cancellationToken);
        if (!detail.Succeeded || detail.Data is null)
        {
            return ServiceResult<PortfolioPathComparisonDto>.Fail(detail.ErrorMessage ?? "Strategy lab run not found.");
        }

        var available = detail.Data.RiskOnlyShadowPortfolio is not null
            || detail.Data.FullPipelineShadowPortfolio is not null
            || !string.IsNullOrWhiteSpace(detail.Data.RiskPathAssessmentVersion);

        return ServiceResult<PortfolioPathComparisonDto>.Ok(new PortfolioPathComparisonDto
        {
            RiskOnlySummary = detail.Data.RiskOnlyShadowPortfolio,
            FullPipelineSummary = detail.Data.FullPipelineShadowPortfolio,
            DivergenceSummary = detail.Data.PortfolioPathDivergence,
            Diagnostics = detail.Data.PathDiagnostics,
            RiskPathAssessmentVersion = detail.Data.RiskPathAssessmentVersion ?? IndependentPathsVersions.Current,
            PathAssessmentAvailability = available ? "Available" : "Legacy/Unavailable"
        });
    }

    public async Task<ServiceResult<StrategyLabGateAnalysisDto>> GetGateAnalysisAsync(
        long runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyLabGateAnalysisDto>.Fail("Strategy lab run not found.");
        }

        var candidates = await _candidateRepository.GetByRunIdAsync(runId, cancellationToken);
        return ServiceResult<StrategyLabGateAnalysisDto>.Ok(
            StrategyLabGateAnalysisCalculator.Build(run, candidates));
    }

    public async Task<ServiceResult<StrategyLabRiskAnalysisDto>> GetRiskAnalysisAsync(
        long runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyLabRiskAnalysisDto>.Fail("Strategy lab run not found.");
        }

        var candidates = await _candidateRepository.GetByRunIdAsync(runId, cancellationToken);
        return ServiceResult<StrategyLabRiskAnalysisDto>.Ok(StrategyLabRiskAnalysisCalculator.Build(candidates));
    }

    public async Task<ServiceResult<StrategyLabRiskProfileComparisonDto>> CompareRiskProfilesAsync(
        long runId,
        long otherRunId,
        CancellationToken cancellationToken = default)
    {
        var runA = await _runRepository.GetByIdAsync(runId, cancellationToken);
        var runB = await _runRepository.GetByIdAsync(otherRunId, cancellationToken);
        if (runA is null || runB is null)
        {
            return ServiceResult<StrategyLabRiskProfileComparisonDto>.Fail("One or both strategy lab runs were not found.");
        }

        var candidatesA = await _candidateRepository.GetByRunIdAsync(runId, cancellationToken);
        var candidatesB = await _candidateRepository.GetByRunIdAsync(otherRunId, cancellationToken);
        return ServiceResult<StrategyLabRiskProfileComparisonDto>.Ok(
            StrategyLabRiskAnalysisCalculator.Compare(runA, candidatesA, runB, candidatesB));
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyLabRunDto>>> GetRunsByStrategyAsync(string strategyCode, int limit = 20, CancellationToken cancellationToken = default)
    {
        var runs = await _runRepository.GetByStrategyCodeAsync(strategyCode, limit, cancellationToken);
        var currentVersion = await GetCurrentVersionAsync(strategyCode, cancellationToken);
        return ServiceResult<IReadOnlyList<StrategyLabRunDto>>.Ok(runs.Select(r => MapRun(r, currentVersion)).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyLabRunDto>>> GetRecentRunsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var runs = await _runRepository.GetRecentAsync(limit, cancellationToken);
        return ServiceResult<IReadOnlyList<StrategyLabRunDto>>.Ok(runs.Select(r => MapRun(r, r.StrategyVersion)).ToList());
    }

    public async Task<ServiceResult<StrategyLabStartupHealthDto>> GetStartupHealthAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        var (runTable, candidateTable) = await _runRepository.CheckTablesAsync(cancellationToken);
        if (!runTable)
        {
            issues.Add("StrategyLabRun table is not available. Apply migrations.");
        }

        if (!candidateTable)
        {
            issues.Add("StrategyResearchCandidate table is not available. Apply migrations.");
        }

        Strategy? breakoutEntity = null;
        Strategy? sweepEntity = null;
        try
        {
            breakoutEntity = await _strategyRepository.GetByCodeAsync(StrategyCode.PriceStructureBreakoutRetest, cancellationToken);
            sweepEntity = await _strategyRepository.GetByCodeAsync(StrategyCode.PriceStructureLiquiditySweepReclaim, cancellationToken);
        }
        catch (Exception ex)
        {
            issues.Add($"Strategy repository check failed: {ex.Message}");
        }

        var breakoutRegistered = breakoutEntity is not null;
        var sweepRegistered = sweepEntity is not null;
        if (!breakoutRegistered)
        {
            issues.Add("PRICE_STRUCTURE_BREAKOUT_RETEST is not registered in the database.");
        }

        if (!sweepRegistered)
        {
            issues.Add("PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM is not registered in the database.");
        }

        var breakoutResolvable = _strategyRegistry.GetByCode(StrategyCode.PriceStructureBreakoutRetest) is not null;
        var sweepResolvable = _strategyRegistry.GetByCode(StrategyCode.PriceStructureLiquiditySweepReclaim) is not null;
        if (!breakoutResolvable)
        {
            issues.Add("PRICE_STRUCTURE_BREAKOUT_RETEST is not resolvable from strategy registry.");
        }

        if (!sweepResolvable)
        {
            issues.Add("PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM is not resolvable from strategy registry.");
        }

        var syntheticAvailable =
            SyntheticScenarioCatalog.ForStrategy(StrategyCodes.PriceStructureBreakoutRetest).Count > 0
            && SyntheticScenarioCatalog.ForStrategy(StrategyCodes.PriceStructureLiquiditySweepReclaim).Count > 0;
        if (!syntheticAvailable)
        {
            issues.Add("Synthetic test scenarios are missing.");
        }

        var healthy = issues.Count == 0;
        return ServiceResult<StrategyLabStartupHealthDto>.Ok(new StrategyLabStartupHealthDto
        {
            Healthy = healthy,
            StrategyLabRunTableAvailable = runTable,
            StrategyResearchCandidateTableAvailable = candidateTable,
            BreakoutRetestRegistered = breakoutRegistered,
            LiquiditySweepRegistered = sweepRegistered,
            BreakoutRetestResolvable = breakoutResolvable,
            LiquiditySweepResolvable = sweepResolvable,
            SyntheticTestsAvailable = syntheticAvailable,
            Issues = issues,
            Status = healthy ? "Healthy" : "Degraded"
        });
    }

    public async Task<ServiceResult<CreateStrategyLabRunRequest>> GetRerunConfigAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<CreateStrategyLabRunRequest>.Fail("Strategy lab run not found.");
        }

        var fee = JsonSerializer.Deserialize<Dictionary<string, decimal>>(run.FeeSettingsJson) ?? new Dictionary<string, decimal>();
        var slippage = JsonSerializer.Deserialize<Dictionary<string, decimal>>(run.SlippageSettingsJson) ?? new Dictionary<string, decimal>();
        return ServiceResult<CreateStrategyLabRunRequest>.Ok(new CreateStrategyLabRunRequest
        {
            Name = run.Name,
            StrategyCode = run.StrategyCode,
            ExchangeId = run.ExchangeId,
            SymbolId = run.SymbolId,
            Timeframe = run.Timeframe,
            FromUtc = run.FromUtc,
            ToUtc = run.ToUtc,
            ExecutionMode = run.ExecutionMode,
            Parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(run.ParametersJson),
            InitialBalance = run.InitialBalance,
            RiskProfileId = run.RiskProfileId,
            MakerFeeRate = fee.GetValueOrDefault("makerFeeRate", 0.0002m),
            TakerFeeRate = fee.GetValueOrDefault("takerFeeRate", 0.0004m),
            SlippagePercent = slippage.GetValueOrDefault("slippagePercent", 0m),
            ObservationSettings = ExtractObservationSettings(run)
        });
    }

    public Task<ServiceResult<IReadOnlyList<SyntheticTestResultDto>>> RunSyntheticTestsAsync(string strategyCode, CancellationToken cancellationToken = default)
    {
        var scenarios = SyntheticScenarioCatalog.ForStrategy(strategyCode);
        var results = _syntheticRunner.RunAll(scenarios).Select(r => new SyntheticTestResultDto
        {
            ScenarioName = r.Scenario.Name,
            Description = r.Scenario.Description,
            Passed = r.Passed,
            ExpectedCandidateCount = r.Scenario.ExpectedCandidateCount,
            ActualCandidateCount = r.ActualCandidateCount,
            ExpectedDirection = r.Scenario.ExpectedDirection,
            ActualDirection = r.ActualDirection,
            ExpectedNoTradeReason = r.Scenario.ExpectedNoTradeReason,
            FailureDetails = r.FailureDetails
        }).ToList();

        return Task.FromResult(ServiceResult<IReadOnlyList<SyntheticTestResultDto>>.Ok(results));
    }

    public async Task<ServiceResult<StrategyHealthDto>> GetStrategyHealthAsync(string strategyCode, CancellationToken cancellationToken = default)
    {
        var strategyEnum = StrategyCodeExtensions.FromCode(strategyCode);
        var entity = await _strategyRepository.GetByCodeAsync(strategyEnum, cancellationToken);
        var plugin = _strategyRegistry.GetByCode(strategyEnum);
        var synthetic = await RunSyntheticTestsAsync(strategyCode, cancellationToken);
        var syntheticResults = synthetic.Succeeded ? synthetic.Data! : [];
        var runs = await _runRepository.GetByStrategyCodeAsync(strategyCode, 5, cancellationToken);

        var recentCandidates = 0;
        var recentEvaluations = 0;
        var rawTrades = 0;
        decimal? confRate = null;
        decimal? riskRate = null;
        decimal candidateRate = 0;

        foreach (var run in runs.Where(r => r.Status == StrategyLabRunStatus.Completed))
        {
            recentEvaluations += run.EvaluationsCount;
            recentCandidates += run.RawCandidateCount;
            var candidates = await _candidateRepository.GetByRunIdAsync(run.Id, cancellationToken);
            rawTrades += candidates.Count(c => c.CandidateStatus == StrategyResearchCandidateStatus.Closed);
            if (recentEvaluations > 0)
            {
                candidateRate = (decimal)recentCandidates / recentEvaluations * 1000m;
            }

            var confEvaluated = candidates.Count(c => c.ConfidenceDecision != ResearchConfidenceDecision.NotEvaluated);
            if (confEvaluated > 0)
            {
                confRate = (decimal)candidates.Count(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved) / confEvaluated * 100m;
            }

            var riskEvaluated = candidates.Count(c => c.RiskDecision != ResearchRiskDecision.NotEvaluated);
            if (riskEvaluated > 0)
            {
                riskRate = (decimal)candidates.Count(c => c.RiskDecision == ResearchRiskDecision.Approved) / riskEvaluated * 100m;
            }
        }

        var warnings = new List<string>();
        var categories = new List<string>();
        var passed = syntheticResults.Count(r => r.Passed);
        var total = syntheticResults.Count;

        if (total > 0 && passed < total)
        {
            warnings.Add("Synthetic tests are failing. Do not judge market performance until detector tests pass.");
            categories.Add("Detector Problem");
        }

        if (recentCandidates == 0 && runs.Count > 0)
        {
            warnings.Add("Strategy has produced no raw candidates in the last 5 research runs.");
            categories.Add("Market Opportunity Problem");
        }

        if (confRate.HasValue && confRate < 10m)
        {
            categories.Add("Confidence Gate Problem");
        }

        if (riskRate.HasValue && riskRate < 10m)
        {
            categories.Add("Risk Gate Problem");
        }

        return ServiceResult<StrategyHealthDto>.Ok(new StrategyHealthDto
        {
            RegistrationStatus = entity is not null && plugin is not null ? "Healthy" : "Error",
            CandleDataStatus = "Healthy",
            SyntheticTestsPassed = passed,
            SyntheticTestsTotal = total,
            RecentEvaluations = recentEvaluations,
            RecentRawCandidates = recentCandidates,
            CandidateRatePer1000Candles = Math.Round(candidateRate, 2),
            RawTrades = rawTrades,
            ConfidenceApprovalRate = confRate,
            RiskApprovalRate = riskRate,
            RecentStrategyLabRuns = runs.Count,
            Warnings = warnings,
            ProblemCategories = categories
        });
    }

    private async Task<string?> GetCurrentVersionAsync(string strategyCode, CancellationToken cancellationToken)
    {
        var entity = await _strategyRepository.GetByCodeAsync(StrategyCodeExtensions.FromCode(strategyCode), cancellationToken);
        return entity?.Version;
    }

    private static StrategyLabRunDto MapRun(StrategyLabRun run, string? currentVersion) => new()
    {
        Id = run.Id,
        Name = run.Name,
        StrategyCode = run.StrategyCode,
        StrategyVersion = run.StrategyVersion,
        ExchangeId = run.ExchangeId,
        SymbolId = run.SymbolId,
        Symbol = run.Symbol,
        Timeframe = run.Timeframe,
        FromUtc = run.FromUtc,
        ToUtc = run.ToUtc,
        ExecutionMode = run.ExecutionMode,
        Status = run.Status,
        ExperimentFingerprint = run.ExperimentFingerprint,
        CurrentStage = run.CurrentStage,
        PercentComplete = run.PercentComplete,
        CreatedAtUtc = run.CreatedAtUtc,
        CompletedAtUtc = run.CompletedAtUtc,
        ErrorMessage = run.ErrorMessage,
        ParametersJson = run.ParametersJson,
        StrategyFeatureFlagsJson = run.StrategyFeatureFlagsJson,
        ObservationSettings = ExtractObservationSettings(run),
        InitialBalance = run.InitialBalance,
        RiskProfileId = run.RiskProfileId,
        CurrentStrategyVersion = currentVersion,
        StrategyVersionChanged = currentVersion is not null && !string.Equals(currentVersion, run.StrategyVersion, StringComparison.Ordinal)
    };

    private static StrategyLabObservationSettingsDto NormalizeObservationSettings(CreateStrategyLabRunRequest request)
    {
        var settings = request.ObservationSettings ?? new StrategyLabObservationSettingsDto();
        settings.ConfidenceModel = string.IsNullOrWhiteSpace(settings.ConfidenceModel)
            ? StrategySetupQualityScorer.ModelVersion
            : settings.ConfidenceModel;
        settings.RiskProfileId ??= request.RiskProfileId;
        if (!settings.UseSystemDefaultConfidenceThreshold)
        {
            settings.CustomConfidenceThreshold = Math.Clamp(settings.CustomConfidenceThreshold ?? 70m, 0m, 100m);
            settings.EffectiveConfidenceThreshold = settings.CustomConfidenceThreshold.Value;
        }
        else
        {
            settings.EffectiveConfidenceThreshold = settings.EffectiveConfidenceThreshold > 0
                ? settings.EffectiveConfidenceThreshold
                : 80m;
        }

        return settings;
    }

    private static StrategyLabObservationSettingsDto ExtractObservationSettings(StrategyLabRun run)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(run.StrategyFeatureFlagsJson) && run.StrategyFeatureFlagsJson != "{}")
            {
                using var doc = JsonDocument.Parse(run.StrategyFeatureFlagsJson);
                if (doc.RootElement.TryGetProperty("observationSettings", out var el)
                    || doc.RootElement.TryGetProperty("ObservationSettings", out el))
                {
                    var parsed = JsonSerializer.Deserialize<StrategyLabObservationSettingsDto>(el.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (parsed is not null)
                    {
                        parsed.RiskProfileId ??= run.RiskProfileId;
                        return parsed;
                    }
                }
            }
        }
        catch
        {
            // fall through
        }

        return new StrategyLabObservationSettingsDto
        {
            RiskProfileId = run.RiskProfileId,
            ConfidenceModel = StrategySetupQualityScorer.ModelVersion
        };
    }

    private static StrategyResearchCandidateDto MapCandidate(StrategyResearchCandidate candidate)
    {
        string setupType = "Unknown";
        try
        {
            using var doc = JsonDocument.Parse(candidate.StructureJson);
            if (doc.RootElement.TryGetProperty("setupType", out var st)
                || doc.RootElement.TryGetProperty("SetupType", out st))
            {
                setupType = st.GetString() ?? setupType;
            }
            else if (doc.RootElement.TryGetProperty("structure", out var structure)
                     && (structure.TryGetProperty("setupType", out st) || structure.TryGetProperty("SetupType", out st)))
            {
                setupType = st.GetString() ?? setupType;
            }
        }
        catch
        {
            // ignored
        }

        var confidenceThreshold = candidate.ConfidenceThreshold
            ?? TryParseConfidenceThreshold(candidate.ConfidenceReason);
        var confidenceMargin = candidate.ConfidenceMargin
            ?? (candidate.ConfidenceScore.HasValue && confidenceThreshold.HasValue
                ? candidate.ConfidenceScore - confidenceThreshold
                : null);

        return new StrategyResearchCandidateDto
        {
            Id = candidate.Id,
            SetupDetectedAtUtc = candidate.SetupDetectedAtUtc,
            Direction = candidate.Direction,
            SetupType = setupType,
            ProposedEntryPrice = candidate.ProposedEntryPrice,
            StopLoss = candidate.StopLoss,
            Target1 = candidate.Target1,
            RewardRisk = candidate.RewardRisk,
            StrategyReason = candidate.StrategyReason,
            RawOutcomeStatus = candidate.RawOutcomeStatus,
            RawNetPnl = candidate.RawNetPnl,
            RawRMultiple = candidate.RawRMultiple,
            ConfidenceScore = candidate.ConfidenceScore,
            ConfidenceThreshold = confidenceThreshold,
            ConfidenceDecision = candidate.ConfidenceDecision,
            ConfidenceMargin = confidenceMargin,
            ConfidenceReason = candidate.ConfidenceReason,
            ConfidenceModelVersion = candidate.ConfidenceModelVersion,
            ConfidenceComponentsJson = candidate.ConfidenceComponentsJson,
            ConfidenceEvaluatedAtUtc = candidate.ConfidenceEvaluatedAtUtc,
            RiskScore = candidate.CandidateRiskScore ?? candidate.RiskScore,
            CandidateRiskScore = candidate.CandidateRiskScore ?? candidate.RiskScore,
            PortfolioRiskScore = candidate.PortfolioRiskScore,
            PortfolioRiskAssessmentStatus = candidate.PortfolioRiskAssessmentStatus,
            RiskThreshold = candidate.RiskThreshold,
            RiskDecision = candidate.RiskDecision,
            RiskMargin = candidate.RiskMargin,
            RiskReason = candidate.RiskReason,
            RiskModelVersion = candidate.RiskModelVersion,
            RiskAssessmentVersion =                 candidate.RiskAssessmentVersion ?? (
                candidate.RiskEvaluatedAtUtc.HasValue ? RiskObservationVersions.Legacy : null),
            RiskComponentsJson = candidate.RiskComponentsJson,
            RiskRuleResultsJson = candidate.RiskRuleResultsJson,
            RiskFailedRuleKeysJson = candidate.RiskFailedRuleKeysJson,
            RiskWarningRuleKeysJson = candidate.RiskWarningRuleKeysJson,
            RiskPerTradePercent = candidate.RiskPerTradePercent,
            RiskAmount = candidate.RiskAmount,
            RiskAtStopPercent = candidate.RiskAtStopPercent,
            ProposedPositionSize = candidate.ProposedPositionSize,
            PositionNotional = candidate.PositionNotional,
            ProposedLeverage = candidate.MinimumRequiredLeverage ?? candidate.ProposedLeverage,
            MinimumRequiredLeverage = candidate.MinimumRequiredLeverage ?? candidate.ProposedLeverage,
            AssessmentLeverage = candidate.AssessmentLeverage,
            PreferredLeverage = candidate.PreferredLeverage,
            MaxLeverage = candidate.MaxLeverage,
            InitialMarginRequired = candidate.InitialMarginRequired,
            StopDistancePercent = candidate.StopDistancePercent,
            PositionExposurePercent = candidate.NotionalExposurePercent ?? candidate.PositionExposurePercent,
            NotionalExposurePercent = candidate.NotionalExposurePercent ?? candidate.PositionExposurePercent,
            MarginUsagePercent = candidate.MarginUsagePercent,
            EstimatedRoundTripFees = candidate.EstimatedRoundTripFees,
            FeeToTargetPercent = candidate.FeeToTargetPercent,
            PositionSizingUnavailableReason = candidate.PositionSizingUnavailableReason,
            CurrentExposurePercent = candidate.CurrentNotionalExposurePercent ?? candidate.CurrentExposurePercent,
            CurrentNotionalExposurePercent = candidate.CurrentNotionalExposurePercent ?? candidate.CurrentExposurePercent,
            CurrentMarginUsagePercent = candidate.CurrentMarginUsagePercent,
            ConcurrentRiskPercent = candidate.ConcurrentRiskPercent,
            DailyLossUsagePercent = candidate.DailyLossUsagePercent,
            CurrentDrawdownPercent = candidate.CurrentDrawdownPercent,
            ConcurrentPositionCount = candidate.ConcurrentPositionCount,
            RiskScoreDecision = candidate.RiskScoreDecision,
            HardRuleComplianceDecision = candidate.HardRuleComplianceDecision,
            RiskPolicyEligibilityDecision = candidate.RiskPolicyEligibilityDecision,
            RiskPolicyReason = candidate.RiskPolicyReason,
            RiskPolicyFailedRuleKeysJson = candidate.RiskPolicyFailedRuleKeysJson,
            RiskPolicyMinimumConfidence = candidate.RiskPolicyMinimumConfidence,
            FinalPipelineRejectionSourcesJson = candidate.FinalPipelineRejectionSourcesJson,
            RiskProfileId = candidate.RiskProfileId,
            RiskProfileVersion = candidate.RiskProfileVersion,
            RiskProfileName = candidate.RiskProfileName,
            RiskProfileSource = candidate.RiskProfileSource,
            RiskProfileSnapshotId = candidate.RiskProfileSnapshotId,
            RiskRejectedRuleKey = candidate.RiskRejectedRuleKey,
            RiskEvaluatedAtUtc = candidate.RiskEvaluatedAtUtc,
            DrawdownCalculationMode = candidate.DrawdownCalculationMode,
            FinalPipelineDecision = candidate.FinalPipelineDecision,
            RawExitTimeUtc = candidate.RawExitTimeUtc,
            ExitOutcome = candidate.ExitOutcome,
            NetResult = candidate.NetResult,
            Mfe = candidate.Mfe,
            Mae = candidate.Mae,
            DurationBars = candidate.DurationBars,
            StructureJson = candidate.StructureJson,
            GenericRiskFieldSource = candidate.GenericRiskFieldSource?.ToString(),
            RiskPathAssessmentVersion = candidate.RiskPathAssessmentVersion,
            RiskOnlyFinancialRiskDecision = candidate.RiskOnlyFinancialRiskDecision,
            RiskOnlyEntryDecision = candidate.RiskOnlyEntryDecision,
            RiskOnlyRejectionSourcesJson = candidate.RiskOnlyRejectionSourcesJson,
            RiskOnlyAssessment = DeserializePathAssessment(candidate.RiskOnlyAssessmentJson),
            RiskOnlyCurrentDrawdownPercent = candidate.RiskOnlyCurrentDrawdownPercent,
            RiskOnlyDailyLossUsagePercent = candidate.RiskOnlyDailyLossUsagePercent,
            RiskOnlyCurrentMarginUsagePercent = candidate.RiskOnlyCurrentMarginUsagePercent,
            RiskOnlyConcurrentRiskPercent = candidate.RiskOnlyConcurrentRiskPercent,
            RiskOnlyOpenPositionCount = candidate.RiskOnlyOpenPositionCount,
            FullPipelineFinancialRiskDecision = candidate.FullPipelineFinancialRiskDecision,
            FullPipelineEntryDecision = candidate.FullPipelineEntryDecision,
            FullPipelineRejectionSourcesJson = candidate.FullPipelineRejectionSourcesJson,
            FullPipelineAssessment = DeserializePathAssessment(candidate.FullPipelineAssessmentJson),
            FullPipelineCurrentDrawdownPercent = candidate.FullPipelineCurrentDrawdownPercent,
            FullPipelineDailyLossUsagePercent = candidate.FullPipelineDailyLossUsagePercent,
            FullPipelineCurrentMarginUsagePercent = candidate.FullPipelineCurrentMarginUsagePercent,
            FullPipelineConcurrentRiskPercent = candidate.FullPipelineConcurrentRiskPercent,
            FullPipelineOpenPositionCount = candidate.FullPipelineOpenPositionCount
        };
    }

    private static PathPortfolioAssessmentDto? DeserializePathAssessment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PathPortfolioAssessmentDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static PortfolioPathCandidateComparisonDto? BuildPathComparison(
        PathPortfolioAssessmentDto? riskOnly,
        PathPortfolioAssessmentDto? fullPipeline)
    {
        if (riskOnly is null || fullPipeline is null)
        {
            return null;
        }

        var highlights = new List<string>();
        if (riskOnly.FinancialRiskDecision != fullPipeline.FinancialRiskDecision)
        {
            highlights.Add(
                $"Financial risk: Risk-Only={riskOnly.FinancialRiskDecision}, Full-Pipeline={fullPipeline.FinancialRiskDecision}");
        }

        if (riskOnly.EntryDecision != fullPipeline.EntryDecision)
        {
            highlights.Add($"Entry: Risk-Only={riskOnly.EntryDecision}, Full-Pipeline={fullPipeline.EntryDecision}");
        }

        if (riskOnly.CurrentDrawdownPercent != fullPipeline.CurrentDrawdownPercent)
        {
            highlights.Add(
                $"Drawdown: Risk-Only={riskOnly.CurrentDrawdownPercent:0.####}%, Full-Pipeline={fullPipeline.CurrentDrawdownPercent:0.####}%");
        }

        return new PortfolioPathCandidateComparisonDto
        {
            FinancialRiskDecisionsDiffer = riskOnly.FinancialRiskDecision != fullPipeline.FinancialRiskDecision,
            EntryDecisionsDiffer = riskOnly.EntryDecision != fullPipeline.EntryDecision,
            DrawdownDifference = (riskOnly.CurrentDrawdownPercent ?? 0m) - (fullPipeline.CurrentDrawdownPercent ?? 0m),
            DailyLossDifference = (riskOnly.CurrentDailyLossUsagePercent ?? 0m) - (fullPipeline.CurrentDailyLossUsagePercent ?? 0m),
            BalanceDifference = riskOnly.AssessmentBalance - fullPipeline.AssessmentBalance,
            HighlightedDifferences = highlights
        };
    }

    private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Legacy runs stored confidence reason like "Confidence 70.00 &lt; 80.00" before threshold columns existed.
    /// </summary>
    private static decimal? TryParseConfidenceThreshold(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        // Patterns: "Confidence 70.00 < 80.00" or "Confidence 82.00 >= 80.00"
        var parts = reason.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 4
            && decimal.TryParse(parts[^1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var threshold))
        {
            return threshold;
        }

        return null;
    }
}
