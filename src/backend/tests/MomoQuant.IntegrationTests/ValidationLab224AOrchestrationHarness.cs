using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Application.ValidationLab.Synthetic;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.IntegrationTests;

internal sealed class ValidationLab224AOrchestrationHarness
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ValidationLab224AOrchestrationHarness(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<(long ExperimentId, IReadOnlyList<IReadOnlyDictionary<string, string>> Combos)> CreatePreparedExperimentAsync(
        string nameSuffix,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var lab = sp.GetRequiredService<IValidationLabService>();
        var symbolsRepo = sp.GetRequiredService<ISymbolRepository>();
        var expRepo = sp.GetRequiredService<IValidationExperimentRepository>();
        var definitions = sp.GetRequiredService<IStrategyParameterDefinitionProvider>();

        long exchangeId;
        long symbolId;
        var reference = await expRepo.GetByIdAsync(23, cancellationToken);
        if (reference is null)
        {
            var recent = await expRepo.GetRecentAsync(1, cancellationToken);
            reference = recent.FirstOrDefault();
        }
        if (reference is not null)
        {
            exchangeId = reference.ExchangeId;
            symbolId = reference.SymbolId;
        }
        else
        {
            var (symbols, _) = await symbolsRepo.GetPagedAsync(
                new MomoQuant.Shared.Contracts.PagedRequest { Page = 1, PageSize = 50 },
                null,
                cancellationToken);
            var symbol = symbols.FirstOrDefault()
                ?? throw new InvalidOperationException("No symbols found for orchestration fixture.");
            exchangeId = symbol.ExchangeId;
            symbolId = symbol.Id;
        }

        var end = DateTime.UtcNow.Date.AddDays(-1);
        var start = end.AddDays(-14);

        var create = await lab.CreateExperimentAsync(new CreateValidationExperimentRequest
        {
            Name = $"VL-224A {nameSuffix} {Guid.NewGuid():N}",
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            StrategyVersion = "1.0.0",
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = "15m",
            RequestedStartUtc = start,
            RequestedEndUtc = end,
            SplitRatio = 0.70m,
            RequiredWarmupCandles = 20,
            MaximumTrials = 3,
            DeterministicSeed = ValidationLab224AIntegrityOrchestrationFixture.DeterministicSeed,
            AutoImportMissingCandles = true,
            ParameterSearchSpaceOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["swingLeftBarsMin"] = "1",
                ["swingLeftBarsMax"] = "3",
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
                MinimumTrainingClosedTrades = 1,
                MinimumTrainingProfitFactor = 0.01m,
                MinimumTrainingNetExpectancyR = -999m,
                MaximumTrainingDrawdownPercent = 100m
            }
        }, cancellationToken);

        if (!create.Succeeded || create.Data is null)
        {
            throw new InvalidOperationException(create.ErrorMessage ?? "Create experiment failed.");
        }

        var prepare = await lab.PrepareDataAsync(create.Data.Id, cancellationToken);
        if (!prepare.Succeeded)
        {
            throw new InvalidOperationException(prepare.ErrorMessage ?? "Prepare data failed.");
        }

        var combos = ValidationLab224AIntegrityOrchestrationFixture.BuildThreeTrialGrid(definitions);
        return (create.Data.Id, combos);
    }

    public async Task SeedTrialsAsync(
        long experimentId,
        IReadOnlyList<ValidationParameterTrial> trials,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var trialRepo = scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>();

        foreach (var trial in trials)
        {
            trial.ValidationExperimentId = experimentId;
            await trialRepo.AddAsync(trial, cancellationToken);
        }
    }

    public async Task<ValidationExperimentDto> RunTrainingFinalizeAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        var result = await lab.RunTrainingAsync(experimentId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Run training failed.");
        }

        return result.Data;
    }

    public async Task<ValidationExperimentDto> GetExperimentAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        var result = await lab.GetExperimentAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Get experiment failed.");
        }

        return result.Data;
    }

    public async Task<ServiceResult<ValidationExperimentDto>> TryFreezeAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        return await lab.FreezeAsync(experimentId, cancellationToken);
    }

    public async Task<ServiceResult<ValidationExperimentDetailDto>> TryRunValidationAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        return await lab.RunValidationAsync(experimentId, cancellationToken);
    }

    public async Task<ValidationSelectionIntegrityReportDto> GetSelectionIntegrityAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        var result = await lab.GetSelectionIntegrityAsync(experimentId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Selection integrity failed.");
        }

        return result.Data;
    }

    public async Task<ValidationExperiment?> GetExperimentEntityAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
        return await repo.GetByIdAsync(experimentId, cancellationToken);
    }
}
