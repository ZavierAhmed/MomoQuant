using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Research;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Application.ValidationLab.Synthetic;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

public enum E2C2RunnerMode
{
    AllowedComplete,
    AdversarialBoundary,
    ThrowingTrialFailure
}

/// <summary>Shared controllable seams for Milestone 23.0E2C2 / E2C2B orchestration tests.</summary>
public sealed class E2C2SeamControls
{
    public E2C2RunnerMode RunnerMode { get; set; } = E2C2RunnerMode.AllowedComplete;
    public bool FailAllFlushes { get; set; }
    public HashSet<int> FailOnFlushNumbers { get; } = [];
    public bool FailOperationStatusSync { get; set; }
    public bool FailLeaseRelease { get; set; }
    public bool FailLeaseHeartbeat { get; set; }
    public bool FailAuditFinalizationIncomplete { get; set; }
    public bool FailCompletenessVerification { get; set; }
    public bool ThrowOnAuditFinalizer { get; set; }
    public bool ThrowOnCompletenessVerifier { get; set; }
    public bool FailScopeDisposal { get; set; }
    public int FailExperimentUpdateCount { get; set; }

    public void Reset()
    {
        RunnerMode = E2C2RunnerMode.AllowedComplete;
        FailAllFlushes = false;
        FailOnFlushNumbers.Clear();
        FailOperationStatusSync = false;
        FailLeaseRelease = false;
        FailLeaseHeartbeat = false;
        FailAuditFinalizationIncomplete = false;
        FailCompletenessVerification = false;
        ThrowOnAuditFinalizer = false;
        ThrowOnCompletenessVerifier = false;
        FailScopeDisposal = false;
        FailExperimentUpdateCount = 0;
    }
}

public static class E2C2FailureReasonHelpers
{
    public static IReadOnlyList<ValidationTrainingFailureRecord> ParseRecords(string? json) =>
        ValidationTrainingFailureJson.ParseRecords(json);

    public static void AssertPrimaryAndOrderedCodes(
        ValidationExperiment experiment,
        params string[] expectedCodesInOrder)
    {
        Assert.Equal(expectedCodesInOrder[0], experiment.PrimaryFailureReason);
        var parsed = ParseRecords(experiment.FailureReasonsJson);
        Assert.Equal(expectedCodesInOrder.Length, parsed.Count);
        Assert.Equal(expectedCodesInOrder, parsed.Select(r => r.Code).ToArray());
    }

    public static void AssertTrialFailureState(
        ValidationParameterTrial trial,
        ValidationTrialStatus expectedStatus,
        bool rankIneligible,
        params string[] expectedRankIneligibleCodes)
    {
        Assert.Equal(expectedStatus, trial.Status);
        Assert.Equal(
            rankIneligible ? ValidationTrialRankEligibility.Ineligible : trial.TrialRankEligibility,
            trial.TrialRankEligibility);
        if (expectedRankIneligibleCodes.Length == 0)
        {
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(trial.RankIneligibleReasonsJson));
        var codes = JsonSerializer.Deserialize<string[]>(trial.RankIneligibleReasonsJson!) ?? [];
        foreach (var code in expectedRankIneligibleCodes)
        {
            Assert.Contains(code, codes);
        }
    }

    public static void AssertExactFailureReasons(
        ValidationExperiment experiment,
        params string[] expectedCodesInOrder)
    {
        AssertPrimaryAndOrderedCodes(experiment, expectedCodesInOrder);
        var parsed = ParseRecords(experiment.FailureReasonsJson);
        Assert.Equal(expectedCodesInOrder.Length, parsed.Select(r => r.LogicalIdentity).Distinct(StringComparer.Ordinal).Count());
    }

    public static void AssertExactFailureRecords(
        ValidationExperiment experiment,
        params (string Code, ValidationTrainingFailurePhase Phase)[] expectedInOrder)
    {
        Assert.Equal(expectedInOrder[0].Code, experiment.PrimaryFailureReason);
        var parsed = ParseRecords(experiment.FailureReasonsJson);
        Assert.Equal(expectedInOrder.Length, parsed.Count);
        Assert.Equal(expectedInOrder.Select(e => e.Code).ToArray(), parsed.Select(r => r.Code).ToArray());
        Assert.Equal(expectedInOrder.Select(e => e.Phase).ToArray(), parsed.Select(r => r.Phase).ToArray());
        var expectedIdentities = expectedInOrder
            .Select(e => $"{ExpectedPrecedence(e.Code)}:{e.Code}:{e.Phase}")
            .ToArray();
        Assert.Equal(expectedIdentities, parsed.Select(r => r.LogicalIdentity).ToArray());
    }

    private static int ExpectedPrecedence(string code) => code switch
    {
        ValidationTrainingFailureCodes.ValidationDataLeakage =>
            (int)ValidationTrainingFailurePrecedence.Boundary,
        ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed =>
            (int)ValidationTrainingFailurePrecedence.AuditDurability,
        ValidationTrainingFailureCodes.InsufficientWarmup or ValidationTrainingFailureCodes.TrialExecutionFailed
            or "FailedNoTrainingTrialPassedGuardrails" =>
            (int)ValidationTrainingFailurePrecedence.TrialExecution,
        ValidationTrainingFailureCodes.TrainingCleanupFailed =>
            (int)ValidationTrainingFailurePrecedence.Cleanup,
        _ => code.Contains("Leakage", StringComparison.OrdinalIgnoreCase)
            ? (int)ValidationTrainingFailurePrecedence.Boundary
            : (int)ValidationTrainingFailurePrecedence.AuditDurability
    };

    public static void AssertNoMirroredDiagnosticDuplicates(ValidationExperiment experiment)
    {
        var parsed = ParseRecords(experiment.FailureReasonsJson);
        Assert.Equal(parsed.Count, parsed.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(parsed.Count, parsed.Select(r => r.LogicalIdentity).Distinct(StringComparer.Ordinal).Count());
    }

    public static void AssertNoSensitiveMessages(
        ValidationExperiment experiment,
        string? serviceResultMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(experiment.ErrorMessage))
        {
            Assert.DoesNotContain("StackTrace", experiment.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" at MomoQuant.", experiment.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("Password=", experiment.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Server=", experiment.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var record in ParseRecords(experiment.FailureReasonsJson))
        {
            Assert.DoesNotContain("StackTrace", record.UserSafeMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" at MomoQuant.", record.UserSafeMessage, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(serviceResultMessage))
        {
            Assert.DoesNotContain("StackTrace", serviceResultMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" at MomoQuant.", serviceResultMessage, StringComparison.Ordinal);
        }
    }
}

public sealed class E2C2OrchestrationFactory : MomoQuantWebApplicationFactory
{
    public E2C2SeamControls Controls { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Controls);

            services.RemoveAll<IStrategyLabRunner>();
            services.AddScoped<IStrategyLabRunner>(sp =>
            {
                var runs = sp.GetRequiredService<IStrategyLabRunRepository>();
                var candidates = sp.GetRequiredService<IStrategyResearchCandidateRepository>();
                return new E2C2StrategyLabRunner(
                    runs,
                    candidates,
                    sp.GetRequiredService<E2C2SeamControls>());
            });

            services.RemoveAll<IValidationCandleAccessRecorder>();
            services.AddScoped<IValidationCandleAccessRecorder>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<ValidationCandleAccessRecorder>(sp);
                return new E2C2FlushFailRecorder(inner, sp.GetRequiredService<E2C2SeamControls>());
            });

            services.RemoveAll<IResearchOperationStatusService>();
            services.AddScoped<IResearchOperationStatusService>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<ResearchOperationStatusService>(sp);
                return new E2C2OperationStatusDecorator(inner, sp.GetRequiredService<E2C2SeamControls>());
            });

            services.RemoveAll<IValidationTrainingExecutionLeaseService>();
            services.AddScoped<IValidationTrainingExecutionLeaseService>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<ValidationTrainingExecutionLeaseService>(sp);
                return new E2C2LeaseDecorator(inner, sp.GetRequiredService<E2C2SeamControls>());
            });

            services.RemoveAll<IValidationAuditExecutionFinalizer>();
            services.AddScoped<IValidationAuditExecutionFinalizer>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<ValidationAuditExecutionFinalizer>(sp);
                return new E2C2FinalizerDecorator(inner, sp.GetRequiredService<E2C2SeamControls>());
            });

            services.RemoveAll<IValidationAuditCompletenessVerifier>();
            services.AddScoped<IValidationAuditCompletenessVerifier>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<ValidationAuditCompletenessVerifier>(sp);
                return new E2C2CompletenessVerifierDecorator(inner, sp.GetRequiredService<E2C2SeamControls>());
            });

            services.RemoveAll<IValidationTrainingScopeExecution>();
            services.AddScoped<IValidationTrainingScopeExecution>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<ValidationTrainingScopeExecution>(sp);
                return new E2C2ScopeExecutionDecorator(inner, sp.GetRequiredService<E2C2SeamControls>());
            });
        });
    }
}

internal static class E2C2ExperimentFactory
{
    public static async Task<(long ExperimentId, IReadOnlyDictionary<string, string> Combo)> CreatePreparedSingleTrialExperimentAsync(
        MomoQuantWebApplicationFactory factory,
        string suffix)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var lab = sp.GetRequiredService<IValidationLabService>();
        var expRepo = sp.GetRequiredService<IValidationExperimentRepository>();
        var symbolsRepo = sp.GetRequiredService<ISymbolRepository>();
        var definitions = sp.GetRequiredService<IStrategyParameterDefinitionProvider>();

        long exchangeId;
        long symbolId;
        var reference = await expRepo.GetByIdAsync(23) ?? (await expRepo.GetRecentAsync(1)).FirstOrDefault();
        if (reference is not null)
        {
            exchangeId = reference.ExchangeId;
            symbolId = reference.SymbolId;
        }
        else
        {
            var (symbols, _) = await symbolsRepo.GetPagedAsync(
                new PagedRequest { Page = 1, PageSize = 20 }, null);
            var symbol = symbols.First();
            exchangeId = symbol.ExchangeId;
            symbolId = symbol.Id;
        }

        var end = DateTime.UtcNow.Date.AddDays(-1);
        var start = end.AddDays(-14);
        var create = await lab.CreateExperimentAsync(new CreateValidationExperimentRequest
        {
            Name = $"VL-E2C2 {suffix} {Guid.NewGuid():N}",
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
            MaximumTrials = 1,
            DeterministicSeed = 23032,
            AutoImportMissingCandles = true,
            ParameterSearchSpaceOverrides = BuildSingleTrialOverrides(),
            QualificationProfile = new ValidationQualificationProfileDto
            {
                MinimumTrainingClosedTrades = 0,
                MinimumTrainingProfitFactor = 0m,
                MinimumTrainingNetExpectancyR = -999m,
                MaximumTrainingDrawdownPercent = 100m
            }
        });
        if (!create.Succeeded || create.Data is null)
        {
            throw new InvalidOperationException(create.ErrorMessage ?? "Create failed.");
        }

        var prepare = await lab.PrepareDataAsync(create.Data.Id);
        if (!prepare.Succeeded)
        {
            throw new InvalidOperationException(prepare.ErrorMessage ?? "Prepare failed.");
        }

        var combos = BuildSingleTrialCombo(definitions);
        return (create.Data.Id, combos);
    }

    public static Dictionary<string, string> BuildSingleTrialOverrides() =>
        new(StringComparer.OrdinalIgnoreCase)
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
        };

    public static IReadOnlyDictionary<string, string> BuildSingleTrialCombo(IStrategyParameterDefinitionProvider definitions)
    {
        var grid = ValidationLab224AIntegrityOrchestrationFixture.BuildThreeTrialGrid(definitions);
        return grid[0];
    }

    public static async Task CleanupExperimentAsync(MomoQuantWebApplicationFactory factory, long experimentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var auditIds = await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId)
            .Select(e => e.AuditExecutionId)
            .ToListAsync();
        if (auditIds.Count > 0)
        {
            await db.ValidationAuditBatches
                .Where(b => auditIds.Contains(b.AuditExecutionId))
                .ExecuteDeleteAsync();
        }

        await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
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
            .Where(o => o.EntityId == experimentId.ToString())
            .ExecuteDeleteAsync();
        await db.ValidationExperiments
            .Where(e => e.Id == experimentId)
            .ExecuteDeleteAsync();
    }
}

internal sealed class E2C2StrategyLabRunner : IStrategyLabRunner
{
    private readonly IStrategyLabRunRepository _runs;
    private readonly IStrategyResearchCandidateRepository _candidates;
    private readonly E2C2SeamControls _controls;

    public E2C2StrategyLabRunner(
        IStrategyLabRunRepository runs,
        IStrategyResearchCandidateRepository candidates,
        E2C2SeamControls controls)
    {
        _runs = runs;
        _candidates = candidates;
        _controls = controls;
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
            throw new InvalidOperationException("E2C2 seam runner is only for ValidationTraining tests.");
        }

        var scope = ValidationTrainingCandleScopeAmbient.Current
                    ?? throw new InvalidOperationException("Ambient training scope required.");

        switch (_controls.RunnerMode)
        {
            case E2C2RunnerMode.AdversarialBoundary:
            {
                var boundary = executionContext.TrainingBoundaryUtc ?? scope.ValidationBoundaryUtc;
                throw new ValidationDataLeakageException(
                    scope.ValidationExperimentId,
                    boundary,
                    "M230E2C2-Adversarial",
                    boundary,
                    null,
                    "ValidationDataLeakageDetected");
            }
            case E2C2RunnerMode.ThrowingTrialFailure:
                throw new InvalidOperationException("E2C2 controlled trial execution failure.");
            default:
                await CompleteAllowedRunAsync(runId, scope, cancellationToken);
                break;
        }
    }

    private async Task CompleteAllowedRunAsync(
        long runId,
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken)
    {
        var range = scope.GetEvaluationRange(
            scope.SegmentStartUtc,
            scope.SegmentEndExclusiveUtc,
            ValidationCandleAccessContext.Create("M230E2C2-Allowed", ValidationCandleAccessPurpose.EvaluationRange));
        Assert.NotEmpty(range);
        var candle = range[0];
        _ = scope.GetByOpenTimeUtc(candle.OpenTimeUtc, "M230E2C2-Allowed");

        var run = await _runs.GetByIdAsync(runId, cancellationToken)
                  ?? throw new InvalidOperationException($"Lab run {runId} missing.");
        run.Status = StrategyLabRunStatus.Completed;
        run.CompletedAtUtc = DateTime.UtcNow;
        run.ErrorMessage = null;
        run.ResultSummaryJson = "{}";
        await _runs.UpdateAsync(run, cancellationToken);

        var entry = candle.Close;
        var stop = entry * 0.99m;
        var exit = entry * 1.02m;
        var setupTime = candle.OpenTimeUtc;
        var exitTime = setupTime.AddMinutes(15);
        await _candidates.AddRangeAsync(
            [
                new StrategyResearchCandidate
                {
                    StrategyLabRunId = run.Id,
                    StrategyCode = run.StrategyCode,
                    StrategyVersion = run.StrategyVersion ?? "1.0.0",
                    ExchangeId = run.ExchangeId,
                    SymbolId = run.SymbolId,
                    Symbol = run.Symbol,
                    Timeframe = run.Timeframe,
                    Direction = TradeDirection.Long,
                    SetupDetectedAtUtc = setupTime,
                    ProposedEntryTimeUtc = setupTime,
                    ProposedEntryPrice = entry,
                    StopLoss = stop,
                    Target1 = exit,
                    RewardRisk = 2m,
                    CandidateStatus = StrategyResearchCandidateStatus.Closed,
                    RawOutcomeStatus = RawOutcomeStatus.Winner,
                    RawExitTimeUtc = exitTime,
                    RawExitPrice = exit,
                    ProposedPositionSize = 1m,
                    RiskAmount = entry - stop,
                    SetupFingerprint = $"m230e2c2-{run.Id}",
                    StrategyReason = "M230E2C2-AllowedSeam",
                    ParametersJson = "{}",
                    StructureJson = "{}"
                }
            ],
            cancellationToken);
    }
}

internal sealed class E2C2FlushFailRecorder : IValidationCandleAccessRecorder
{
    private readonly IValidationCandleAccessRecorder _inner;
    private readonly E2C2SeamControls _controls;
    private int _flushCount;

    public E2C2FlushFailRecorder(IValidationCandleAccessRecorder inner, E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public int FlushCount => _flushCount;

    public async Task<ValidationAccessBatchPersistResult> FlushAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken = default)
    {
        if (scope.BoundAuditExecutionId is null)
        {
            return await _inner.FlushAsync(scope, cancellationToken);
        }

        _flushCount++;
        if (_controls.FailAllFlushes || _controls.FailOnFlushNumbers.Contains(_flushCount))
        {
            throw CreatePersistenceException();
        }

        return await _inner.FlushAsync(scope, cancellationToken);
    }

    private static ValidationAccessEvidencePersistenceException CreatePersistenceException()
    {
        var eventId = Guid.NewGuid();
        return new ValidationAccessEvidencePersistenceException(new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = [eventId],
            MissingEventIds = [eventId],
            CommitStatus = ValidationAccessBatchCommitStatus.FailedPermanent,
            VerificationStatus = ValidationAccessBatchVerificationStatus.FailedPermanent,
            RecoveryStatus = ValidationAccessBatchRecoveryStatus.RetryExhausted,
            CompletedAtUtc = DateTime.UtcNow
        });
    }
}

internal sealed class E2C2OperationStatusDecorator : IResearchOperationStatusService
{
    private readonly IResearchOperationStatusService _inner;
    private readonly E2C2SeamControls _controls;

    public E2C2OperationStatusDecorator(IResearchOperationStatusService inner, E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public Task<ResearchOperationStatus?> GetByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        _inner.GetByOperationIdAsync(operationId, cancellationToken);

    public Task<ResearchOperationStatus?> GetForValidationExperimentAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        _inner.GetForValidationExperimentAsync(experimentId, cancellationToken);

    public Task<ResearchOperationStatus> UpsertValidationTrainingAsync(
        ResearchOperationStatus status,
        CancellationToken cancellationToken = default) =>
        _inner.UpsertValidationTrainingAsync(status, cancellationToken);

    public Task<ResearchOperationStatus> SyncFromValidationTrainingAsync(
        long experimentId,
        string status,
        string stage,
        ValidationTrainingProgressDto progress,
        string? leaseOwner = null,
        string? correlationId = null,
        string? errorCode = null,
        string? userSafeError = null,
        CancellationToken cancellationToken = default)
    {
        if (_controls.FailOperationStatusSync)
        {
            throw new InvalidOperationException("E2C2 simulated operation-status sync failure.");
        }

        return _inner.SyncFromValidationTrainingAsync(
            experimentId,
            status,
            stage,
            progress,
            leaseOwner,
            correlationId,
            errorCode,
            userSafeError,
            cancellationToken);
    }

    public Task<ServiceResult<ResearchOperationStatus>> AdvanceProgressAsync(
        string operationId,
        decimal percentComplete,
        int completedWorkCount,
        int failedWorkCount,
        string? stage = null,
        string? status = null,
        string? activeWorkItem = null,
        CancellationToken cancellationToken = default) =>
        _inner.AdvanceProgressAsync(
            operationId,
            percentComplete,
            completedWorkCount,
            failedWorkCount,
            stage,
            status,
            activeWorkItem,
            cancellationToken);

    public Task<ServiceResult<ResearchOperationStatus>> HeartbeatAsync(
        string operationId,
        string leaseOwner,
        CancellationToken cancellationToken = default) =>
        _inner.HeartbeatAsync(operationId, leaseOwner, cancellationToken);

    public Task<ResearchOperationStatus?> DetectAndMarkStaleAsync(
        string operationId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default) =>
        _inner.DetectAndMarkStaleAsync(operationId, staleAfter, cancellationToken);

    public Task<ServiceResult<ResearchOperationStatus>> CancelAsync(
        string operationId,
        string callerIdentity,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default) =>
        _inner.CancelAsync(operationId, callerIdentity, callerIsAdmin, cancellationToken);
}

internal sealed class E2C2LeaseDecorator : IValidationTrainingExecutionLeaseService
{
    private readonly IValidationTrainingExecutionLeaseService _inner;
    private readonly E2C2SeamControls _controls;

    public E2C2LeaseDecorator(IValidationTrainingExecutionLeaseService inner, E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public Task<(bool Acquired, string? ConflictMessage)> TryAcquireAsync(
        long experimentId,
        string leaseOwner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default) =>
        _inner.TryAcquireAsync(experimentId, leaseOwner, ttl, cancellationToken);

    public Task<ValidationLeaseOperationResult> HeartbeatAsync(
        long experimentId,
        string leaseOwner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (_controls.FailLeaseHeartbeat)
        {
            throw new InvalidOperationException("E2C2 simulated lease heartbeat failure.");
        }

        return _inner.HeartbeatAsync(experimentId, leaseOwner, ttl, cancellationToken);
    }

    public Task<ValidationLeaseOperationResult> ReleaseAsync(
        long experimentId,
        string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        if (_controls.FailLeaseRelease)
        {
            throw new OperationCanceledException("E2C2 simulated lease release failure.");
        }

        return _inner.ReleaseAsync(experimentId, leaseOwner, cancellationToken);
    }

    public Task<bool> IsActiveAsync(long experimentId, CancellationToken cancellationToken = default) =>
        _inner.IsActiveAsync(experimentId, cancellationToken);
}

internal sealed class E2C2FinalizerDecorator : IValidationAuditExecutionFinalizer
{
    private readonly IValidationAuditExecutionFinalizer _inner;
    private readonly E2C2SeamControls _controls;

    public E2C2FinalizerDecorator(IValidationAuditExecutionFinalizer inner, E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public async Task<ValidationAuditExecutionCompletionResult> CompleteAsync(
        Guid auditExecutionId,
        long finalExpectedSequence,
        CancellationToken cancellationToken = default)
    {
        if (_controls.ThrowOnAuditFinalizer)
        {
            throw new InvalidOperationException("E2C2 simulated generic finalizer exception.");
        }

        if (_controls.FailAuditFinalizationIncomplete)
        {
            return new ValidationAuditExecutionCompletionResult
            {
                AuditExecutionId = auditExecutionId,
                IsComplete = false,
                CompletionCode = ValidationAuditCompletenessCode.FinalSequenceMissing,
                FinalExpectedSequence = finalExpectedSequence,
                FailureCode = ValidationAuditCompletenessCode.FinalSequenceMissing.ToString()
            };
        }

        return await _inner.CompleteAsync(auditExecutionId, finalExpectedSequence, cancellationToken);
    }
}

internal sealed class E2C2CompletenessVerifierDecorator : IValidationAuditCompletenessVerifier
{
    private readonly IValidationAuditCompletenessVerifier _inner;
    private readonly E2C2SeamControls _controls;

    public E2C2CompletenessVerifierDecorator(
        IValidationAuditCompletenessVerifier inner,
        E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public ValidationAuditCompletenessResult Verify(
        ValidationParameterTrial trial,
        ValidationAuditExecution? execution,
        IReadOnlyList<ValidationAuditBatch> batches,
        IReadOnlyList<ValidationCandleAccessAudit> accessRowsForScope)
    {
        if (_controls.ThrowOnCompletenessVerifier)
        {
            throw new InvalidOperationException("E2C2 simulated generic verifier exception.");
        }

        if (_controls.FailCompletenessVerification)
        {
            return new ValidationAuditCompletenessResult
            {
                AuditExecutionId = execution?.AuditExecutionId,
                IsAuthoritative = true,
                IsTerminal = true,
                IsComplete = false,
                EvidenceSatisfied = false,
                CompletionCode = ValidationAuditCompletenessCode.SequenceGap,
                LastConfirmedSequence = execution?.LastConfirmedSequence ?? 0
            };
        }

        return _inner.Verify(trial, execution, batches, accessRowsForScope);
    }
}

internal sealed class E2C2ScopeExecutionDecorator : IValidationTrainingScopeExecution
{
    private readonly IValidationTrainingScopeExecution _inner;
    private readonly E2C2SeamControls _controls;

    public E2C2ScopeExecutionDecorator(IValidationTrainingScopeExecution inner, E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public async Task<ValidationTrainingScopeExecutionResult> ExecuteWithScopeAsync(
        ValidationExperiment experiment,
        ValidationTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ExecuteWithScopeAsync(experiment, scopeRequest, body, cancellationToken);
        if (!_controls.FailScopeDisposal || scopeRequest.BoundAuditExecutionId is null)
        {
            return result;
        }

        return new ValidationTrainingScopeExecutionResult
        {
            BodyException = result.BodyException,
            FlushException = result.FlushException,
            DisposalException = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                new InvalidOperationException("E2C2 simulated outer scope disposal failure.")),
            BodyPhase = result.BodyPhase,
            FlushPhase = result.FlushPhase,
            FlushAttempted = result.FlushAttempted
        };
    }

    public Task<ValidationTrainingScopeExecutionResult> ExecuteTrialAsync(
        IValidationTrainingCandleScope scope,
        int trialNumber,
        long? trialId,
        Func<Task> trialBody,
        CancellationToken cancellationToken = default) =>
        _inner.ExecuteTrialAsync(scope, trialNumber, trialId, trialBody, cancellationToken);
}

internal sealed class E2C2ExperimentRepositoryDecorator : IValidationExperimentRepository
{
    private readonly IValidationExperimentRepository _inner;
    private readonly E2C2SeamControls _controls;

    public E2C2ExperimentRepositoryDecorator(IValidationExperimentRepository inner, E2C2SeamControls controls)
    {
        _inner = inner;
        _controls = controls;
    }

    public Task<ValidationExperiment?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<ValidationExperiment>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _inner.GetRecentAsync(limit, cancellationToken);

    public Task<IReadOnlyList<ValidationExperiment>> GetByStrategyFingerprintOverlapAsync(
        string strategyCode,
        string strategyVersion,
        string symbol,
        string timeframe,
        CancellationToken cancellationToken = default) =>
        _inner.GetByStrategyFingerprintOverlapAsync(
            strategyCode, strategyVersion, symbol, timeframe, cancellationToken);

    public Task AddAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default) =>
        _inner.AddAsync(experiment, cancellationToken);

    public Task UpdateAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default)
    {
        if (_controls.FailExperimentUpdateCount > 0)
        {
            _controls.FailExperimentUpdateCount--;
            throw new InvalidOperationException("E2C2 simulated experiment persistence failure.");
        }

        return _inner.UpdateAsync(experiment, cancellationToken);
    }
}
