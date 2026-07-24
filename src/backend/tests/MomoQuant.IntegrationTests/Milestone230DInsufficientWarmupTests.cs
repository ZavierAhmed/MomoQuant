using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>Milestone 23.0D — API path fails closed before creating trials when strategy warm-up is unavailable.</summary>
[Collection("Integration")]
public sealed class Milestone230DInsufficientWarmupTests
{
    [Fact]
    public async Task RunTrainingApi_TooFewStrategyWarmupCandles_FailsWithoutCandidates()
    {
        await using var factory = new InsufficientWarmupFactory();
        long? userId = null;
        long? experimentId = null;
        long? disposableSymbolId = null;
        var candleIds = new List<long>();

        try
        {
            var (client, disposableUserId) =
                await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(factory, "m230d-warmup");
            userId = disposableUserId;
            var sharedReference = await Milestone230DOrchestrationTests.GetReferenceSymbolAsync(factory);
            var reference = await CreateDisposableSymbolAsync(factory, sharedReference.ExchangeId);
            disposableSymbolId = reference.SymbolId;
            var requestedStart = new DateTime(2041, 1, 2, 1, 0, 0, DateTimeKind.Utc);

            // Prepare-data asks for five candles and succeeds. Production strategy requirements
            // later resolve a larger warm-up, proving the run-training boundary fails closed.
            candleIds.AddRange(await Milestone230DOrchestrationTests.SeedCandlesAsync(
                factory,
                reference.ExchangeId,
                reference.SymbolId,
                requestedStart.AddMinutes(-10 * 15),
                count: 50));
            experimentId = await Milestone230DOrchestrationTests.CreatePreparedAsync(
                client,
                reference,
                requestedStart,
                requestedStart.AddMinutes(39 * 15),
                requiredWarmup: 5);

            var response = await client.PostAsync(
                $"/api/v1/validation-lab/experiments/{experimentId}/run-training", null);
            Assert.True(
                response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest,
                await response.Content.ReadAsStringAsync());

            var terminal = await Milestone230DOrchestrationTests.PollUntilTerminalAsync(
                client,
                experimentId.Value);
            Assert.Equal(ValidationExperimentStatus.Failed, terminal.Status);
            Assert.Equal("InsufficientWarmup", terminal.CurrentStage);
            Assert.Equal(ValidationWarmupStatus.Insufficient, terminal.WarmupStatus);
            Assert.True(terminal.AvailableWarmupCandles < 100);

            await using var scope = factory.Services.CreateAsyncScope();
            var entity = await scope.ServiceProvider
                .GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(experimentId.Value);
            Assert.Equal(ValidationTrainingFailureCodes.InsufficientWarmup, entity?.PrimaryFailureReason);
            var trials = await scope.ServiceProvider
                .GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(experimentId.Value);
            Assert.Empty(trials);
            Assert.Null(terminal.SelectedTrialId);
        }
        finally
        {
            await Milestone230DOrchestrationTests.CleanupAsync(factory, experimentId, candleIds);
            if (disposableSymbolId is long symbolId)
            {
                await using var cleanup = factory.Services.CreateAsyncScope();
                var db = cleanup.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.Symbols.Where(s => s.Id == symbolId).ExecuteDeleteAsync();
            }
            if (userId is long id)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(factory, id);
            }
        }
    }

    private static async Task<(long ExchangeId, long SymbolId)> CreateDisposableSymbolAsync(
        MomoQuantWebApplicationFactory factory,
        long exchangeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var symbol = new Symbol
        {
            ExchangeId = exchangeId,
            SymbolName = $"M23D{Guid.NewGuid():N}"[..16],
            BaseAsset = "M230D",
            QuoteAsset = "TEST",
            ContractType = ContractType.Perpetual,
            PricePrecision = 2,
            QuantityPrecision = 3,
            MinQty = 0.001m,
            MinNotional = 1m,
            TickSize = 0.01m,
            StepSize = 0.001m,
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0004m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Symbols.Add(symbol);
        await db.SaveChangesAsync();
        return (exchangeId, symbol.Id);
    }

    private sealed class InsufficientWarmupFactory : MomoQuantWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStrategyExecutionRequirementsResolver>();
                services.AddScoped<IStrategyExecutionRequirementsResolver, FixedWarmupRequirementsResolver>();
            });
        }
    }

    private sealed class FixedWarmupRequirementsResolver : IStrategyExecutionRequirementsResolver
    {
        public Task<ServiceResult<StrategyExecutionRequirements>> ResolveAsync(
            ResolveStrategyExecutionRequirementsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(request.StrategyVersion));

        public Task<ServiceResult<StrategyExecutionRequirements>> ResolveByStrategyIdAsync(
            long strategyId,
            string? strategyVersion = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(strategyVersion));

        private static ServiceResult<StrategyExecutionRequirements> Result(string? version) =>
            ServiceResult<StrategyExecutionRequirements>.Ok(new StrategyExecutionRequirements
            {
                StrategyId = 1,
                StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
                StrategyVersion = version ?? "1.0.0",
                RequiredWarmupCandleCount = 100,
                RequirementsVersion = "M230D.FixedWarmup/v1"
            });
    }
}
