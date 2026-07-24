using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0D WP35 — authenticated API → real validation training orchestrator.
/// The runner seam supplies deterministic strategy output only. Production owns trial rows,
/// metric calculation, snapshots, guardrails, rank, selection, segment writing, and audits.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230DOrchestrationTests
{
    [Fact]
    public async Task RunTrainingApi_UsesCalculatorSnapshots_ToRankSelectReconcileAndAudit()
    {
        await using var factory = new DeterministicOrchestrationFactory();
        long? userId = null;
        long? experimentId = null;
        var candleIds = new List<long>();

        try
        {
            var (client, disposableUserId) =
                await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(factory, "m230d-orch");
            userId = disposableUserId;

            var reference = await GetReferenceSymbolAsync(factory);
            var requestedStart = new DateTime(2040, 1, 2, 1, 0, 0, DateTimeKind.Utc);
            candleIds.AddRange(await SeedCandlesAsync(
                factory,
                reference.ExchangeId,
                reference.SymbolId,
                requestedStart.AddMinutes(-100 * 15),
                count: 300));

            experimentId = await CreatePreparedAsync(
                client,
                reference,
                requestedStart,
                requestedStart.AddMinutes(199 * 15),
                requiredWarmup: 100);

            var response = await client.PostAsync(
                $"/api/v1/validation-lab/experiments/{experimentId}/run-training", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            var terminal = await PollUntilTerminalAsync(client, experimentId.Value);
            Assert.Equal(ValidationExperimentStatus.TrainingCompleted, terminal.Status);
            Assert.Equal(ValidationWarmupStatus.Complete, terminal.WarmupStatus);
            Assert.Equal(100, terminal.AvailableWarmupCandles);

            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var trials = await sp.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(experimentId.Value);
            var trial = Assert.Single(trials);

            Assert.Equal(ValidationMetricsContract.VersionV132, trial.TrialMetricsVersion);
            Assert.False(string.IsNullOrWhiteSpace(trial.TrialMetricSnapshotJson));
            Assert.False(string.IsNullOrWhiteSpace(trial.TrialMetricFingerprint));
            Assert.Equal(64, trial.TrialMetricFingerprint!.Length);
            Assert.NotNull(trial.GuardrailEvaluationJson);
            Assert.Equal(ValidationTrialRankEligibility.Eligible, trial.TrialRankEligibility);
            Assert.Equal(1, trial.Rank);
            Assert.Equal(trial.Id, terminal.SelectedTrialId);
            Assert.Equal(trial.TrialMetricFingerprint, terminal.SelectedMetricFingerprint);
            Assert.Equal(
                ValidationTrialSegmentReconciliationStatus.Matched,
                terminal.TrialSegmentReconciliationStatus);

            var audits = await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .GetByExperimentIdAsync(experimentId.Value);
            Assert.NotEmpty(audits);
            Assert.All(audits, audit => Assert.False(string.IsNullOrWhiteSpace(audit.AccessPurpose)));
            Assert.Contains(audits, audit => audit.AccessPurpose == ValidationCandleAccessPurpose.EvaluationRange.ToString());
            Assert.Contains(audits, audit => audit.AccessPurpose == ValidationCandleAccessPurpose.ByOpenTime.ToString());
            Assert.DoesNotContain(audits, audit => audit.WasDenied);
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds);
            if (userId is long id)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(factory, id);
            }
        }
    }

    internal static async Task<(long ExchangeId, long SymbolId)> GetReferenceSymbolAsync(
        MomoQuantWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
        var reference = await repo.GetByIdAsync(23) ?? (await repo.GetRecentAsync(1)).FirstOrDefault();
        if (reference is not null)
        {
            return (reference.ExchangeId, reference.SymbolId);
        }

        var symbols = scope.ServiceProvider.GetRequiredService<ISymbolRepository>();
        var (items, _) = await symbols.GetPagedAsync(new PagedRequest { Page = 1, PageSize = 20 }, null);
        var symbol = items.FirstOrDefault() ?? throw new InvalidOperationException("No integration symbol exists.");
        return (symbol.ExchangeId, symbol.Id);
    }

    internal static async Task<IReadOnlyList<long>> SeedCandlesAsync(
        MomoQuantWebApplicationFactory factory,
        long exchangeId,
        long symbolId,
        DateTime firstOpenUtc,
        int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var now = DateTime.UtcNow;
        var candles = Enumerable.Range(0, count).Select(i =>
        {
            var openTime = firstOpenUtc.AddMinutes(i * 15);
            var open = 100m + (i % 20) * 0.1m;
            return new Candle
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = openTime,
                CloseTimeUtc = openTime.AddMinutes(15).AddTicks(-1),
                Open = open,
                High = open + 1m,
                Low = open - 1m,
                Close = open + 0.25m,
                Volume = 100m + i,
                QuoteVolume = (100m + i) * open,
                TradeCount = 10 + i,
                IsClosed = true,
                CreatedAtUtc = now
            };
        }).ToList();
        db.Candles.AddRange(candles);
        await db.SaveChangesAsync();
        return candles.Select(c => c.Id).ToList();
    }

    internal static async Task<long> CreatePreparedAsync(
        HttpClient client,
        (long ExchangeId, long SymbolId) reference,
        DateTime requestedStart,
        DateTime requestedEnd,
        int requiredWarmup)
    {
        var request = new CreateValidationExperimentRequest
        {
            Name = $"VL-230D-Orchestration-{Guid.NewGuid():N}",
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            StrategyVersion = "1.0.0",
            ExchangeId = reference.ExchangeId,
            SymbolId = reference.SymbolId,
            Timeframe = "15m",
            RequestedStartUtc = requestedStart,
            RequestedEndUtc = requestedEnd,
            SplitRatio = 0.70m,
            RequiredWarmupCandles = requiredWarmup,
            MaximumTrials = 1,
            DeterministicSeed = 23035,
            AutoImportMissingCandles = false,
            ParameterSearchSpaceOverrides = SingleTrialSearchSpace(),
            QualificationProfile = new ValidationQualificationProfileDto
            {
                MinimumTrainingClosedTrades = 1,
                MinimumTrainingProfitFactor = 0m,
                MinimumTrainingNetExpectancyR = -999m,
                MaximumTrainingDrawdownPercent = 100m
            }
        };

        var create = await client.PostAsJsonAsync("/api/v1/validation-lab/experiments", request);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ValidationExperimentDto>>(
            IntegrationTestJson.Options);
        Assert.NotNull(created?.Data);

        var prepare = await client.PostAsync(
            $"/api/v1/validation-lab/experiments/{created!.Data!.Id}/prepare-data", null);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        return created.Data.Id;
    }

    private static Dictionary<string, string> SingleTrialSearchSpace() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["swingLeftBarsMin"] = "1", ["swingLeftBarsMax"] = "1", ["swingLeftBarsStep"] = "1",
        ["swingRightBarsMin"] = "1", ["swingRightBarsMax"] = "1", ["swingRightBarsStep"] = "1",
        ["retestTolerancePercentMin"] = "0.3", ["retestTolerancePercentMax"] = "0.3",
        ["retestTolerancePercentStep"] = "0.1",
        ["maxRetestBarsMin"] = "10", ["maxRetestBarsMax"] = "10", ["maxRetestBarsStep"] = "1",
        ["fixedRewardRiskMin"] = "2", ["fixedRewardRiskMax"] = "2", ["fixedRewardRiskStep"] = "0.5",
        ["stopBufferPercentMin"] = "0.05", ["stopBufferPercentMax"] = "0.05",
        ["stopBufferPercentStep"] = "0.05"
    };

    internal static async Task<ValidationExperimentDetailDto> PollUntilTerminalAsync(
        HttpClient client,
        long experimentId)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/v1/validation-lab/experiments/{experimentId}");
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ValidationExperimentDetailDto>>(
                IntegrationTestJson.Options);
            if (payload?.Data is { } detail
                && detail.Status is ValidationExperimentStatus.TrainingCompleted
                    or ValidationExperimentStatus.Failed)
            {
                return detail;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Experiment {experimentId} did not reach a terminal training state.");
    }

    internal static async Task CleanupAsync(
        MomoQuantWebApplicationFactory factory,
        long? experimentId,
        IReadOnlyCollection<long> candleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        if (experimentId is long id)
        {
            var runIds = await db.ValidationParameterTrials
                .Where(t => t.ValidationExperimentId == id && t.StrategyLabRunId != null)
                .Select(t => t.StrategyLabRunId!.Value)
                .ToListAsync();
            await db.ValidationCandleAccessAudits.Where(a => a.ValidationExperimentId == id).ExecuteDeleteAsync();
            await db.ValidationSegmentResults.Where(s => s.ValidationExperimentId == id).ExecuteDeleteAsync();
            await db.ValidationParameterTrials.Where(t => t.ValidationExperimentId == id).ExecuteDeleteAsync();
            await db.ValidationExperimentExecutionLeases.Where(l => l.ValidationExperimentId == id).ExecuteDeleteAsync();
            await db.ResearchOperationStatuses
                .Where(o => o.EntityId == id.ToString())
                .ExecuteDeleteAsync();
            if (runIds.Count > 0)
            {
                await db.StrategyResearchCandidates.Where(c => runIds.Contains(c.StrategyLabRunId)).ExecuteDeleteAsync();
                await db.StrategyLabRuns.Where(r => runIds.Contains(r.Id)).ExecuteDeleteAsync();
            }
            await db.ValidationExperiments.Where(e => e.Id == id).ExecuteDeleteAsync();
        }

        if (candleIds.Count > 0)
        {
            await db.Candles.Where(c => candleIds.Contains(c.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed class DeterministicOrchestrationFactory : MomoQuantWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStrategyLabRunner>();
                services.AddScoped<IStrategyLabRunner, DeterministicCandidateRunner>();
            });
        }
    }

    private sealed class DeterministicCandidateRunner : IStrategyLabRunner
    {
        private readonly IStrategyLabRunRepository _runs;
        private readonly IStrategyResearchCandidateRepository _candidates;

        public DeterministicCandidateRunner(
            IStrategyLabRunRepository runs,
            IStrategyResearchCandidateRepository candidates)
        {
            _runs = runs;
            _candidates = candidates;
        }

        public Task ExecuteAsync(long runId, CancellationToken cancellationToken = default) =>
            ExecuteAsync(runId, StrategyLabExecutionContext.ForGeneralResearch(), cancellationToken);

        public async Task ExecuteAsync(
            long runId,
            StrategyLabExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ExecutionPurpose.ValidationTraining, executionContext.ExecutionPurpose);
            var scope = ValidationTrainingCandleScopeAmbient.Current
                        ?? throw new InvalidOperationException("Validation training scope was not propagated.");
            var range = scope.GetEvaluationRange(
                scope.SegmentStartUtc,
                scope.SegmentEndExclusiveUtc,
                ValidationCandleAccessContext.Create(
                    "M230D.DeterministicRunner",
                    ValidationCandleAccessPurpose.EvaluationRange));
            Assert.True(range.Count >= 10);
            Assert.NotNull(scope.GetByOpenTimeUtc(
                range[5].OpenTimeUtc,
                ValidationCandleAccessContext.Create(
                    "M230D.DeterministicRunner",
                    ValidationCandleAccessPurpose.ByOpenTime)));

            var run = await _runs.GetByIdAsync(runId, cancellationToken)
                      ?? throw new InvalidOperationException($"Strategy Lab run {runId} was not found.");
            var outputs = Enumerable.Range(0, 5).Select(i =>
            {
                var winner = i < 4;
                var entryTime = range[10 + i].OpenTimeUtc;
                return new StrategyResearchCandidate
                {
                    StrategyLabRunId = run.Id,
                    StrategyCode = run.StrategyCode,
                    StrategyVersion = run.StrategyVersion,
                    ExchangeId = run.ExchangeId,
                    SymbolId = run.SymbolId,
                    Symbol = run.Symbol,
                    Timeframe = run.Timeframe,
                    Direction = TradeDirection.Long,
                    SetupDetectedAtUtc = entryTime,
                    ProposedEntryTimeUtc = entryTime,
                    ProposedEntryPrice = 100m,
                    StopLoss = 99m,
                    Target1 = 102m,
                    RewardRisk = 2m,
                    CandidateStatus = StrategyResearchCandidateStatus.Closed,
                    StrategyReason = "Deterministic orchestration candidate",
                    SetupFingerprint = $"m230d-{run.Id}-{i}",
                    ParametersJson = run.ParametersJson,
                    StructureJson = "{}",
                    RawOutcomeStatus = winner ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
                    RawExitTimeUtc = entryTime.AddMinutes(15),
                    RawExitPrice = winner ? 102m : 99.5m,
                    RawExitReason = winner ? "Target" : "Stop",
                    CreatedAtUtc = DateTime.UtcNow
                };
            }).ToList();
            await _candidates.AddRangeAsync(outputs, cancellationToken);

            run.Status = StrategyLabRunStatus.Completed;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.ResultSummaryJson = "{}";
            run.ErrorMessage = null;
            await _runs.UpdateAsync(run, cancellationToken);
        }
    }
}
