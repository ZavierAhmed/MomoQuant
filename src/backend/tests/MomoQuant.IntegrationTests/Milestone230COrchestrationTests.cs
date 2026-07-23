using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Research;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0C Part 6 — true API → production RunTrainingAsync orchestration.
/// Adversarial candle access is injected only via a test <see cref="IStrategyLabRunner"/> DI seam;
/// tests never call <see cref="IValidationTrainingScopeExecution"/> as the top-level action,
/// never manually set LeakageAuditStatus / CurrentStage / TrainingCompleted, never insert audits,
/// and never invoke <see cref="IValidationTrainingFailureHandler"/> directly.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230COrchestrationTests
{
    [Fact]
    public async Task Adversarial_RunTrainingApi_DeniesBoundary_FailsLeakage_BlocksFreezeValidation()
    {
        await using var factory = new OrchestrationFactory(TrialSeamMode.Adversarial);
        var correlationId = Guid.NewGuid().ToString("N");
        long? experimentId = null;
        long? authUserId = null;

        try
        {
            var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
                factory, "m230c-adv");
            authUserId = userId;

            var id = await CreatePreparedExperimentViaApiAsync(client, factory, $"m230c-adv-{correlationId}");
            experimentId = id;

            DateTime validationStart;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var entity = await scope.ServiceProvider
                    .GetRequiredService<IValidationExperimentRepository>()
                    .GetByIdAsync(id);
                Assert.NotNull(entity);
                Assert.NotNull(entity!.ValidationStartUtc);
                validationStart = DateTime.SpecifyKind(entity.ValidationStartUtc.Value, DateTimeKind.Utc);
            }

            var trainResponse = await client.PostAsync(
                $"/api/v1/validation-lab/experiments/{id}/run-training", null);
            Assert.True(
                trainResponse.IsSuccessStatusCode || trainResponse.StatusCode == HttpStatusCode.BadRequest,
                $"Unexpected train status {(int)trainResponse.StatusCode}");
            var trainBody = await trainResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("StackTrace", trainBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("at MomoQuant.", trainBody, StringComparison.Ordinal);

            var terminal = await PollUntilTerminalAsync(client, id, TimeSpan.FromMinutes(2));
            Assert.Equal(ValidationExperimentStatus.Failed, terminal.Status);
            Assert.Equal("LeakageDetected", terminal.CurrentStage);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                // LeakageAuditStatus may be redacted on unrevealed detail DTOs — assert from DB entity.
                var experiment = await sp.GetRequiredService<IValidationExperimentRepository>()
                    .GetByIdAsync(id);
                Assert.NotNull(experiment);
                Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment!.LeakageAuditStatus);
                Assert.Equal("LeakageDetected", experiment.CurrentStage);
                Assert.Equal(ValidationExperimentStatus.Failed, experiment.Status);
                Assert.Null(experiment.SelectedTrialId);

                var audits = await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .GetByExperimentIdAsync(id);
                var denied = Assert.Single(audits.Where(a => a.WasDenied));
                Assert.True(denied.WasDenied);
                Assert.Equal(validationStart, DateTime.SpecifyKind(denied.RequestedStartUtc!.Value, DateTimeKind.Utc));
                Assert.Equal(0, denied.ReturnedCandleCount);
                Assert.Null(denied.MinimumReturnedTimestampUtc);
                Assert.Null(denied.MaximumReturnedTimestampUtc);
                Assert.Contains("BoundaryCrossed", denied.DenialReason ?? string.Empty, StringComparison.Ordinal);

                var trials = await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id);
                var leakageTrial = Assert.Single(trials.Where(t => t.Status == ValidationTrialStatus.LeakageFailed));
                Assert.Null(leakageTrial.Rank);
                Assert.False(
                    string.Equals(leakageTrial.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase));

                var segments = await sp.GetRequiredService<IValidationSegmentResultRepository>()
                    .GetByExperimentIdAsync(id);
                Assert.DoesNotContain(segments, s => s.SegmentType == ValidationSegmentType.Validation);
                Assert.Null(experiment.ValidationStrategyLabRunId);
            }

            var opResponse = await client.GetAsync(
                $"/api/v1/validation-lab/experiments/{id}/operation-status");
            Assert.Equal(HttpStatusCode.OK, opResponse.StatusCode);
            var opPayload = await opResponse.Content
                .ReadFromJsonAsync<ApiResponse<MomoQuant.Application.Common.ResearchOperationStatus>>(IntegrationTestJson.Options);
            Assert.NotNull(opPayload?.Data);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, opPayload!.Data!.ErrorCode);
            var opJson = await opResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("StackTrace", opJson, StringComparison.OrdinalIgnoreCase);

            var freeze = await client.PostAsync($"/api/v1/validation-lab/experiments/{id}/freeze", null);
            Assert.Equal(HttpStatusCode.BadRequest, freeze.StatusCode);
            var freezeBody = await freeze.Content.ReadAsStringAsync();
            Assert.True(
                freezeBody.Contains("Leakage", StringComparison.OrdinalIgnoreCase)
                || freezeBody.Contains("TrainingCompleted", StringComparison.OrdinalIgnoreCase)
                || freezeBody.Contains("Failed", StringComparison.OrdinalIgnoreCase),
                freezeBody);
            Assert.DoesNotContain("StackTrace", freezeBody, StringComparison.OrdinalIgnoreCase);

            var validation = await client.PostAsync(
                $"/api/v1/validation-lab/experiments/{id}/run-validation", null);
            Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
            var validationBody = await validation.Content.ReadAsStringAsync();
            Assert.Contains("ConfigurationFrozen", validationBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StackTrace", validationBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await CleanupExperimentAsync(factory, eid);
            }

            if (authUserId is long uid)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(factory, uid);
            }
        }
    }

    [Fact]
    public async Task Allowed_RunTrainingApi_PersistsAccess_LeakagePasses_TrialEligible()
    {
        await using var factory = new OrchestrationFactory(TrialSeamMode.AllowedComplete);
        var correlationId = Guid.NewGuid().ToString("N");
        long? experimentId = null;
        long? authUserId = null;

        try
        {
            var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
                factory, "m230c-ok");
            authUserId = userId;

            var id = await CreatePreparedExperimentViaApiAsync(client, factory, $"m230c-ok-{correlationId}");
            experimentId = id;

            DateTime validationStart;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var entity = await scope.ServiceProvider
                    .GetRequiredService<IValidationExperimentRepository>()
                    .GetByIdAsync(id);
                Assert.NotNull(entity);
                Assert.NotNull(entity!.ValidationStartUtc);
                validationStart = DateTime.SpecifyKind(entity.ValidationStartUtc.Value, DateTimeKind.Utc);
            }

            var trainResponse = await client.PostAsync(
                $"/api/v1/validation-lab/experiments/{id}/run-training", null);
            Assert.True(trainResponse.IsSuccessStatusCode, $"Train HTTP {(int)trainResponse.StatusCode}");

            var terminal = await PollUntilTerminalAsync(client, id, TimeSpan.FromMinutes(2));
            Assert.Equal(ValidationExperimentStatus.TrainingCompleted, terminal.Status);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                var experiment = await sp.GetRequiredService<IValidationExperimentRepository>()
                    .GetByIdAsync(id);
                Assert.NotNull(experiment);
                Assert.Equal(ValidationLeakageAuditStatus.Passed, experiment!.LeakageAuditStatus);
                Assert.NotNull(experiment.SelectedTrialId);

                var audits = await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .GetByExperimentIdAsync(id);
                var allowed = Assert.Single(audits.Where(a =>
                    !a.WasDenied
                    && (a.CallerComponent ?? string.Empty).Contains("M230C-Allowed", StringComparison.Ordinal)));
                Assert.True(allowed.ReturnedCandleCount >= 1);
                Assert.NotNull(allowed.MaximumReturnedTimestampUtc);
                Assert.True(allowed.MaximumReturnedTimestampUtc!.Value < validationStart);

                var trials = await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id);
                Assert.Contains(trials, t =>
                    t.Status == ValidationTrialStatus.Completed
                    && string.Equals(t.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(trials, t => t.Status == ValidationTrialStatus.LeakageFailed);
            }
        }
        finally
        {
            if (experimentId is long eid)
            {
                await CleanupExperimentAsync(factory, eid);
            }

            if (authUserId is long uid)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(factory, uid);
            }
        }
    }

    private static async Task<long> CreatePreparedExperimentViaApiAsync(
        HttpClient client,
        MomoQuantWebApplicationFactory factory,
        string nameSuffix)
    {
        long exchangeId;
        long symbolId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var expRepo = scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
            var reference = await expRepo.GetByIdAsync(23)
                            ?? (await expRepo.GetRecentAsync(1)).FirstOrDefault();
            if (reference is not null)
            {
                exchangeId = reference.ExchangeId;
                symbolId = reference.SymbolId;
            }
            else
            {
                var symbols = scope.ServiceProvider.GetRequiredService<ISymbolRepository>();
                var (page, _) = await symbols.GetPagedAsync(
                    new PagedRequest { Page = 1, PageSize = 20 }, null);
                var symbol = page.FirstOrDefault()
                             ?? throw new InvalidOperationException("No symbols for 230C orchestration.");
                exchangeId = symbol.ExchangeId;
                symbolId = symbol.Id;
            }
        }

        var end = DateTime.UtcNow.Date.AddDays(-1);
        var start = end.AddDays(-14);
        var createBody = new CreateValidationExperimentRequest
        {
            Name = $"VL-230C {nameSuffix}",
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            StrategyCode = MomoQuant.Domain.Constants.StrategyCodes.PriceStructureBreakoutRetest,
            StrategyVersion = "1.0.0",
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = "15m",
            RequestedStartUtc = start,
            RequestedEndUtc = end,
            SplitRatio = 0.70m,
            RequiredWarmupCandles = 20,
            MaximumTrials = 1,
            DeterministicSeed = 23003,
            AutoImportMissingCandles = true,
            ParameterSearchSpaceOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["swingLeftBarsMin"] = "1",
                ["swingLeftBarsMax"] = "1",
                ["swingLeftBarsStep"] = "1",
                ["swingRightBarsMin"] = "1",
                ["swingRightBarsMax"] = "1",
                ["swingRightBarsStep"] = "1",
                ["retestTolerancePercentMin"] = "0.3",
                ["retestTolerancePercentMax"] = "0.3",
                ["retestTolerancePercentStep"] = "0.1",
                ["maxRetestBarsMin"] = "10",
                ["maxRetestBarsMax"] = "10",
                ["maxRetestBarsStep"] = "1",
                ["fixedRewardRiskMin"] = "2",
                ["fixedRewardRiskMax"] = "2",
                ["fixedRewardRiskStep"] = "0.5",
                ["stopBufferPercentMin"] = "0.05",
                ["stopBufferPercentMax"] = "0.05",
                ["stopBufferPercentStep"] = "0.05"
            },
            QualificationProfile = new ValidationQualificationProfileDto
            {
                MinimumTrainingClosedTrades = 0,
                MinimumTrainingProfitFactor = 0m,
                MinimumTrainingNetExpectancyR = -999m,
                MaximumTrainingDrawdownPercent = 100m
            }
        };

        var create = await client.PostAsJsonAsync("/api/v1/validation-lab/experiments", createBody);
        Assert.True(create.IsSuccessStatusCode, $"Create failed: {(int)create.StatusCode}");
        var created = await create.Content
            .ReadFromJsonAsync<ApiResponse<ValidationExperimentDto>>(IntegrationTestJson.Options);
        Assert.NotNull(created?.Data);

        var prepare = await client.PostAsync(
            $"/api/v1/validation-lab/experiments/{created!.Data!.Id}/prepare-data", null);
        Assert.True(prepare.IsSuccessStatusCode, $"Prepare failed: {(int)prepare.StatusCode}");
        var prepared = await prepare.Content
            .ReadFromJsonAsync<ApiResponse<ValidationExperimentDto>>(IntegrationTestJson.Options);
        Assert.Equal(ValidationExperimentStatus.DataReady, prepared?.Data?.Status);

        return created.Data.Id;
    }

    private static async Task<ValidationExperimentDto> PollUntilTerminalAsync(
        HttpClient client,
        long experimentId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        ValidationExperimentDto? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var op = await client.GetAsync(
                $"/api/v1/validation-lab/experiments/{experimentId}/operation-status");
            _ = op;

            var detail = await client.GetAsync($"/api/v1/validation-lab/experiments/{experimentId}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var payload = await detail.Content
                .ReadFromJsonAsync<ApiResponse<ValidationExperimentDetailDto>>(IntegrationTestJson.Options);
            last = payload?.Data;
            Assert.NotNull(last);

            if (last!.Status is ValidationExperimentStatus.Failed
                or ValidationExperimentStatus.TrainingCompleted
                or ValidationExperimentStatus.Cancelled
                or ValidationExperimentStatus.Completed
                or ValidationExperimentStatus.ConfigurationFrozen)
            {
                return last;
            }

            await Task.Delay(750);
        }

        throw new TimeoutException(
            $"Experiment {experimentId} did not reach terminal status. Last={last?.Status}/{last?.CurrentStage}");
    }

    private static async Task CleanupExperimentAsync(MomoQuantWebApplicationFactory factory, long experimentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        await db.ValidationCandleAccessAudits
            .Where(a => a.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationSegmentResults
            .Where(s => s.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationParameterTrials
            .Where(t => t.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationExperimentExecutionLeases
            .Where(l => l.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ResearchOperationStatuses
            .Where(o => o.EntityId == experimentId.ToString()
                        && o.OperationType == ResearchOperationStatusCodes.ValidationTrainingType)
            .ExecuteDeleteAsync();
        await db.ValidationExperiments
            .Where(e => e.Id == experimentId)
            .ExecuteDeleteAsync();
    }

    private enum TrialSeamMode
    {
        Adversarial,
        AllowedComplete
    }

    private sealed class OrchestrationFactory : MomoQuantWebApplicationFactory
    {
        private readonly TrialSeamMode _mode;

        public OrchestrationFactory(TrialSeamMode mode) => _mode = mode;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStrategyLabRunner>();
                services.AddScoped<IStrategyLabRunner>(sp =>
                {
                    var runs = sp.GetRequiredService<IStrategyLabRunRepository>();
                    return new SeamStrategyLabRunner(runs, _mode);
                });
            });
        }
    }

    /// <summary>
    /// Legitimate DI seam: still invoked from ValidationLabService.RunTrainingAsync → ExecuteTrialAsync.
    /// Adversarial mode requests OpenTimeUtc == ValidationStartUtc through the ambient training scope.
    /// Allowed mode records an in-range access then completes the lab run without PSBR evaluation.
    /// </summary>
    private sealed class SeamStrategyLabRunner : IStrategyLabRunner
    {
        private readonly IStrategyLabRunRepository _runs;
        private readonly TrialSeamMode _mode;

        public SeamStrategyLabRunner(IStrategyLabRunRepository runs, TrialSeamMode mode)
        {
            _runs = runs;
            _mode = mode;
        }

        public Task ExecuteAsync(long runId, CancellationToken cancellationToken = default) =>
            ExecuteAsync(runId, StrategyLabExecutionContext.ForGeneralResearch(), cancellationToken);

        public async Task ExecuteAsync(
            long runId,
            StrategyLabExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(executionContext);

            if (executionContext.ExecutionPurpose != ExecutionPurpose.ValidationTraining)
            {
                throw new InvalidOperationException("230C seam runner is only for ValidationTraining tests.");
            }

            var scope = ValidationTrainingCandleScopeAmbient.Current
                        ?? throw new InvalidOperationException(
                            "Ambient IValidationTrainingCandleScope required for seam trial.");

            if (_mode == TrialSeamMode.Adversarial)
            {
                var boundary = executionContext.TrainingBoundaryUtc
                               ?? scope.ValidationBoundaryUtc;
                _ = scope.GetByOpenTimeUtc(boundary, "M230C-AdversarialTrial");
                return;
            }

            Assert.True(scope.Count > 0, "Prepared experiment must expose training candles.");
            var candle = scope[scope.Count - 1];
            Assert.True(candle.OpenTimeUtc < scope.ValidationBoundaryUtc);
            var hit = scope.GetByOpenTimeUtc(candle.OpenTimeUtc, "M230C-AllowedTrial");
            Assert.NotNull(hit);

            var run = await _runs.GetByIdAsync(runId, cancellationToken)
                      ?? throw new InvalidOperationException($"Lab run {runId} missing.");
            run.Status = StrategyLabRunStatus.Completed;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.ErrorMessage = null;
            run.ResultSummaryJson = "{}";
            await _runs.UpdateAsync(run, cancellationToken);
        }
    }
}
