using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Application.Trading.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Constants;

namespace MomoQuant.Application.StrategyBenchmarks;

public interface IStrategyBenchmarkReportService
{
    Task<ServiceResult<StrategyBenchmarkReportDto>> GetReportAsync(
        long benchmarkRunId,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyBenchmarkReportService : IStrategyBenchmarkReportService
{
    private readonly IStrategyBenchmarkRunRepository _runRepository;
    private readonly IStrategyBenchmarkResultRepository _resultRepository;
    private readonly IBacktestRunRepository _backtestRunRepository;
    private readonly IStrategyGradeService _gradeService;
    private readonly IRiskConfidenceCalibrationAdvisor _calibrationAdvisor;

    public StrategyBenchmarkReportService(
        IStrategyBenchmarkRunRepository runRepository,
        IStrategyBenchmarkResultRepository resultRepository,
        IBacktestRunRepository backtestRunRepository,
        IStrategyGradeService gradeService,
        IRiskConfidenceCalibrationAdvisor calibrationAdvisor)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _backtestRunRepository = backtestRunRepository;
        _gradeService = gradeService;
        _calibrationAdvisor = calibrationAdvisor;
    }

    public async Task<ServiceResult<StrategyBenchmarkReportDto>> GetReportAsync(
        long benchmarkRunId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(benchmarkRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkReportDto>.Fail("Strategy benchmark run was not found.");
        }

        var results = await _resultRepository.GetByBenchmarkRunIdAsync(benchmarkRunId, cancellationToken);
        var config = StrategyBenchmarkMapper.ParseConfig(run.ConfigJson);
        var preparation = config.Preparation ?? new StrategyBenchmarkPreparationDto
        {
            Imports = [],
            DataQuality = [],
            Indicators = []
        };

        var diagnosticsByResultId = await BuildDiagnosticsLookupAsync(results, cancellationToken);
        var strategyRanking = BuildStrategyRanking(results, diagnosticsByResultId);
        var strategyDetails = results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.StrategyCode)
            .Select(result => new StrategyBenchmarkResultMatrixDto
            {
                StrategyCode = result.StrategyCode,
                StrategyName = result.StrategyName,
                Symbol = result.Symbol ?? string.Empty,
                Timeframe = result.Timeframe ?? string.Empty,
                Grade = result.Grade,
                Score = result.Score,
                TotalTrades = result.TotalTrades,
                NetPnl = result.NetPnl,
                NetPnlPercent = result.NetPnlPercent,
                MaxDrawdownPercent = result.MaxDrawdownPercent,
                ProfitFactor = result.ProfitFactor,
                WinRatePercent = result.WinRatePercent,
                AverageWin = result.AverageWin,
                AverageLoss = result.AverageLoss,
                LargestLoss = result.LargestLoss,
                AverageRewardRisk = result.AverageRewardRisk,
                Warnings = StrategyBenchmarkMapper.ParseStringListField(result.WarningsJson)
            })
            .ToList();

        var symbolResults = results
            .Where(result => !string.IsNullOrWhiteSpace(result.Symbol))
            .GroupBy(result => result.Symbol!)
            .SelectMany(group => group
                .OrderByDescending(result => result.Score)
                .Take(1)
                .Select(result => new StrategyBenchmarkSymbolResultDto
                {
                    Symbol = result.Symbol!,
                    StrategyCode = result.StrategyCode,
                    StrategyName = result.StrategyName,
                    Grade = result.Grade,
                    Score = result.Score,
                    TotalTrades = result.TotalTrades,
                    NetPnlPercent = result.NetPnlPercent,
                    ProfitFactor = result.ProfitFactor,
                    WinRatePercent = result.WinRatePercent
                }))
            .OrderBy(result => result.Symbol)
            .ToList();

        var timeframeResults = results
            .Where(result => !string.IsNullOrWhiteSpace(result.Timeframe))
            .GroupBy(result => result.Timeframe!)
            .SelectMany(group => group
                .OrderByDescending(result => result.Score)
                .Take(1)
                .Select(result => new StrategyBenchmarkTimeframeResultDto
                {
                    Timeframe = result.Timeframe!,
                    StrategyCode = result.StrategyCode,
                    StrategyName = result.StrategyName,
                    Grade = result.Grade,
                    Score = result.Score,
                    TotalTrades = result.TotalTrades,
                    NetPnlPercent = result.NetPnlPercent,
                    ProfitFactor = result.ProfitFactor,
                    WinRatePercent = result.WinRatePercent
                }))
            .OrderBy(result => result.Timeframe)
            .ToList();

        var bestOverall = strategyRanking.FirstOrDefault()?.StrategyCode;
        var bestBySymbol = symbolResults.ToDictionary(item => item.Symbol, item => item.StrategyCode);
        var bestByTimeframe = timeframeResults.ToDictionary(item => item.Timeframe, item => item.StrategyCode);

        var strategiesToRetune = strategyRanking
            .Where(item => item.Grade is "D" or "F" || item.NetPnlPercent < 0m)
            .Select(item => item.StrategyCode)
            .Distinct()
            .ToList();

        var strategiesNeedingMoreData = strategyRanking
            .Where(item => item.TotalTrades < 5 || item.Grade == "N/A")
            .Select(item => item.StrategyCode)
            .Distinct()
            .ToList();

        var recommendations = BuildRecommendations(strategyRanking, strategiesToRetune, strategiesNeedingMoreData);
        var noTradeAnalysis = BuildNoTradeAnalysis(results, diagnosticsByResultId);
        var riskRejections = BuildRiskRejectionAnalysis(results, diagnosticsByResultId);
        var pipelineFunnel = BuildPipelineFunnel(results, diagnosticsByResultId);
        var candidateTrades = BuildCandidateTrades(results, diagnosticsByResultId);
        var executedTrades = BuildExecutedTrades(results, diagnosticsByResultId);
        var rejectedCandidates = candidateTrades
            .Where(item => !string.Equals(item.FinalDecision, CandidateTradeFinalDecision.Executed.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var shadowTrades = BuildShadowTrades(results, diagnosticsByResultId);
        var rejectionQuality = BuildRejectionQuality(results, diagnosticsByResultId);
        var calibration = _calibrationAdvisor.Build(rejectionQuality);

        return ServiceResult<StrategyBenchmarkReportDto>.Ok(new StrategyBenchmarkReportDto
        {
            Run = StrategyBenchmarkMapper.MapRun(run),
            Summary = new StrategyBenchmarkSummaryDto
            {
                TotalBacktestRuns = run.TotalRuns,
                CompletedRuns = run.CompletedRuns,
                FailedRuns = Math.Max(0, run.TotalRuns - run.CompletedRuns),
                BestOverallStrategy = bestOverall,
                BestStrategyBySymbol = bestBySymbol,
                BestStrategyByTimeframe = bestByTimeframe,
                StrategiesToRetune = strategiesToRetune,
                StrategiesNeedingMoreData = strategiesNeedingMoreData
            },
            DataPreparation = preparation,
            StrategyRanking = strategyRanking,
            StrategyDetails = strategyDetails,
            SymbolResults = symbolResults,
            TimeframeResults = timeframeResults,
            NoTradeAnalysis = noTradeAnalysis,
            RiskRejections = riskRejections,
            PipelineFunnel = pipelineFunnel,
            CandidateTrades = candidateTrades,
            ExecutedTrades = executedTrades,
            RejectedCandidates = rejectedCandidates,
            ShadowTrades = shadowTrades,
            RejectionQuality = rejectionQuality,
            RiskConfidenceCalibration = calibration,
            DecisionRecommendations = recommendations,
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<Dictionary<long, BacktestPipelineDiagnosticsSnapshot>> BuildDiagnosticsLookupAsync(
        IReadOnlyList<StrategyBenchmarkResult> results,
        CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<long, BacktestPipelineDiagnosticsSnapshot>();
        foreach (var result in results)
        {
            var diagnostics = BacktestPipelineDiagnosticsSnapshot.Empty;
            if (result.BacktestRunId is long backtestRunId)
            {
                var backtestRun = await _backtestRunRepository.GetByIdAsync(backtestRunId, cancellationToken);
                diagnostics = BacktestPipelineDiagnosticsSnapshot.FromSettingsJson(backtestRun?.SettingsJson);
            }

            lookup[result.Id] = diagnostics;
        }

        return lookup;
    }

    private List<StrategyBenchmarkStrategyResultDto> BuildStrategyRanking(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        var ranking = new List<StrategyBenchmarkStrategyResultDto>();
        var groups = results.GroupBy(result => result.StrategyCode);

        foreach (var group in groups)
        {
            var cells = group.ToList();
            var representative = cells
                .OrderByDescending(cell => cell.Score)
                .ThenBy(cell => cell.StrategyId)
                .First();
            var aggregateMetrics = new StrategyBenchmarkMetrics
            {
                NetPnlPercent = cells.Count == 0 ? 0m : cells.Average(item => item.NetPnlPercent),
                MaxDrawdownPercent = cells.Count == 0 ? 0m : cells.Max(item => item.MaxDrawdownPercent),
                ProfitFactor = cells.Count == 0 ? 0m : cells.Average(item => item.ProfitFactor),
                WinRatePercent = cells.Count == 0 ? 0m : cells.Average(item => item.WinRatePercent),
                TotalTrades = cells.Sum(item => item.TotalTrades),
                TotalSignals = cells.Sum(item => item.TotalSignals),
                ApprovedSignals = cells.Sum(item => item.ApprovedSignals),
                RejectedSignals = cells.Sum(item => item.RejectedSignals),
                MissedOrders = cells.Sum(item => item.MissedOrders)
            };

            var siblings = cells.Select(item => new StrategyBenchmarkMetrics
            {
                NetPnlPercent = item.NetPnlPercent,
                MaxDrawdownPercent = item.MaxDrawdownPercent,
                ProfitFactor = item.ProfitFactor,
                WinRatePercent = item.WinRatePercent,
                TotalTrades = item.TotalTrades,
                TotalSignals = item.TotalSignals,
                ApprovedSignals = item.ApprovedSignals,
                RejectedSignals = item.RejectedSignals,
                MissedOrders = item.MissedOrders
            }).ToList();

            var grade = _gradeService.Grade(aggregateMetrics, siblings);
            var candidateSignals = cells.Sum(item => ResolveCandidateSignals(item, diagnosticsLookup));
            var confidenceRejected = cells.Sum(item => ResolveConfidenceRejected(item, diagnosticsLookup));
            var riskRejections = cells.Sum(item => ResolveRiskRejections(item, diagnosticsLookup));
            var noTradeCount = cells.Sum(item => ResolveNoTradeCount(item, diagnosticsLookup));
            var shadowNetPnl = cells.Sum(item => ResolveShadowNetPnl(item, diagnosticsLookup));
            var totalRejected = Math.Max(1, confidenceRejected + riskRejections);
            var falseRejectRate = Math.Round(
                cells.Sum(item => ResolveFalseRejectCount(item, diagnosticsLookup)) * 100m / totalRejected,
                2);
            var noTradeReason = cells
                .Select(item => ResolveTopNoTradeReason(item, diagnosticsLookup))
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .GroupBy(reason => reason!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(grouping => grouping.Count())
                .Select(grouping => grouping.Key)
                .FirstOrDefault();
            var resultReason = ResolveResultReason(cells, diagnosticsLookup);

            var bestSymbol = cells.OrderByDescending(item => item.NetPnlPercent).FirstOrDefault()?.Symbol;
            var worstSymbol = cells.OrderBy(item => item.NetPnlPercent).FirstOrDefault()?.Symbol;
            var bestTimeframe = cells.OrderByDescending(item => item.NetPnlPercent).FirstOrDefault()?.Timeframe;
            var worstTimeframe = cells.OrderBy(item => item.NetPnlPercent).FirstOrDefault()?.Timeframe;

            ranking.Add(new StrategyBenchmarkStrategyResultDto
            {
                Rank = 0,
                StrategyId = representative.StrategyId,
                StrategyCode = representative.StrategyCode,
                StrategyName = representative.StrategyName,
                Grade = grade.Grade,
                Score = grade.Score,
                TotalTrades = aggregateMetrics.TotalTrades,
                NetPnl = cells.Sum(item => item.NetPnl),
                NetPnlPercent = aggregateMetrics.NetPnlPercent,
                MaxDrawdownPercent = aggregateMetrics.MaxDrawdownPercent,
                ProfitFactor = aggregateMetrics.ProfitFactor,
                WinRatePercent = aggregateMetrics.WinRatePercent,
                AverageConfidenceScore = cells.Count == 0 ? 0m : cells.Average(item => item.AverageConfidenceScore),
                BestSymbol = bestSymbol,
                WorstSymbol = worstSymbol,
                BestTimeframe = bestTimeframe,
                WorstTimeframe = worstTimeframe,
                ResultReason = resultReason,
                CandidateSignals = candidateSignals,
                ConfidenceRejected = confidenceRejected,
                RiskRejections = riskRejections,
                ShadowNetPnlPercent = shadowNetPnl,
                FalseRejectRatePercent = falseRejectRate,
                NoTradeCount = noTradeCount,
                TopNoTradeReason = noTradeReason,
                Strengths = grade.Strengths,
                Weaknesses = grade.Weaknesses,
                Warnings = grade.Warnings,
                PipelineSummary = ResolvePipelineSummary(representative.StrategyCode, cells, diagnosticsLookup),
                BbSweeps = ResolveBbMetricSum(cells, diagnosticsLookup, funnel => funnel.BollingerBandUpperWickBreaks + funnel.BollingerBandLowerWickBreaks),
                LiquiditySweeps = ResolveBbMetricSum(cells, diagnosticsLookup, funnel => funnel.BuySideLiquiditySweeps + funnel.SellSideLiquiditySweeps),
                CisdConfirmations = ResolveBbMetricSum(cells, diagnosticsLookup, funnel => funnel.CisdConfirmed),
                RsiPassed = ResolveBbMetricSum(cells, diagnosticsLookup, funnel => funnel.RsiPrimedPassed),
                TargetPassed3R = ResolveBbMetricSum(cells, diagnosticsLookup, funnel => funnel.TargetPassed3R),
                FinalCandidates = ResolveBbMetricSum(cells, diagnosticsLookup, funnel => funnel.FinalCandidateSignals)
            });
        }

        return ranking
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.NetPnlPercent)
            .Select((item, index) => new StrategyBenchmarkStrategyResultDto
            {
                Rank = index + 1,
                StrategyId = item.StrategyId,
                StrategyCode = item.StrategyCode,
                StrategyName = item.StrategyName,
                Grade = item.Grade,
                Score = item.Score,
                TotalTrades = item.TotalTrades,
                NetPnl = item.NetPnl,
                NetPnlPercent = item.NetPnlPercent,
                MaxDrawdownPercent = item.MaxDrawdownPercent,
                ProfitFactor = item.ProfitFactor,
                WinRatePercent = item.WinRatePercent,
                AverageConfidenceScore = item.AverageConfidenceScore,
                BestSymbol = item.BestSymbol,
                WorstSymbol = item.WorstSymbol,
                BestTimeframe = item.BestTimeframe,
                WorstTimeframe = item.WorstTimeframe,
                ResultReason = item.ResultReason,
                CandidateSignals = item.CandidateSignals,
                ConfidenceRejected = item.ConfidenceRejected,
                RiskRejections = item.RiskRejections,
                ShadowNetPnlPercent = item.ShadowNetPnlPercent,
                FalseRejectRatePercent = item.FalseRejectRatePercent,
                NoTradeCount = item.NoTradeCount,
                TopNoTradeReason = item.TopNoTradeReason,
                Strengths = item.Strengths,
                Weaknesses = item.Weaknesses,
                Warnings = item.TotalTrades == 0 && item.CandidateSignals == 0 && IsBbStrategy(item.StrategyCode)
                    ? item.Warnings.Concat(["Detection produced no qualifying candidates. Review detector calibration before judging profitability."]).Distinct().ToList()
                    : item.TotalTrades == 0
                        ? item.Warnings.Concat(["No valid trades"]).Distinct().ToList()
                        : item.Warnings,
                PipelineSummary = item.PipelineSummary,
                BbSweeps = item.BbSweeps,
                LiquiditySweeps = item.LiquiditySweeps,
                CisdConfirmations = item.CisdConfirmations,
                RsiPassed = item.RsiPassed,
                TargetPassed3R = item.TargetPassed3R,
                FinalCandidates = item.FinalCandidates
            })
            .ToList();
    }

    private IReadOnlyList<StrategyBenchmarkNoTradeAnalysisDto> BuildNoTradeAnalysis(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        var analysis = new List<StrategyBenchmarkNoTradeAnalysisDto>();
        foreach (var result in results)
        {
            var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var resolvedDiagnostics)
                ? resolvedDiagnostics
                : BacktestPipelineDiagnosticsSnapshot.Empty;
            var evaluations = ResolveEvaluations(result, diagnosticsLookup);
            var noTradeCount = ResolveNoTradeCount(result, diagnosticsLookup);
            var candidateSignals = ResolveCandidateSignals(result, diagnosticsLookup);
            var riskRejections = ResolveRiskRejections(result, diagnosticsLookup);
            var missingDataCount = diagnostics.CandleCount <= 0 ? 1 : 0;
            var missingIndicatorCount = diagnostics.IndicatorSnapshotCount <= 0 ? 1 : 0;
            var topReason = ResolveTopNoTradeReason(result, diagnosticsLookup);
            var topNoTradeReasonCount = ResolveTopNoTradeReasonCount(result, diagnosticsLookup);
            var topRiskReason = diagnostics.TopRiskRejectionReasons.FirstOrDefault()?.Reason;
            var resultReason = ResolveResultReason(new List<StrategyBenchmarkResult> { result }, diagnosticsLookup);
            var recommendation = BuildNoTradeRecommendation(
                result.StrategyCode,
                topReason ?? BbLiquiditySweepRejectionCodes.NoBollingerBandSweep,
                riskRejections,
                missingDataCount,
                missingIndicatorCount,
                candidateSignals,
                result.TotalTrades,
                diagnostics.BbFunnel);

            var bbFunnel = MapBbFunnelDto(diagnostics.BbFunnel);
            analysis.Add(new StrategyBenchmarkNoTradeAnalysisDto
            {
                StrategyCode = result.StrategyCode,
                StrategyName = result.StrategyName,
                Symbol = result.Symbol ?? string.Empty,
                ExecutionTimeframe = result.Timeframe ?? string.Empty,
                Evaluations = evaluations,
                NoTradeCount = noTradeCount,
                CandidateSignals = candidateSignals,
                Trades = result.TotalTrades,
                TopNoTradeReason = topReason,
                TopNoTradeReasonCount = topNoTradeReasonCount,
                MissingDataCount = missingDataCount,
                MissingIndicatorsCount = missingIndicatorCount,
                RiskRejections = riskRejections,
                TopRiskRejectionReason = topRiskReason,
                ResultReason = resultReason,
                Recommendation = recommendation,
                Funnel = BuildFunnel(result, diagnostics, evaluations, candidateSignals, riskRejections),
                TuningSuggestions = BuildTuningSuggestions(result.StrategyCode, result.TotalTrades, result.NetPnlPercent, riskRejections, diagnostics.BbFunnel),
                PipelineSummary = diagnostics.BbFunnel?.PipelineSummary,
                WhyZeroTradesAnalysis = candidateSignals == 0 && result.TotalTrades == 0
                    ? diagnostics.BbFunnel?.WhyZeroTradesAnalysis ?? recommendation
                    : null,
                NoTradeReasonBreakdown = diagnostics.BbFunnel?.NoTradeReasonBreakdown,
                BbFunnelCounts = bbFunnel
            });
        }

        return analysis
            .OrderBy(item => item.StrategyCode)
            .ThenBy(item => item.Symbol)
            .ThenBy(item => item.ExecutionTimeframe)
            .ToList();
    }

    private IReadOnlyList<StrategyBenchmarkRiskRejectionDto> BuildRiskRejectionAnalysis(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        results.Select(result =>
        {
            var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var resolvedDiagnostics)
                ? resolvedDiagnostics
                : BacktestPipelineDiagnosticsSnapshot.Empty;
            var candidateSignals = ResolveCandidateSignals(result, diagnosticsLookup);
            var riskRejections = ResolveRiskRejections(result, diagnosticsLookup);
            var rejectionPercent = candidateSignals <= 0 ? 0m : Math.Round(riskRejections * 100m / candidateSignals, 2);
            var topRiskReason = diagnostics.TopRiskRejectionReasons.FirstOrDefault()?.Reason;
            var recommendation = riskRejections > 0
                ? "Review risk profile, confidence threshold, stop distance, and position sizing."
                : "No significant risk rejections.";

            return new StrategyBenchmarkRiskRejectionDto
            {
                StrategyCode = result.StrategyCode,
                StrategyName = result.StrategyName,
                Symbol = result.Symbol ?? string.Empty,
                ExecutionTimeframe = result.Timeframe ?? string.Empty,
                TotalCandidateSignals = candidateSignals,
                RiskRejections = riskRejections,
                TopRiskReason = topRiskReason,
                RejectionPercent = rejectionPercent,
                Recommendation = recommendation
            };
        })
        .Where(item => item.TotalCandidateSignals > 0 || item.RiskRejections > 0)
        .OrderByDescending(item => item.RiskRejections)
        .ThenBy(item => item.StrategyCode)
        .ToList();

    private IReadOnlyList<StrategyBenchmarkPipelineFunnelDto> BuildPipelineFunnel(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        results.Select(result =>
        {
            var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var snapshot)
                ? snapshot
                : BacktestPipelineDiagnosticsSnapshot.Empty;
            return new StrategyBenchmarkPipelineFunnelDto
            {
                StrategyCode = result.StrategyCode,
                StrategyName = result.StrategyName,
                Symbol = result.Symbol ?? string.Empty,
                Timeframe = result.Timeframe ?? string.Empty,
                Evaluations = ResolveEvaluations(result, diagnosticsLookup),
                CandidateSignals = ResolveCandidateSignals(result, diagnosticsLookup),
                ConfidenceApproved = diagnostics.ConfidenceApproved,
                ConfidenceRejected = diagnostics.ConfidenceRejected,
                RiskApproved = diagnostics.RiskApproved,
                RiskRejected = diagnostics.RiskRejected,
                ExecutedTrades = result.TotalTrades,
                ShadowTrades = diagnostics.ShadowTrades.Count,
                FinalNetPnl = result.NetPnl,
                ShadowNetPnl = diagnostics.RejectionQuality?.ShadowNetPnl ?? 0m,
                PipelineSummary = diagnostics.BbFunnel?.PipelineSummary
            };
        }).ToList();

    private IReadOnlyList<CandidateTradeLedgerDto> BuildCandidateTrades(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        results
            .SelectMany(result =>
            {
                var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var snapshot)
                    ? snapshot
                    : BacktestPipelineDiagnosticsSnapshot.Empty;
                return diagnostics.CandidateTrades.Select(item => new CandidateTradeLedgerDto
                {
                    SignalTimeUtc = item.SignalTimeUtc,
                    StrategyCode = item.StrategyCode,
                    StrategyName = item.StrategyName,
                    Symbol = item.Symbol,
                    Timeframe = item.Timeframe,
                    Direction = item.Direction,
                    EntryPrice = item.EntryPrice,
                    StopLoss = item.StopLoss,
                    TakeProfit = item.TakeProfit,
                    CombinedConfidence = item.CombinedConfidence,
                    RiskPercent = item.RiskPercent,
                    Leverage = item.Leverage,
                    MarginUsed = item.MarginUsed,
                    NotionalValue = item.NotionalValue,
                    FinalDecision = item.FinalDecision.ToString(),
                    FinalDecisionReason = item.FinalDecisionReason
                });
            })
            .OrderByDescending(item => item.SignalTimeUtc)
            .ToList();

    private IReadOnlyList<ExecutedTradeLedgerDto> BuildExecutedTrades(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        results
            .SelectMany(result =>
            {
                var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var snapshot)
                    ? snapshot
                    : BacktestPipelineDiagnosticsSnapshot.Empty;
                return diagnostics.CandidateTrades
                    .Where(item => item.FinalDecision == CandidateTradeFinalDecision.Executed)
                    .Select(item => new ExecutedTradeLedgerDto
                    {
                        EntryTimeUtc = item.SignalTimeUtc,
                        ExitTimeUtc = null,
                        StrategyCode = item.StrategyCode,
                        Symbol = item.Symbol,
                        Direction = item.Direction,
                        Leverage = item.Leverage,
                        MarginUsed = item.MarginUsed,
                        NotionalValue = item.NotionalValue,
                        EntryPrice = item.EntryPrice,
                        ExitPrice = null,
                        StopLoss = item.StopLoss,
                        TakeProfit = item.TakeProfit,
                        NetPnl = 0m,
                        NetPnlPercent = 0m,
                        ExitReason = null
                    });
            })
            .OrderByDescending(item => item.EntryTimeUtc)
            .ToList();

    private IReadOnlyList<ShadowTradeLedgerDto> BuildShadowTrades(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        results
            .SelectMany(result =>
            {
                var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var snapshot)
                    ? snapshot
                    : BacktestPipelineDiagnosticsSnapshot.Empty;
                return diagnostics.ShadowTrades.Select(item => new ShadowTradeLedgerDto
                {
                    SignalTimeUtc = item.SignalTimeUtc,
                    StrategyCode = item.StrategyCode,
                    Symbol = item.Symbol,
                    Direction = item.Direction,
                    RejectedBy = item.RejectedBy.ToString(),
                    OutcomeClassification = item.OutcomeClassification.ToString(),
                    ShadowExitReason = item.ShadowExitReason,
                    ShadowNetPnl = item.ShadowNetPnl,
                    MaxFavorableExcursion = item.MaxFavorableExcursion,
                    MaxAdverseExcursion = item.MaxAdverseExcursion,
                    DurationCandles = item.DurationCandles
                });
            })
            .OrderByDescending(item => item.SignalTimeUtc)
            .ToList();

    private IReadOnlyList<StrategyBenchmarkRejectionQualityDto> BuildRejectionQuality(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        results.Select(result =>
        {
            var diagnostics = diagnosticsLookup.TryGetValue(result.Id, out var snapshot)
                ? snapshot
                : BacktestPipelineDiagnosticsSnapshot.Empty;
            var quality = diagnostics.RejectionQuality ?? new RejectionQualityDto();
            return new StrategyBenchmarkRejectionQualityDto
            {
                StrategyCode = result.StrategyCode,
                StrategyName = result.StrategyName,
                Symbol = result.Symbol ?? string.Empty,
                Timeframe = result.Timeframe ?? string.Empty,
                RejectedCandidateCount = quality.RejectedCandidateCount,
                RejectedByConfidenceCount = quality.RejectedByConfidenceCount,
                RejectedByRiskCount = quality.RejectedByRiskCount,
                RejectedByBothCount = quality.RejectedByBothCount,
                ShadowTradesSimulated = quality.ShadowTradesSimulated,
                RejectedWouldHaveWon = quality.RejectedWouldHaveWon,
                RejectedWouldHaveLost = quality.RejectedWouldHaveLost,
                RejectedBreakEven = quality.RejectedBreakEven,
                RejectedNotEnoughData = quality.RejectedNotEnoughData,
                ShadowNetPnl = quality.ShadowNetPnl,
                ConfidenceFalseRejectCount = quality.ConfidenceFalseRejectCount,
                RiskFalseRejectCount = quality.RiskFalseRejectCount,
                ConfidenceCorrectRejectCount = quality.ConfidenceCorrectRejectCount,
                RiskCorrectRejectCount = quality.RiskCorrectRejectCount
            };
        }).ToList();

    private static string BuildNoTradeRecommendation(
        string strategyCode,
        string topReason,
        int riskRejections,
        int missingDataCount,
        int missingIndicatorCount,
        int candidateSignals,
        int tradesOpened,
        BbLiquiditySweepPipelineSnapshot? bbFunnel = null)
    {
        if (missingDataCount > 0)
        {
            return "Required market data is missing. Import required timeframes first.";
        }

        if (missingIndicatorCount > 0)
        {
            return "Required indicators are missing. Recalculate indicators.";
        }

        if (bbFunnel?.FunnelCounts.DetectorCalibrationMode == true)
        {
            return "Detector calibration only — not a final strategy result. Review funnel counts before judging profitability.";
        }

        if (candidateSignals == 0 && tradesOpened == 0 && bbFunnel is not null)
        {
            return bbFunnel.WhyZeroTradesAnalysis
                ?? BbWhyZeroTradesAnalyzer.Analyze(bbFunnel.FunnelCounts, bbFunnel.FunnelCounts.RsiPrimedEvaluations > 0);
        }

        if (candidateSignals > 0 && riskRejections > 0 && tradesOpened == 0)
        {
            return "Strategy generated signals, but risk rejected them. Review risk profile, stop distance, confidence, or position sizing.";
        }

        if (candidateSignals > 0 && tradesOpened > 0)
        {
            return "Strategy generated trades. Review profitability, drawdown, and sample size.";
        }

        if (candidateSignals == 0 && tradesOpened == 0 && IsBbStrategy(strategyCode))
        {
            return "Detection produced no qualifying candidates. Review detector calibration before judging profitability.";
        }

        if (candidateSignals == 0 && tradesOpened == 0)
        {
            return "No qualifying setup occurred in this selected market window. Extend date range or tune parameters.";
        }

        if (strategyCode == "FOUR_HOUR_RANGE_REENTRY" && topReason.Contains("range", StringComparison.OrdinalIgnoreCase))
        {
            return "Do not judge yet. Verify 4h anchor + 5m execution data and preferred timeframe execution.";
        }

        if (topReason.Contains("timeframe", StringComparison.OrdinalIgnoreCase) || topReason.Contains("regime", StringComparison.OrdinalIgnoreCase))
        {
            return "No qualifying setup occurred in this selected market window. Extend date range or tune parameters.";
        }

        return "No meaningful evaluation result. Check strategy implementation.";
    }

    private static IReadOnlyList<string> BuildRecommendations(
        IReadOnlyList<StrategyBenchmarkStrategyResultDto> ranking,
        IReadOnlyList<string> strategiesToRetune,
        IReadOnlyList<string> strategiesNeedingMoreData)
    {
        var recommendations = new List<string>();
        var best = ranking.FirstOrDefault();
        if (best is not null && best.Grade is not "N/A" and not "F" and not "D")
        {
            recommendations.Add($"Prioritize {best.StrategyCode} for LivePaper validation ({best.Grade}, score {best.Score:0.##}).");
        }

        if (strategiesToRetune.Count > 0)
        {
            recommendations.Add($"Needs parameter tuning: {string.Join(", ", strategiesToRetune)}.");
        }

        if (strategiesNeedingMoreData.Count > 0)
        {
            recommendations.Add($"Needs more data / no valid trades yet: {string.Join(", ", strategiesNeedingMoreData)}.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("No strong recommendations yet. Re-run after more market data is available.");
        }

        return recommendations;
    }

    private static int ResolveEvaluations(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        diagnosticsLookup.TryGetValue(result.Id, out var diagnostics) && diagnostics.StrategyEvaluations > 0
            ? diagnostics.StrategyEvaluations
            : Math.Max(result.TotalSignals, result.NoTradeSignals);

    private static int ResolveCandidateSignals(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        if (diagnosticsLookup.TryGetValue(result.Id, out var diagnostics))
        {
            if (IsBbStrategy(result.StrategyCode) && diagnostics.BbFunnel?.FunnelCounts.FinalCandidateSignals > 0)
            {
                return Math.Max(diagnostics.EntrySignals, diagnostics.BbFunnel.FunnelCounts.FinalCandidateSignals);
            }

            if (diagnostics.EntrySignals > 0)
            {
                return diagnostics.EntrySignals;
            }
        }

        return result.TotalSignals;
    }

    private static int ResolveRiskRejections(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        diagnosticsLookup.TryGetValue(result.Id, out var diagnostics) && diagnostics.RiskRejected >= 0
            ? diagnostics.RiskRejected
            : result.RejectedSignals;

    private static int ResolveConfidenceRejected(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        diagnosticsLookup.TryGetValue(result.Id, out var diagnostics) && diagnostics.ConfidenceRejected >= 0
            ? diagnostics.ConfidenceRejected
            : 0;

    private static decimal ResolveShadowNetPnl(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup) =>
        diagnosticsLookup.TryGetValue(result.Id, out var diagnostics) && diagnostics.RejectionQuality is not null
            ? diagnostics.RejectionQuality.ShadowNetPnl
            : 0m;

    private static int ResolveFalseRejectCount(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        if (!diagnosticsLookup.TryGetValue(result.Id, out var diagnostics) || diagnostics.RejectionQuality is null)
        {
            return 0;
        }

        return diagnostics.RejectionQuality.ConfidenceFalseRejectCount + diagnostics.RejectionQuality.RiskFalseRejectCount;
    }

    private static int ResolveNoTradeCount(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        if (diagnosticsLookup.TryGetValue(result.Id, out var diagnostics) && diagnostics.NoTradeSignals > 0)
        {
            return diagnostics.NoTradeSignals;
        }

        var inferred = ResolveEvaluations(result, diagnosticsLookup) - ResolveCandidateSignals(result, diagnosticsLookup);
        return Math.Max(result.NoTradeSignals, Math.Max(0, inferred));
    }

    private static string? ResolveTopNoTradeReason(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        if (diagnosticsLookup.TryGetValue(result.Id, out var diagnostics))
        {
            if (!string.IsNullOrWhiteSpace(diagnostics.BbFunnel?.TopNoTradeReason))
            {
                return diagnostics.BbFunnel.TopNoTradeReason;
            }

            if (diagnostics.TopNoTradeReasons.Count > 0)
            {
                return diagnostics.TopNoTradeReasons[0].Reason;
            }
        }

        return result.TotalTrades == 0 && !IsBbStrategy(result.StrategyCode)
            ? "No valid entry setup met strategy conditions."
            : result.TotalTrades == 0
                ? BbLiquiditySweepRejectionCodes.NoBollingerBandSweep
                : null;
    }

    private static int ResolveTopNoTradeReasonCount(
        StrategyBenchmarkResult result,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        if (diagnosticsLookup.TryGetValue(result.Id, out var diagnostics))
        {
            if (diagnostics.BbFunnel?.TopNoTradeReasonCount > 0)
            {
                return diagnostics.BbFunnel.TopNoTradeReasonCount;
            }

            if (diagnostics.TopNoTradeReasons.Count > 0)
            {
                return diagnostics.TopNoTradeReasons[0].Count;
            }
        }

        return 0;
    }

    private static string ResolveResultReason(
        IReadOnlyList<StrategyBenchmarkResult> results,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        var trades = results.Sum(item => item.TotalTrades);
        var candidateSignals = results.Sum(item => ResolveCandidateSignals(item, diagnosticsLookup));
        var riskRejections = results.Sum(item => ResolveRiskRejections(item, diagnosticsLookup));
        var noTrade = results.Sum(item => ResolveNoTradeCount(item, diagnosticsLookup));
        var hasMissingData = results.Any(item =>
            diagnosticsLookup.TryGetValue(item.Id, out var diagnostics) && diagnostics.CandleCount <= 0);
        var hasMissingIndicators = results.Any(item =>
            diagnosticsLookup.TryGetValue(item.Id, out var diagnostics) && diagnostics.IndicatorSnapshotCount <= 0);

        if (hasMissingData)
        {
            return "Missing required data";
        }

        if (hasMissingIndicators)
        {
            return "Missing indicators";
        }

        if (trades == 0 && candidateSignals > 0 && riskRejections > 0)
        {
            return "All signals rejected by risk";
        }

        if (trades == 0 && noTrade > 0)
        {
            return "No valid setups";
        }

        if (trades > 0 && trades < 5)
        {
            return "Sample too small";
        }

        return trades > 0 ? "Strategy generated trades" : "No meaningful evaluation result";
    }

    private static IReadOnlyList<StrategyFunnelStepDto> BuildFunnel(
        StrategyBenchmarkResult result,
        BacktestPipelineDiagnosticsSnapshot diagnostics,
        int evaluations,
        int candidateSignals,
        int riskRejections)
    {
        var tradesClosed = result.TotalTrades;
        var noTrade = Math.Max(0, diagnostics.NoTradeSignals);
        var funnel = new List<StrategyFunnelStepDto>
        {
            new()
            {
                StepName = "Candles evaluated",
                PassedCount = evaluations,
                FailedCount = 0,
                FailReason = null
            },
            new()
            {
                StepName = "NoTrade outcomes",
                PassedCount = noTrade,
                FailedCount = Math.Max(0, evaluations - noTrade),
                FailReason = diagnostics.TopNoTradeReasons.FirstOrDefault()?.Reason
            },
            new()
            {
                StepName = "Candidate signals",
                PassedCount = candidateSignals,
                FailedCount = Math.Max(0, evaluations - candidateSignals),
                FailReason = diagnostics.TopNoTradeReasons.FirstOrDefault()?.Reason
            },
            new()
            {
                StepName = "Risk approved",
                PassedCount = Math.Max(0, candidateSignals - riskRejections),
                FailedCount = riskRejections,
                FailReason = diagnostics.TopRiskRejectionReasons.FirstOrDefault()?.Reason
            },
            new()
            {
                StepName = "Trades closed",
                PassedCount = tradesClosed,
                FailedCount = Math.Max(0, candidateSignals - tradesClosed),
                FailReason = tradesClosed == 0 ? "No trades reached closed state." : null
            }
        };

        if (result.StrategyCode == "FOUR_HOUR_RANGE_REENTRY")
        {
            funnel.Add(new StrategyFunnelStepDto
            {
                StepName = "4H range ready",
                PassedCount = Math.Max(0, evaluations - diagnostics.CountNoTradeContaining("range has not closed")),
                FailedCount = diagnostics.CountNoTradeContaining("range has not closed"),
                FailReason = "First 4H New York range has not closed yet."
            });
            funnel.Add(new StrategyFunnelStepDto
            {
                StepName = "Close outside range / re-entry path",
                PassedCount = candidateSignals,
                FailedCount = Math.Max(0, evaluations - candidateSignals),
                FailReason = diagnostics.TopNoTradeReasons.FirstOrDefault()?.Reason
            });
        }

        if (IsBbStrategy(result.StrategyCode) && diagnostics.BbFunnel is not null)
        {
            var bb = diagnostics.BbFunnel.FunnelCounts;
            funnel.Insert(1, new StrategyFunnelStepDto { StepName = "Candles in session", PassedCount = bb.CandlesInAllowedSession, FailedCount = bb.CandlesOutsideSession });
            funnel.Insert(2, new StrategyFunnelStepDto { StepName = "BB wick sweeps", PassedCount = bb.BollingerBandUpperWickBreaks + bb.BollingerBandLowerWickBreaks, FailedCount = Math.Max(0, evaluations - bb.BollingerBandUpperWickBreaks - bb.BollingerBandLowerWickBreaks), FailReason = BbLiquiditySweepRejectionCodes.NoBollingerBandSweep });
            funnel.Insert(3, new StrategyFunnelStepDto { StepName = "Liquidity levels available", PassedCount = Math.Max(bb.BuySideLiquidityLevelsAvailable, bb.SellSideLiquidityLevelsAvailable), FailedCount = bb.BuySideLiquidityLevelsAvailable + bb.SellSideLiquidityLevelsAvailable == 0 ? evaluations : 0, FailReason = BbLiquiditySweepRejectionCodes.NoLiquidityLevelsDetected });
            funnel.Insert(4, new StrategyFunnelStepDto { StepName = "Liquidity sweeps", PassedCount = bb.BuySideLiquiditySweeps + bb.SellSideLiquiditySweeps, FailedCount = Math.Max(0, evaluations - bb.BuySideLiquiditySweeps - bb.SellSideLiquiditySweeps), FailReason = BbLiquiditySweepRejectionCodes.NoLiquiditySweep });
            funnel.Insert(5, new StrategyFunnelStepDto { StepName = "CISD candidates", PassedCount = bb.CisdCandidates, FailedCount = Math.Max(0, bb.BuySideLiquiditySweeps + bb.SellSideLiquiditySweeps - bb.CisdCandidates) });
            funnel.Insert(6, new StrategyFunnelStepDto { StepName = "CISD confirmed", PassedCount = bb.CisdConfirmed, FailedCount = Math.Max(0, bb.CisdCandidates - bb.CisdConfirmed), FailReason = BbLiquiditySweepRejectionCodes.NoCisdConfirmation });
            funnel.Insert(7, new StrategyFunnelStepDto { StepName = "RSI primed passed", PassedCount = bb.RsiPrimedPassed, FailedCount = Math.Max(0, bb.RsiPrimedEvaluations - bb.RsiPrimedPassed), FailReason = BbLiquiditySweepRejectionCodes.RsiPrimedFilterFailed });
            funnel.Insert(8, new StrategyFunnelStepDto { StepName = "Target >= minimum R", PassedCount = bb.TargetPassedMinimumR, FailedCount = Math.Max(0, bb.CisdConfirmed - bb.TargetPassedMinimumR), FailReason = BbLiquiditySweepRejectionCodes.TargetLessThanMinimumR });
            funnel.Insert(9, new StrategyFunnelStepDto { StepName = "Target >= 3R", PassedCount = bb.TargetPassed3R, FailedCount = Math.Max(0, bb.TargetPassedMinimumR - bb.TargetPassed3R) });
            funnel.Insert(10, new StrategyFunnelStepDto { StepName = "Final candidates", PassedCount = bb.FinalCandidateSignals, FailedCount = Math.Max(0, bb.TargetPassedMinimumR - bb.FinalCandidateSignals) });
        }

        return funnel;
    }

    private static bool IsBbStrategy(string strategyCode) =>
        string.Equals(strategyCode, StrategyCodes.BbLiquiditySweepCisd, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategyCode, StrategyCodes.BbLiquiditySweepCisdRsiPrimed, StringComparison.OrdinalIgnoreCase);

    private static BbLiquiditySweepFunnelCountsDto? MapBbFunnelDto(BbLiquiditySweepPipelineSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var funnel = snapshot.FunnelCounts;
        return new BbLiquiditySweepFunnelCountsDto
        {
            Evaluations = funnel.Evaluations,
            CandlesInAllowedSession = funnel.CandlesInAllowedSession,
            CandlesOutsideSession = funnel.CandlesOutsideSession,
            BollingerBandUpperWickBreaks = funnel.BollingerBandUpperWickBreaks,
            BollingerBandLowerWickBreaks = funnel.BollingerBandLowerWickBreaks,
            CandlesClosedBackInsideBb = funnel.CandlesClosedBackInsideBb,
            FiveMinuteLiquidityLevelsDetected = funnel.FiveMinuteLiquidityLevelsDetected,
            OneMinuteLiquidityLevelsDetected = funnel.OneMinuteLiquidityLevelsDetected,
            BuySideLiquidityLevelsAvailable = funnel.BuySideLiquidityLevelsAvailable,
            SellSideLiquidityLevelsAvailable = funnel.SellSideLiquidityLevelsAvailable,
            BuySideLiquiditySweeps = funnel.BuySideLiquiditySweeps,
            SellSideLiquiditySweeps = funnel.SellSideLiquiditySweeps,
            CloseBackAcrossLiquidityLine = funnel.CloseBackAcrossLiquidityLine,
            CisdCandidates = funnel.CisdCandidates,
            CisdConfirmed = funnel.CisdConfirmed,
            RsiPrimedEvaluations = funnel.RsiPrimedEvaluations,
            RsiPrimedPassed = funnel.RsiPrimedPassed,
            TargetPassed3R = funnel.TargetPassed3R,
            TargetPassedMinimumR = funnel.TargetPassedMinimumR,
            FinalCandidateSignals = funnel.FinalCandidateSignals,
            TradesCreated = funnel.TradesCreated,
            StrictnessProfile = funnel.StrictnessProfile,
            DetectorCalibrationMode = funnel.DetectorCalibrationMode
        };
    }

    private static string? ResolvePipelineSummary(
        string strategyCode,
        IReadOnlyList<StrategyBenchmarkResult> cells,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup)
    {
        if (!IsBbStrategy(strategyCode))
        {
            return null;
        }

        return cells
            .Select(cell => diagnosticsLookup.TryGetValue(cell.Id, out var diagnostics) ? diagnostics.BbFunnel?.PipelineSummary : null)
            .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary));
    }

    private static int ResolveBbMetricSum(
        IReadOnlyList<StrategyBenchmarkResult> cells,
        IReadOnlyDictionary<long, BacktestPipelineDiagnosticsSnapshot> diagnosticsLookup,
        Func<BbLiquiditySweepFunnelCounts, int> selector) =>
        cells.Sum(cell => diagnosticsLookup.TryGetValue(cell.Id, out var diagnostics) && diagnostics.BbFunnel is not null
            ? selector(diagnostics.BbFunnel.FunnelCounts)
            : 0);

    private sealed class BbLiquiditySweepPipelineSnapshot
    {
        public required BbLiquiditySweepFunnelCounts FunnelCounts { get; init; }
        public required IReadOnlyDictionary<string, int> NoTradeReasonBreakdown { get; init; }
        public string? PipelineSummary { get; init; }
        public string? WhyZeroTradesAnalysis { get; init; }
        public string? TopNoTradeReason { get; init; }
        public int TopNoTradeReasonCount { get; init; }
    }

    private static IReadOnlyList<string> BuildTuningSuggestions(
        string strategyCode,
        int trades,
        decimal netPnlPercent,
        int riskRejections,
        BbLiquiditySweepPipelineSnapshot? bbFunnel = null)
    {
        if (IsBbStrategy(strategyCode))
        {
            var suggestions = new List<string>
            {
                "Run DetectorCalibration first to verify BB sweeps, liquidity levels, and CISD detectors.",
                "Use BalancedResearch for realistic candidate discovery before OriginalStrict validation.",
                "Tune swingLeft/swingRight and EqualHighLowToleranceAtrMultiplier for #itsimpossible approximation."
            };

            if (bbFunnel?.FunnelCounts.BollingerBandUpperWickBreaks + bbFunnel.FunnelCounts.BollingerBandLowerWickBreaks == 0)
            {
                suggestions.Add("No BB sweeps detected — extend date range or relax RequireSweepOutsideBb.");
            }

            if (bbFunnel?.FunnelCounts.BuySideLiquiditySweeps + bbFunnel.FunnelCounts.SellSideLiquiditySweeps == 0)
            {
                suggestions.Add("Increase MaxDistanceFromLiquidityAtrMultiplier or enable includeSingleSwingLevels.");
            }

            return suggestions;
        }

        if (strategyCode == "ATR_VOLATILITY_BREAKOUT")
        {
            return
            [
                "Reduce minimum ATR expansion threshold.",
                "Allow breakout confirmation over 1 candle instead of 2.",
                "Lower minimum volume filter.",
                "Test 5m as well as 15m."
            ];
        }

        if (strategyCode == "BOLLINGER_SQUEEZE_BREAKOUT")
        {
            return
            [
                "Relax squeeze percentile threshold.",
                "Reduce breakout confirmation requirement.",
                "Test longer date range."
            ];
        }

        if (strategyCode == "DONCHIAN_BREAKOUT")
        {
            return
            [
                "Reduce Donchian lookback.",
                "Allow close above channel or high breakout mode.",
                "Test trending market windows."
            ];
        }

        if (strategyCode == "VWAP_MEAN_REVERSION")
        {
            return
            [
                "Tune RSI thresholds.",
                "Tune VWAP deviation threshold.",
                "Add volatility filter.",
                "Consider disabling until retested."
            ];
        }

        if (strategyCode == "FOUR_HOUR_RANGE_REENTRY")
        {
            return
            [
                "Extend date range.",
                "Test more symbols.",
                "Review risk rejection reasons.",
                "Consider allowing more sessions or lower stop buffer."
            ];
        }

        if (trades == 0 && riskRejections > 0)
        {
            return ["Review risk profile and confidence thresholds to reduce hard rejections."];
        }

        if (trades == 0)
        {
            return ["No qualifying setup occurred. Extend date range or tune parameters."];
        }

        if (netPnlPercent < 0m)
        {
            return ["Current result is negative. Review entry filters, stop-loss, and take-profit calibration."];
        }

        return ["Performance is acceptable. Continue validation with more samples."];
    }

    private sealed class BacktestPipelineDiagnosticsSnapshot
    {
        public static readonly BacktestPipelineDiagnosticsSnapshot Empty = new();

        public int CandleCount { get; init; }
        public int IndicatorSnapshotCount { get; init; }
        public int StrategyEvaluations { get; init; }
        public int NoTradeSignals { get; init; }
        public int EntrySignals { get; init; }
        public int ConfidenceApproved { get; init; }
        public int ConfidenceRejected { get; init; }
        public int RiskApproved { get; init; }
        public int RiskRejected { get; init; }
        public IReadOnlyList<CandidateTradeRecord> CandidateTrades { get; init; } = [];
        public IReadOnlyList<ShadowTradeRecord> ShadowTrades { get; init; } = [];
        public RejectionQualityDto? RejectionQuality { get; init; }
        public IReadOnlyList<ReasonCount> TopNoTradeReasons { get; init; } = [];
        public IReadOnlyList<ReasonCount> TopRiskRejectionReasons { get; init; } = [];
        public BbLiquiditySweepPipelineSnapshot? BbFunnel { get; init; }

        public int CountNoTradeContaining(string token) =>
            TopNoTradeReasons
                .Where(item => item.Reason.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Count);

        public static BacktestPipelineDiagnosticsSnapshot FromSettingsJson(string? settingsJson)
        {
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                return Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(settingsJson);
                if (!document.RootElement.TryGetProperty("pipelineDiagnostics", out var pipeline))
                {
                    return Empty;
                }

                return new BacktestPipelineDiagnosticsSnapshot
                {
                    CandleCount = TryGetInt(pipeline, "candleCount"),
                    IndicatorSnapshotCount = TryGetInt(pipeline, "indicatorSnapshotCount"),
                    StrategyEvaluations = TryGetInt(pipeline, "strategyEvaluations"),
                    NoTradeSignals = TryGetInt(pipeline, "noTradeSignals"),
                    EntrySignals = TryGetInt(pipeline, "entrySignals"),
                    ConfidenceApproved = TryGetInt(pipeline, "confidenceApproved"),
                    ConfidenceRejected = TryGetInt(pipeline, "confidenceRejected"),
                    RiskApproved = TryGetInt(pipeline, "riskApproved"),
                    RiskRejected = TryGetInt(pipeline, "riskRejected"),
                    CandidateTrades = ParseCandidateTrades(pipeline, "candidateTrades"),
                    ShadowTrades = ParseShadowTrades(pipeline, "shadowTrades"),
                    RejectionQuality = ParseRejectionQuality(pipeline, "rejectionQuality"),
                    TopNoTradeReasons = ParseReasonCounts(pipeline, "topNoTradeReasons"),
                    TopRiskRejectionReasons = ParseRiskReasonCounts(pipeline, "topRiskRejectionRules"),
                    BbFunnel = ParseBbLiquiditySweep(pipeline, "bbLiquiditySweep")
                };
            }
            catch
            {
                return Empty;
            }
        }

        private static int TryGetInt(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;

        private static IReadOnlyList<ReasonCount> ParseReasonCounts(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var reasons = new List<ReasonCount>();
            foreach (var item in node.EnumerateArray())
            {
                if (!item.TryGetProperty("reason", out var reasonNode))
                {
                    continue;
                }

                var reason = reasonNode.GetString();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    continue;
                }

                reasons.Add(new ReasonCount
                {
                    Reason = reason,
                    Count = item.TryGetProperty("count", out var countNode) && countNode.TryGetInt32(out var parsedCount)
                        ? parsedCount
                        : 0
                });
            }

            return reasons
                .OrderByDescending(item => item.Count)
                .ToList();
        }

        private static IReadOnlyList<ReasonCount> ParseRiskReasonCounts(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var reasons = new List<ReasonCount>();
            foreach (var item in node.EnumerateArray())
            {
                if (!item.TryGetProperty("ruleKey", out var reasonNode))
                {
                    continue;
                }

                var reason = reasonNode.GetString();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    continue;
                }

                reasons.Add(new ReasonCount
                {
                    Reason = reason,
                    Count = item.TryGetProperty("count", out var countNode) && countNode.TryGetInt32(out var parsedCount)
                        ? parsedCount
                        : 0
                });
            }

            return reasons
                .OrderByDescending(item => item.Count)
                .ToList();
        }

        private static IReadOnlyList<CandidateTradeRecord> ParseCandidateTrades(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<CandidateTradeRecord>>(node.GetRawText()) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static IReadOnlyList<ShadowTradeRecord> ParseShadowTrades(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<ShadowTradeRecord>>(node.GetRawText()) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static RejectionQualityDto? ParseRejectionQuality(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<RejectionQualityDto>(node.GetRawText());
            }
            catch
            {
                return null;
            }
        }

        private static BbLiquiditySweepPipelineSnapshot? ParseBbLiquiditySweep(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            try
            {
                var funnelCounts = node.TryGetProperty("funnelCounts", out var funnelNode)
                    ? JsonSerializer.Deserialize<BbLiquiditySweepFunnelCounts>(funnelNode.GetRawText())
                    : null;
                if (funnelCounts is null)
                {
                    return null;
                }

                var breakdown = node.TryGetProperty("noTradeReasonBreakdown", out var breakdownNode)
                    ? JsonSerializer.Deserialize<Dictionary<string, int>>(breakdownNode.GetRawText()) ?? new Dictionary<string, int>()
                    : funnelCounts.NoTradeReasonBreakdown;

                return new BbLiquiditySweepPipelineSnapshot
                {
                    FunnelCounts = funnelCounts,
                    NoTradeReasonBreakdown = breakdown,
                    PipelineSummary = node.TryGetProperty("pipelineSummary", out var summaryNode) ? summaryNode.GetString() : funnelCounts.BuildPipelineSummary(),
                    WhyZeroTradesAnalysis = node.TryGetProperty("whyZeroTradesAnalysis", out var whyNode) ? whyNode.GetString() : null,
                    TopNoTradeReason = node.TryGetProperty("topNoTradeReason", out var topNode) ? topNode.GetString() : funnelCounts.TopNoTradeReason,
                    TopNoTradeReasonCount = node.TryGetProperty("topNoTradeReasonCount", out var countNode) && countNode.TryGetInt32(out var parsedCount)
                        ? parsedCount
                        : funnelCounts.TopNoTradeReasonCount
                };
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class ReasonCount
    {
        public required string Reason { get; init; }
        public int Count { get; init; }
    }
}
