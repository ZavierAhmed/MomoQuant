using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Synthetic;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public class ValidationLab224AActivationTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;
    private readonly ValidationLab224AOrchestrationHarness _harness;

    public ValidationLab224AActivationTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
        _harness = new ValidationLab224AOrchestrationHarness(factory.Services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task NewExperiment_DefaultsToV13AndIntegrityVersions()
    {
        var (id, _) = await _harness.CreatePreparedExperimentAsync("default-versions");
        var entity = await _harness.GetExperimentEntityAsync(id);
        Assert.NotNull(entity);
        Assert.Equal(ValidationMetricsContract.VersionV131, entity!.ValidationMetricsVersion);
        Assert.Equal(ValidationRiskBasisService.Version, entity.RiskBasisVersion);
        Assert.Equal(ValidationParameterFingerprintService.Version, entity.ParameterFingerprintVersion);
        Assert.Equal("ValidationSelectionIntegrity/v1", entity.SelectionIntegrityVersion);
    }

    [Fact]
    public async Task AllRejected_Orchestration_NoSelection_NoFreeze_NoValidation()
    {
        var (id, combos) = await _harness.CreatePreparedExperimentAsync("all-rejected");
        var seeds = ValidationLab224AIntegrityOrchestrationFixture.SeedAllRejected(id, combos);
        await _harness.SeedTrialsAsync(id, seeds);

        var trained = await _harness.RunTrainingFinalizeAsync(id);
        var entity = await _harness.GetExperimentEntityAsync(id);

        Assert.Equal(ValidationExperimentStatus.Failed, trained.Status);
        Assert.Equal(StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails, trained.StrategyRobustnessDecision);
        Assert.Equal(ValidationSelectionIntegrityStatus.FailedNoEligibleTrials, trained.SelectionIntegrityStatus);
        Assert.Null(entity!.SelectedTrialId);
        Assert.Null(entity.SelectedTrialParameterSnapshotJson);
        Assert.Null(entity.FrozenStrategyParameterSnapshotJson);
        Assert.Null(entity.FrozenParameterFingerprint);
        Assert.Null(entity.ValidationStrategyLabRunId);

        var freeze = await _harness.TryFreezeAsync(id);
        Assert.False(freeze.Succeeded);

        var validation = await _harness.TryRunValidationAsync(id);
        Assert.False(validation.Succeeded);
        Assert.True(
            (validation.ErrorMessage ?? string.Empty).Contains("ValidationStartedWithoutEligibleTrainingWinner", StringComparison.Ordinal)
            || (validation.ErrorMessage ?? string.Empty).Contains("ConfigurationFrozen", StringComparison.Ordinal),
            validation.ErrorMessage);
    }

    [Fact]
    public async Task OneEligible_Orchestration_FreezesAndStartsValidation()
    {
        var (id, combos) = await _harness.CreatePreparedExperimentAsync("one-eligible");
        var seeds = ValidationLab224AIntegrityOrchestrationFixture.SeedOneEligible(id, combos, eligibleIndex: 1);
        await _harness.SeedTrialsAsync(id, seeds);

        var trained = await _harness.RunTrainingFinalizeAsync(id);
        Assert.Equal(ValidationExperimentStatus.TrainingCompleted, trained.Status);
        Assert.Equal(ValidationSelectionIntegrityStatus.Passed, trained.SelectionIntegrityStatus);
        Assert.NotNull(trained.SelectedTrialId);

        var entityBeforeFreeze = await _harness.GetExperimentEntityAsync(id);
        Assert.False(string.IsNullOrWhiteSpace(entityBeforeFreeze!.SelectedTrialParameterSnapshotJson));
        Assert.False(string.IsNullOrWhiteSpace(entityBeforeFreeze.SelectedTrialParameterFingerprint));

        var freeze = await _harness.TryFreezeAsync(id);
        Assert.True(freeze.Succeeded, freeze.ErrorMessage);
        Assert.NotNull(freeze.Data!.FrozenParameterFingerprint);
        Assert.Equal(
            entityBeforeFreeze.SelectedTrialParameterFingerprint,
            freeze.Data.FrozenParameterFingerprint);

        var afterFreeze = await _harness.GetExperimentEntityAsync(id);
        Assert.Equal(FrozenSnapshotValidationStatus.Valid, afterFreeze!.FrozenSnapshotValidationStatus);
        Assert.Equal(
            afterFreeze.SelectedTrialParameterFingerprint,
            afterFreeze.FrozenParameterFingerprint);

        var integrity = await _harness.GetSelectionIntegrityAsync(id);
        Assert.Equal(ValidationSelectionIntegrityStatus.Passed, integrity.Status);
        Assert.True(integrity.FingerprintsMatch);

        var validation = await _harness.TryRunValidationAsync(id);
        Assert.True(validation.Succeeded, validation.ErrorMessage);
        Assert.NotNull(validation.Data!.ValidationStrategyLabRunId);
    }

    [Fact]
    public async Task MultipleEligible_Orchestration_DeterministicWinner()
    {
        var (id1, combos1) = await _harness.CreatePreparedExperimentAsync("multi-a");
        var seeds1 = ValidationLab224AIntegrityOrchestrationFixture.SeedMultipleEligible(id1, combos1);
        await _harness.SeedTrialsAsync(id1, seeds1);
        var run1 = await _harness.RunTrainingFinalizeAsync(id1);

        var (id2, combos2) = await _harness.CreatePreparedExperimentAsync("multi-b");
        var seeds2 = ValidationLab224AIntegrityOrchestrationFixture.SeedMultipleEligible(id2, combos2);
        await _harness.SeedTrialsAsync(id2, seeds2);
        var run2 = await _harness.RunTrainingFinalizeAsync(id2);

        Assert.Equal(run1.SelectedTrialParameterFingerprint, run2.SelectedTrialParameterFingerprint);
        Assert.Equal(ValidationSelectionIntegrityStatus.Passed, run1.SelectionIntegrityStatus);
    }

    [Fact]
    public async Task SelectionIntegrity_And_MetricBasis_EndpointsRespond()
    {
        long? authUserId = null;
        try
        {
            var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
                _factory, "m224a-admin");
            authUserId = userId;
            var (id, combos) = await _harness.CreatePreparedExperimentAsync("api-endpoints");
            var seeds = ValidationLab224AIntegrityOrchestrationFixture.SeedOneEligible(id, combos);
            await _harness.SeedTrialsAsync(id, seeds);
            await _harness.RunTrainingFinalizeAsync(id);

            var integrity = await client.GetAsync($"/api/v1/validation-lab/experiments/{id}/selection-integrity");
            Assert.Equal(HttpStatusCode.OK, integrity.StatusCode);

            var audit = await client.GetAsync($"/api/v1/validation-lab/experiments/{id}/metric-basis-audit");
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        }
        finally
        {
            if (authUserId is long uid)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(_factory, uid);
            }
        }
    }

    [Fact]
    public async Task HistoricalExperiments_22_23_24_RemainV12IfPresent()
    {
        foreach (var expId in new long[] { 22, 23, 24 })
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<MomoQuant.Application.Abstractions.IValidationExperimentRepository>();
            var exp = await repo.GetByIdAsync(expId);
            if (exp is null)
            {
                continue;
            }

            Assert.Equal(ValidationMetricsContract.VersionV12, exp.ValidationMetricsVersion);
        }
    }
}

[Collection("Integration")]
public class ValidationLab224AMetricOrchestrationTests
{
    [Fact]
    public void V13_MetricFixture_ReconcilesExactly()
    {
        var risk = new ValidationRiskBasisService();
        var winner = Cand("W", 100m, 99m, 2m, 1.8m);
        var loser = Cand("L", 100m, 99m, -1m, -1.2m);

        var wBasis = risk.ComputeTradeBasis(winner, ValidationLayerType.RawStrategy);
        var lBasis = risk.ComputeTradeBasis(loser, ValidationLayerType.RawStrategy);

        Assert.Equal(1m, wBasis.DerivedRiskAmount);
        Assert.Equal(2m, wBasis.GrossRMultiple);
        Assert.Equal(1.8m, wBasis.NetRMultiple);
        Assert.Equal(-1m, lBasis.GrossRMultiple);
        Assert.Equal(-1.2m, lBasis.NetRMultiple);

        var hundred = Enumerable.Range(1, 100).Select(i => Cand($"T{i:D3}", 100m, 99m, -0.10m, -0.15m)).ToList();
        var metrics = ValidationMetricsContract.FromCandidatesV13(
            hundred, 1000, 0, ValidationLayerType.RawStrategy, risk);
        Assert.Equal(-0.10m, metrics.GrossExpectancyR);
        Assert.Equal(-0.15m, metrics.NetExpectancyR);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.NetExpectancyApplicability);
    }

    [Fact]
    public void InvalidRiskBasis_ProducesNullNetExpectancy()
    {
        var risk = new ValidationRiskBasisService();
        var bad = Enumerable.Range(1, 5).Select(i => Cand($"B{i}", 100m, 99m, -1m, -1.2m, 0.49m)).ToList();
        var metrics = ValidationMetricsContract.FromCandidatesV13(
            bad, 500, 0, ValidationLayerType.RawStrategy, risk);
        Assert.Null(metrics.NetExpectancyR);
        Assert.Equal(ValidationMetricApplicability.InvalidRiskBasis, metrics.NetExpectancyApplicability);
    }

    private static StrategyResearchCandidate Cand(
        string fp, decimal entry, decimal stop, decimal gross, decimal net, decimal? risk = null) => new()
    {
        SetupFingerprint = fp,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = gross >= 0 ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
        ProposedEntryPrice = entry,
        StopLoss = stop,
        ProposedEntryTimeUtc = DateTime.UtcNow,
        SetupDetectedAtUtc = DateTime.UtcNow,
        RawGrossPnl = gross,
        RawNetPnl = net,
        RiskAmount = risk
    };
}
