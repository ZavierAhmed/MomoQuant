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
/// Milestone 23.0C — ValidationMetrics/v1.3.2 population persistence through writer → DB → API → JSON/CSV export.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230CPathMetricsPopulationTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone230CPathMetricsPopulationTests(MomoQuantWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Writer_PersistsV132Populations_ApiAndExportsExposeThem_ReorderInvariant()
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
            var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
            var labRuns = sp.GetRequiredService<IStrategyLabRunRepository>();
            var candidatesRepo = sp.GetRequiredService<IStrategyResearchCandidateRepository>();
            var writer = sp.GetRequiredService<IValidationSegmentResultWriter>();
            var segments = sp.GetRequiredService<IValidationSegmentResultRepository>();
            var exportService = sp.GetRequiredService<IExportService>();
            var risk = sp.GetRequiredService<IValidationRiskBasisService>();
            var reducer = sp.GetRequiredService<IValidationRiskBasisStatusReducer>();

            var (exchangeId, symbolId, symbol) = await ResolveSymbolAsync(experiments, sp);
            var now = DateTime.UtcNow;
            var entryTime = now.AddHours(-2);
            var exitTime = now.AddHours(-1);

            var assessmentRiskOnly = JsonSerializer.Serialize(new PathPortfolioAssessmentDto
            {
                Quantity = 2m,
                RiskAmount = 2m,
                PortfolioPath = "RiskOnly"
            }, JsonOptions);

            var run = new StrategyLabRun
            {
                Name = $"M230C-POP-{correlationId}",
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

            // A: included winner with warning (mismatched RawGrossPnl)
            // B: included winner clean
            // C: excluded MissingExitPrice
            var candidateA = ClosedCandidate(run.Id, exchangeId, symbolId, symbol, "A",
                100m, 99m, 102m, entryTime, exitTime, ResearchConfidenceDecision.Approved, 1m, 999m, 888m);
            candidateA.RiskOnlyAssessmentJson = assessmentRiskOnly;
            var candidateB = ClosedCandidate(run.Id, exchangeId, symbolId, symbol, "B",
                100m, 99m, 102m, entryTime, exitTime, ResearchConfidenceDecision.Approved, 1m, 2m, 1.9192m);
            var candidateC = ClosedCandidate(run.Id, exchangeId, symbolId, symbol, "C",
                100m, 99m, null, entryTime, null, ResearchConfidenceDecision.Rejected, 1m);
            await candidatesRepo.AddRangeAsync([candidateA, candidateB, candidateC]);

            var experiment = new ValidationExperiment
            {
                Name = $"VL-230C-POP {correlationId}",
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
                ValidationMetricsVersion = ValidationMetricsContract.VersionV132,
                DraftConfigurationJson = JsonSerializer.Serialize(new
                {
                    makerFeeRate = 0.0002m,
                    takerFeeRate = 0.0004m,
                    slippagePercent = 0m
                }, JsonOptions),
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

            await writer.BuildAndPersistSegmentResultsAsync(
                experiment, run.Id, ValidationSegmentType.Validation, candleCount: 500, CancellationToken.None);

            var dbSegments = await segments.GetByExperimentIdAsync(experiment.Id);
            var raw = Assert.Single(dbSegments.Where(s =>
                s.SegmentType == ValidationSegmentType.Validation
                && s.LayerType == ValidationLayerType.RawStrategy));
            Assert.Equal(ValidationMetricsContract.VersionV132, raw.ResultCalculationVersion);

            var rawMetrics = DeserializeMetrics(raw.MetricsJson);
            Assert.NotNull(rawMetrics);
            Assert.Equal(ValidationMetricPopulationSummary.Version, rawMetrics!.PopulationContractVersion);
            Assert.Equal(3, rawMetrics.CandidatePopulationCount);
            Assert.True(rawMetrics.IncludedPathInputCount >= 2);
            Assert.True(rawMetrics.ExcludedPathInputCount >= 1);
            Assert.Equal(1, rawMetrics.MetricWarningBearingIncludedTradeCount);
            Assert.Contains(
                ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                rawMetrics.MetricWarningCodes ?? Array.Empty<string>());
            Assert.DoesNotContain(
                "MissingExitPrice",
                rawMetrics.MetricWarningCodes ?? Array.Empty<string>());
            Assert.True((rawMetrics.MonetaryPnlPopulationCount ?? 0) >= 2);
            Assert.True((rawMetrics.GrossRPopulationCount ?? 0) >= 1);
            Assert.True((rawMetrics.NetRPopulationCount ?? 0) >= 1);

            // Reorder invariance on path-metric aggregation (same populations regardless of trade order).
            var baseInputs = BuildSyntheticPathInputs();
            var forward = ValidationMetricsContract.FromPathTradesV132(
                baseInputs, 500, baseInputs.Count, baseInputs.Count, 0,
                ValidationLayerType.RawStrategy, risk, reducer);
            var reverse = ValidationMetricsContract.FromPathTradesV132(
                baseInputs.AsEnumerable().Reverse().ToList(), 500, baseInputs.Count, baseInputs.Count, 0,
                ValidationLayerType.RawStrategy, risk, reducer);
            Assert.Equal(forward.CandidatePopulationCount, reverse.CandidatePopulationCount);
            Assert.Equal(forward.IncludedPathInputCount, reverse.IncludedPathInputCount);
            Assert.Equal(forward.ExcludedPathInputCount, reverse.ExcludedPathInputCount);
            Assert.Equal(forward.MonetaryPnlPopulationCount, reverse.MonetaryPnlPopulationCount);
            Assert.Equal(forward.GrossRPopulationCount, reverse.GrossRPopulationCount);
            Assert.Equal(forward.NetRPopulationCount, reverse.NetRPopulationCount);
            Assert.Equal(forward.NetExpectancyR, reverse.NetExpectancyR);
            Assert.Equal(forward.RiskBasisValidationStatus, reverse.RiskBasisValidationStatus);

            var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
                _factory, "m230c-pop");
            authUserId = userId;
            var detailResponse = await client.GetAsync($"/api/v1/validation-lab/experiments/{experiment.Id}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detailPayload = await detailResponse.Content
                .ReadFromJsonAsync<ApiResponse<ValidationExperimentDetailDto>>(IntegrationTestJson.Options);
            var apiRaw = detailPayload!.Data!.SegmentResults!
                .Single(s => s.LayerType == ValidationLayerType.RawStrategy
                             && s.SegmentType == ValidationSegmentType.Validation);
            Assert.Equal(ValidationMetricPopulationSummary.Version, apiRaw.PopulationContractVersion);
            Assert.Equal(rawMetrics.CandidatePopulationCount, apiRaw.CandidatePopulationCount);
            Assert.Equal(rawMetrics.IncludedPathInputCount, apiRaw.IncludedPathInputCount);
            Assert.Equal(rawMetrics.ExcludedPathInputCount, apiRaw.ExcludedPathInputCount);
            Assert.Equal(rawMetrics.MonetaryPnlPopulationCount, apiRaw.MonetaryPnlPopulationCount);
            Assert.Equal(rawMetrics.GrossRPopulationCount, apiRaw.GrossRPopulationCount);
            Assert.Equal(rawMetrics.NetRPopulationCount, apiRaw.NetRPopulationCount);
            Assert.Equal(1, apiRaw.MetricWarningBearingIncludedTradeCount);

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
                    if (string.IsNullOrWhiteSpace(job.FilePath) || !File.Exists(job.FilePath))
                    {
                        continue;
                    }

                    exportFilePaths.Add(job.FilePath);
                    var content = await File.ReadAllTextAsync(job.FilePath);
                    Assert.Contains(ValidationMetricPopulationSummary.Version, content, StringComparison.Ordinal);
                    Assert.Contains("CandidatePopulationCount", content, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("NetRPopulationCount", content, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains(
                        ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                        content,
                        StringComparison.Ordinal);
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
                    // best-effort
                }
            }

            await CleanupAsync(experimentId, labRunId);
            if (authUserId is long uid)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(_factory, uid);
            }
        }
    }

    private static List<ValidationPathTradeMetricInput> BuildSyntheticPathInputs() =>
    [
        new()
        {
            ValidationLayer = ValidationLayerType.RawStrategy,
            CandidateFingerprint = "W",
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 1m,
            GrossPnl = 2m,
            NetPnl = 1.9m,
            TotalTransactionCosts = 0.1m,
            Outcome = "Winner",
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included
        },
        new()
        {
            ValidationLayer = ValidationLayerType.RawStrategy,
            CandidateFingerprint = "L",
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 1m,
            GrossPnl = -1m,
            NetPnl = -1.1m,
            TotalTransactionCosts = 0.1m,
            Outcome = "Loser",
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included
        },
        new()
        {
            ValidationLayer = ValidationLayerType.RawStrategy,
            CandidateFingerprint = "X",
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Excluded,
            MetricExclusionReason = "MissingPathQuantity"
        }
    ];

    private static LayerSegmentMetrics? DeserializeMetrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<LayerSegmentMetrics>(json, JsonOptions);
    }

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

        var symbols = sp.GetRequiredService<ISymbolRepository>();
        var (page, _) = await symbols.GetPagedAsync(new PagedRequest { Page = 1, PageSize = 20 }, null);
        var symbol = page.FirstOrDefault()
                     ?? throw new InvalidOperationException("No symbols available for population test.");
        return (symbol.ExchangeId, symbol.Id, symbol.SymbolName);
    }

    private async Task CleanupAsync(long? experimentId, long? labRunId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        if (experimentId is long eid)
        {
            await db.ValidationSegmentResults.Where(s => s.ValidationExperimentId == eid).ExecuteDeleteAsync();
            await db.ValidationExperiments.Where(e => e.Id == eid).ExecuteDeleteAsync();
        }

        if (labRunId is long rid)
        {
            await db.StrategyResearchCandidates.Where(c => c.StrategyLabRunId == rid).ExecuteDeleteAsync();
            await db.StrategyLabRuns.Where(r => r.Id == rid).ExecuteDeleteAsync();
        }
    }
}
