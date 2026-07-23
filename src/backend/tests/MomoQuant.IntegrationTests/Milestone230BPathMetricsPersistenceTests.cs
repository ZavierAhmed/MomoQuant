using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Exports;
using MomoQuant.Application.Exports.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0B Part B — DB-backed path metrics via production
/// <see cref="IValidationSegmentResultWriter"/>, verified through DB + detail API + export service.
/// Builder is not the final assertion target.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230BPathMetricsPersistenceTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone230BPathMetricsPersistenceTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SegmentResultWriter_PersistsPathMetrics_ApiAndExportExposeWarningsDistinctFromExclusions()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        long? experimentId = null;
        long? labRunId = null;
        long? authUserId = null;
        var exportFilePaths = new List<string>();

        try
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();
            var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
            var labRuns = sp.GetRequiredService<IStrategyLabRunRepository>();
            var candidates = sp.GetRequiredService<IStrategyResearchCandidateRepository>();
            var writer = sp.GetRequiredService<IValidationSegmentResultWriter>();
            var segments = sp.GetRequiredService<IValidationSegmentResultRepository>();
            var exportService = sp.GetRequiredService<IExportService>();

            var (exchangeId, symbolId, symbol) = await ResolveSymbolAsync(experiments, sp);

            var now = DateTime.UtcNow;
            var entryTime = now.AddHours(-2);
            var exitTime = now.AddHours(-1);

            // Candidates A/B/C/D — controlled economics from milestone prompt.
            // A: primary Raw/RiskOnly/FullPipeline path source (warning via mismatched RawGrossPnl)
            // B: confidence-rejected (same prices; still in Raw)
            // C: excluded MissingExitPrice
            // D: pending/open (skipped by path builder)
            var assessmentRiskOnly = JsonSerializer.Serialize(new PathPortfolioAssessmentDto
            {
                Quantity = 2m,
                RiskAmount = 2m,
                PortfolioPath = "RiskOnly"
            }, JsonOptions);
            var assessmentFull = JsonSerializer.Serialize(new PathPortfolioAssessmentDto
            {
                Quantity = 5m,
                RiskAmount = 5m,
                PortfolioPath = "FullPipeline"
            }, JsonOptions);

            var run = new StrategyLabRun
            {
                Name = $"M230B-PATH-{correlationId}",
                StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
                StrategyVersion = "1.0.0",
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Symbol = symbol,
                Timeframe = "15m",
                FromUtc = now.AddDays(-7),
                ToUtc = now,
                Status = StrategyLabRunStatus.Completed,
                InitialBalance = 10000m,
                ResultSummaryJson = "{}",
                CreatedAtUtc = now,
                CompletedAtUtc = now,
                PercentComplete = 100m,
                CurrentStage = "Completed"
            };
            await labRuns.AddAsync(run);
            labRunId = run.Id;

            var candidateA = ClosedCandidate(
                run.Id, exchangeId, symbolId, symbol, "A",
                entry: 100m, stop: 99m, exit: 102m,
                entryTime, exitTime,
                ResearchConfidenceDecision.Approved,
                proposedQty: 10m,
                rawGrossPnl: 999m, // mismatch → MetricWarningCodes, still Included
                rawNetPnl: 888m);
            candidateA.RiskOnlyAssessmentJson = assessmentRiskOnly;
            candidateA.FullPipelineAssessmentJson = assessmentFull;

            var candidateB = ClosedCandidate(
                run.Id, exchangeId, symbolId, symbol, "B",
                entry: 100m, stop: 99m, exit: 102m,
                entryTime, exitTime,
                ResearchConfidenceDecision.Rejected,
                proposedQty: 1m,
                rawGrossPnl: 2m,
                rawNetPnl: 1.9192m);

            var candidateC = ClosedCandidate(
                run.Id, exchangeId, symbolId, symbol, "C",
                entry: 100m, stop: 99m, exit: null,
                entryTime, exitTime: null,
                ResearchConfidenceDecision.Rejected, // keep out of ConfidenceQualified so NetExpectancy evaluates
                proposedQty: 1m);

            var candidateD = new StrategyResearchCandidate
            {
                StrategyLabRunId = run.Id,
                StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
                StrategyVersion = "1.0.0",
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Symbol = symbol,
                Timeframe = "15m",
                Direction = TradeDirection.Long,
                SetupDetectedAtUtc = entryTime,
                ProposedEntryTimeUtc = entryTime,
                ProposedEntryPrice = 100m,
                StopLoss = 99m,
                SetupFingerprint = "D",
                CandidateStatus = StrategyResearchCandidateStatus.Detected,
                RawOutcomeStatus = RawOutcomeStatus.Pending,
                ConfidenceDecision = ResearchConfidenceDecision.Approved,
                CreatedAtUtc = now
            };

            await candidates.AddRangeAsync([candidateA, candidateB, candidateC, candidateD]);

            // Bind shadow ledger CandidateId after persistence (init-only ledger entries).
            var persistedCandidates = await candidates.GetByRunIdAsync(run.Id);
            var aId = persistedCandidates.Single(c => c.SetupFingerprint == "A").Id;
            var riskOnlyShadow = BuildShadow(
                path: "RiskOnly",
                candidateId: aId,
                fingerprint: "A",
                quantity: 2m,
                entry: 100m,
                exit: 102m,
                gross: 4m,
                entryFee: 0.2m,
                exitFee: 0.2m,
                net: 3.6m,
                entryTime,
                exitTime);
            var fullShadow = BuildShadow(
                path: "FullPipeline",
                candidateId: aId,
                fingerprint: "A",
                quantity: 5m,
                entry: 100m,
                exit: 99m,
                gross: -5m,
                entryFee: 0.5m,
                exitFee: 0.5m,
                net: -6m,
                entryTime,
                exitTime);
            run.ResultSummaryJson = JsonSerializer.Serialize(new
            {
                riskOnlyShadowPortfolio = riskOnlyShadow,
                fullPipelineShadowPortfolio = fullShadow
            }, JsonOptions);
            await labRuns.UpdateAsync(run);

            // Prove ResultSummaryJson round-trips shadows the writer will load.
            var reloadedRun = await labRuns.GetByIdAsync(run.Id);
            Assert.NotNull(reloadedRun);
            Assert.Contains("riskOnlyShadowPortfolio", reloadedRun!.ResultSummaryJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fullPipelineShadowPortfolio", reloadedRun.ResultSummaryJson, StringComparison.OrdinalIgnoreCase);

            var draftJson = JsonSerializer.Serialize(new
            {
                makerFeeRate = 0.0002m,
                takerFeeRate = 0.0004m,
                slippagePercent = 0m
            }, JsonOptions);

            var experiment = new ValidationExperiment
            {
                Name = $"VL-230B-PATH {correlationId}",
                ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration,
                Status = ValidationExperimentStatus.Completed,
                StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
                StrategyVersion = "1.0.0",
                ExchangeId = exchangeId,
                Exchange = "test",
                SymbolId = symbolId,
                Symbol = symbol,
                Timeframe = "15m",
                RequestedStartUtc = now.AddDays(-14),
                RequestedEndUtc = now,
                SplitRatio = 0.7m,
                TrainingStartUtc = now.AddDays(-14),
                TrainingEndUtc = now.AddDays(-4),
                ValidationStartUtc = now.AddDays(-4),
                ValidationEndUtc = now,
                ValidationStrategyLabRunId = run.Id,
                ValidationMetricsVersion = ValidationMetricsContract.VersionV131,
                DraftConfigurationJson = draftJson,
                ValidationRevealStatus = ValidationRevealStatus.Revealed,
                ValidationRevealedAtUtc = now,
                InitialBalance = 10000m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RiskBasisVersion = ValidationRiskBasisService.Version,
                ParameterFingerprintVersion = ValidationParameterFingerprintService.Version
            };
            await experiments.AddAsync(experiment);
            experimentId = experiment.Id;

            // Production writer — not builder as final assertion target.
            await writer.BuildAndPersistSegmentResultsAsync(
                experiment,
                run.Id,
                ValidationSegmentType.Validation,
                candleCount: 500,
                CancellationToken.None);

            var dbSegments = await segments.GetByExperimentIdAsync(experiment.Id);
            Assert.Equal(4, dbSegments.Count);

            var raw = AssertLayer(dbSegments, ValidationLayerType.RawStrategy);
            var conf = AssertLayer(dbSegments, ValidationLayerType.ConfidenceQualified);
            var riskOnly = AssertLayer(dbSegments, ValidationLayerType.RiskOnly);
            var full = AssertLayer(dbSegments, ValidationLayerType.FullPipeline);

            // Raw: A+B included (same economics). C is Excluded → NetExpectancyApplicability NotEvaluated
            // (mixed included/excluded population). Gross R still evaluates; warnings ≠ exclusion reasons.
            Assert.Equal(ValidationMetricsContract.VersionV131, raw.ResultCalculationVersion);
            Assert.Equal(2m, raw.GrossExpectancyR);
            Assert.Equal(4m, raw.GrossPnl); // A+B
            Assert.Equal(0.0808m * 2, raw.TransactionCosts);

            var rawMetrics = DeserializeMetrics(raw.MetricsJson);
            Assert.NotNull(rawMetrics);
            Assert.Equal(1, rawMetrics!.MetricWarningBearingIncludedTradeCount);
            Assert.Contains(
                ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                rawMetrics.MetricWarningCodes!);
            Assert.Equal(2, rawMetrics.NetExpectancyIncludedTradeCount);
            Assert.True(rawMetrics.NetExpectancyExcludedTradeCount >= 1);
            Assert.Contains(
                rawMetrics.NetExpectancyExclusionReasons ?? Array.Empty<string>(),
                r => r.Contains("C:", StringComparison.Ordinal));
            Assert.DoesNotContain(
                "MissingExitPrice",
                rawMetrics.MetricWarningCodes ?? Array.Empty<string>());
            // Warnings must not be mirrored as exclusion reasons for included warned trade A.
            Assert.DoesNotContain(
                ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                rawMetrics.NetExpectancyExclusionReasons ?? Array.Empty<string>());

            // ConfidenceQualified: only A (Approved) → exact Raw economics
            Assert.Equal(2m, conf.GrossExpectancyR);
            Assert.Equal(1.9192m, conf.NetExpectancyR);
            Assert.Equal(2m, conf.GrossPnl);
            Assert.Equal(1.9192m, conf.NetPnl);
            Assert.Equal(0.0808m, conf.TransactionCosts);

            // RiskOnly: qty 2, gross 4, costs 0.40, net 3.60, GrossR 2, NetR 1.80
            Assert.Equal(2m, riskOnly.GrossExpectancyR);
            Assert.Equal(1.8m, riskOnly.NetExpectancyR);
            Assert.Equal(4m, riskOnly.GrossPnl);
            Assert.Equal(3.6m, riskOnly.NetPnl);
            Assert.Equal(0.4m, riskOnly.TransactionCosts);

            // FullPipeline: qty 5, gross -5, costs 1, net -6, GrossR -1, NetR -1.20
            Assert.Equal(-1m, full.GrossExpectancyR);
            Assert.Equal(-1.2m, full.NetExpectancyR);
            Assert.Equal(-5m, full.GrossPnl);
            Assert.Equal(-6m, full.NetPnl);
            Assert.Equal(1m, full.TransactionCosts);
            Assert.NotEqual(riskOnly.NetExpectancyR, full.NetExpectancyR);

            // Detail API (JWT via disposable admin — not admin@momoquant.local)
            var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(_factory);
            authUserId = userId;
            var detailResponse = await client.GetAsync($"/api/v1/validation-lab/experiments/{experiment.Id}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detailPayload = await detailResponse.Content
                .ReadFromJsonAsync<ApiResponse<ValidationExperimentDetailDto>>(IntegrationTestJson.Options);
            Assert.NotNull(detailPayload?.Data?.SegmentResults);
            var apiRaw = detailPayload!.Data!.SegmentResults!
                .Single(s => s.LayerType == ValidationLayerType.RawStrategy
                             && s.SegmentType == ValidationSegmentType.Validation);
            Assert.Equal(2m, apiRaw.GrossExpectancyR);
            Assert.Equal(1, apiRaw.MetricWarningBearingIncludedTradeCount);
            Assert.Contains(
                ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                apiRaw.MetricWarningCodes!);
            Assert.DoesNotContain(
                "MissingExitPrice",
                apiRaw.MetricWarningCodes ?? Array.Empty<string>());

            var apiConf = detailPayload.Data.SegmentResults!
                .Single(s => s.LayerType == ValidationLayerType.ConfidenceQualified
                             && s.SegmentType == ValidationSegmentType.Validation);
            Assert.Equal(1.9192m, apiConf.NetExpectancyR);
            var apiRo = detailPayload.Data.SegmentResults!
                .Single(s => s.LayerType == ValidationLayerType.RiskOnly
                             && s.SegmentType == ValidationSegmentType.Validation);
            Assert.Equal(1.8m, apiRo.NetExpectancyR);
            var apiFp = detailPayload.Data.SegmentResults!
                .Single(s => s.LayerType == ValidationLayerType.FullPipeline
                             && s.SegmentType == ValidationSegmentType.Validation);
            Assert.Equal(-1.2m, apiFp.NetExpectancyR);

            // Export via real export service (JSON + CSV)
            var jsonExport = await exportService.CreateAsync(
                new CreateExportRequest
                {
                    Scope = ExportScope.ValidationExperiment.ToString(),
                    SourceId = experiment.Id.ToString(),
                    Format = "json",
                    DetailLevel = "full"
                },
                userId: null);
            Assert.True(jsonExport.Succeeded, jsonExport.ErrorMessage);
            Assert.Equal(ExportJobStatus.Completed.ToString(), jsonExport.Data!.Status);
            Assert.False(string.IsNullOrWhiteSpace(jsonExport.Data.FileName));

            var csvExport = await exportService.CreateAsync(
                new CreateExportRequest
                {
                    Scope = ExportScope.ValidationExperiment.ToString(),
                    SourceId = experiment.Id.ToString(),
                    Format = "csv",
                    DetailLevel = "full"
                },
                userId: null);
            Assert.True(csvExport.Succeeded, csvExport.ErrorMessage);

            await using (var exportScope = _factory.Services.CreateAsyncScope())
            {
                var exportDb = exportScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var jobs = await exportDb.ExportJobs
                    .Where(j => j.SourceId == experiment.Id.ToString()
                                && j.Scope == ExportScope.ValidationExperiment)
                    .ToListAsync();
                Assert.True(jobs.Count >= 2);
                foreach (var job in jobs)
                {
                    if (!string.IsNullOrWhiteSpace(job.FilePath) && File.Exists(job.FilePath))
                    {
                        exportFilePaths.Add(job.FilePath);
                        var content = await File.ReadAllTextAsync(job.FilePath);
                        Assert.Contains(
                            ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                            content,
                            StringComparison.Ordinal);
                        Assert.Contains("MetricWarningBearingIncludedTradeCount", content, StringComparison.OrdinalIgnoreCase);
                        Assert.Contains("1.9192", content, StringComparison.Ordinal);
                        Assert.Contains("1.8", content, StringComparison.Ordinal);
                        Assert.Contains("-1.2", content, StringComparison.Ordinal);
                    }
                }
            }
        }
        finally
        {
            foreach (var path in exportFilePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // best-effort cleanup
                }
            }

            if (experimentId is long eid || labRunId is long)
            {
                await CleanupAsync(experimentId, labRunId);
            }

            if (authUserId is long uid)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(_factory, uid);
            }
        }
    }

    private static ValidationSegmentResult AssertLayer(
        IReadOnlyList<ValidationSegmentResult> rows,
        ValidationLayerType layer)
    {
        var row = Assert.Single(rows.Where(r =>
            r.SegmentType == ValidationSegmentType.Validation && r.LayerType == layer));
        return row;
    }

    private static LayerSegmentMetrics? DeserializeMetrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<LayerSegmentMetrics>(json, JsonOptions);
    }

    private static ShadowPortfolioSummaryDto BuildShadow(
        string path,
        long candidateId,
        string fingerprint,
        decimal quantity,
        decimal entry,
        decimal exit,
        decimal gross,
        decimal entryFee,
        decimal exitFee,
        decimal net,
        DateTime entryTime,
        DateTime exitTime) => new()
    {
        PathName = path,
        TradesOpened = 1,
        ProfitableTrades = net >= 0 ? 1 : 0,
        LosingTrades = net < 0 ? 1 : 0,
        GrossPnl = gross,
        RealizedNetPnl = net,
        TotalTransactionCosts = entryFee + exitFee,
        Ledger =
        [
            new ShadowTradeLedgerEntry
            {
                CandidateId = candidateId,
                SetupFingerprint = fingerprint,
                Direction = TradeDirection.Long,
                EntryTimeUtc = entryTime,
                ExitTimeUtc = exitTime,
                EntryPrice = entry,
                ExitPrice = exit,
                Quantity = quantity,
                GrossPnl = gross,
                EntryFee = entryFee,
                ExitFee = exitFee,
                TotalCost = entryFee + exitFee,
                NetPnl = net,
                ExitOutcome = net >= 0 ? "TargetHit" : "StopHit"
            }
        ]
    };

    private static StrategyResearchCandidate ClosedCandidate(
        long runId,
        long exchangeId,
        long symbolId,
        string symbol,
        string fingerprint,
        decimal entry,
        decimal stop,
        decimal? exit,
        DateTime entryTime,
        DateTime? exitTime,
        ResearchConfidenceDecision confidence,
        decimal? proposedQty,
        decimal? rawGrossPnl = null,
        decimal? rawNetPnl = null) => new()
    {
        StrategyLabRunId = runId,
        StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
        StrategyVersion = "1.0.0",
        ExchangeId = exchangeId,
        SymbolId = symbolId,
        Symbol = symbol,
        Timeframe = "15m",
        Direction = TradeDirection.Long,
        SetupDetectedAtUtc = entryTime,
        ProposedEntryTimeUtc = entryTime,
        ProposedEntryPrice = entry,
        StopLoss = stop,
        RawExitPrice = exit,
        RawExitTimeUtc = exitTime,
        SetupFingerprint = fingerprint,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = exit is null
            ? RawOutcomeStatus.Pending
            : exit >= entry
                ? RawOutcomeStatus.Winner
                : RawOutcomeStatus.Loser,
        ConfidenceDecision = confidence,
        ProposedPositionSize = proposedQty,
        RawGrossPnl = rawGrossPnl,
        RawNetPnl = rawNetPnl,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static async Task<(long ExchangeId, long SymbolId, string Symbol)> ResolveSymbolAsync(
        IValidationExperimentRepository experiments,
        IServiceProvider sp)
    {
        var reference = await experiments.GetByIdAsync(23)
                        ?? (await experiments.GetRecentAsync(1)).FirstOrDefault();
        if (reference is not null)
        {
            return (reference.ExchangeId, reference.SymbolId, reference.Symbol);
        }

        var symbolsRepo = sp.GetRequiredService<ISymbolRepository>();
        var (symbols, _) = await symbolsRepo.GetPagedAsync(
            new PagedRequest { Page = 1, PageSize = 50 },
            null);
        var symbol = symbols.FirstOrDefault()
                     ?? throw new InvalidOperationException("No symbols available for path-metrics fixture.");
        return (symbol.ExchangeId, symbol.Id, symbol.SymbolName);
    }

    private async Task CleanupAsync(long? experimentId, long? labRunId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        if (experimentId is long eid)
        {
            await db.ExportJobs
                .Where(j => j.SourceId == eid.ToString())
                .ExecuteDeleteAsync();
            await db.ValidationSegmentResults
                .Where(s => s.ValidationExperimentId == eid)
                .ExecuteDeleteAsync();
            await db.ValidationCandleAccessAudits
                .Where(a => a.ValidationExperimentId == eid)
                .ExecuteDeleteAsync();
            await db.ValidationParameterTrials
                .Where(t => t.ValidationExperimentId == eid)
                .ExecuteDeleteAsync();
            await db.ValidationExperiments
                .Where(e => e.Id == eid)
                .ExecuteDeleteAsync();
        }

        if (labRunId is long rid)
        {
            await db.StrategyResearchCandidates
                .Where(c => c.StrategyLabRunId == rid)
                .ExecuteDeleteAsync();
            await db.StrategyLabRuns
                .Where(r => r.Id == rid)
                .ExecuteDeleteAsync();
        }
    }
}
