using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;
using MySqlConnector;

namespace MomoQuant.AuditRestartHarness;

/// <summary>
/// Process-level crash/recovery harness for Milestone 23.0E2C1.
/// Phase write: create durable state up to a crash point, then Environment.Exit(42).
/// Phase recover: load durable state only, run recovery/finalizer, emit JSON result.
/// </summary>
public static class Program
{
    private const int CrashExitCode = 42;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = HarnessArgs.Parse(args);
            EnsureTestDatabase(options.Connection);

            await using var provider = BuildServices(options.Connection);
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            return options.Phase switch
            {
                "write" => await RunWriteAsync(sp, options),
                "recover" => await RunRecoverAsync(sp, options),
                _ => Fail($"Unknown phase '{options.Phase}'. Use write|recover.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task<int> RunWriteAsync(IServiceProvider sp, HarnessArgs options)
    {
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var batches = sp.GetRequiredService<IValidationAuditBatchRepository>();
        var audits = sp.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var hasher = sp.GetRequiredService<IValidationAuditPayloadSetHasher>();

        var fixture = await EnsureFixtureAsync(db, options.FixtureId);

        var now = DateTime.UtcNow;
        const string writeLeaseOwner = "harness-write-owner";
        var execution = new ValidationAuditExecution
        {
            AuditExecutionId = options.FixtureId,
            ValidationExperimentId = fixture.Experiment.Id,
            ValidationTrialId = fixture.Trial.Id,
            TrialNumber = fixture.Trial.TrialNumber,
            ScopeExecutionId = CreateScopeId(options.FixtureId),
            AttemptNumber = 1,
            ExecutionToken = "harness-token",
            LeaseOwner = writeLeaseOwner,
            Status = ValidationAuditExecutionStatus.InProgress,
            StartedAtUtc = now,
            UpdatedAtUtc = now,
            RecoveryStatus = ValidationAuditRecoveryStatus.None,
            LastConfirmedSequence = 0,
            ConfirmedEventCount = 0,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
            RowVersion = 1
        };

        // Clean prior fixture rows for idempotent re-runs.
        await CleanupFixtureAsync(db, options.FixtureId, fixture.Experiment.Id);

        fixture.Trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        fixture.Trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
        fixture.Trial.AuditAttemptNumber = 1;
        db.ValidationParameterTrials.Update(fixture.Trial);
        db.ValidationAuditExecutions.Add(execution);
        await db.SaveChangesAsync();

        int? inMemoryAccessCountBeforeCrash = null;
        if (options.CrashPoint == "AfterAuditExecutionCreatedBeforeFirstFlush")
        {
            var segmentStart = now.AddDays(-2);
            var boundary = now;
            var trainingScope = new HarnessBoundTrainingScope(
                fixture.Experiment.Id,
                execution.ScopeExecutionId,
                execution.AuditExecutionId,
                fixture.Trial.Id,
                segmentStart,
                boundary);

            using (ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
            {
                AuditExecutionId = execution.AuditExecutionId,
                ScopeExecutionId = execution.ScopeExecutionId,
                ExecutionToken = execution.ExecutionToken,
                AttemptNumber = execution.AttemptNumber,
                ValidationExperimentId = fixture.Experiment.Id,
                ValidationTrialId = fixture.Trial.Id
            }))
            {
                trainingScope.RecordEvaluationAccess("AuditRestartHarness");
            }

            if (trainingScope.AccessLog.Count < 1)
            {
                return Fail("Crash-before-flush phase must record at least one in-memory access event.");
            }

            inMemoryAccessCountBeforeCrash = trainingScope.AccessLog.Count;
            await WriteAccessHintAsync(options, inMemoryAccessCountBeforeCrash.Value);
            WriteStateHint(options, execution, batchCount: 0, eventCount: 0);
            Console.Error.WriteLine($"InMemoryAccessCount={inMemoryAccessCountBeforeCrash}");
            Environment.Exit(CrashExitCode);
        }

        var eventId = CreateEventId(options.FixtureId);
        var access = new ValidationCandleAccessAudit
        {
            AccessEventId = eventId,
            ScopeExecutionId = execution.ScopeExecutionId,
            ScopeSequenceNumber = 1,
            ValidationExperimentId = fixture.Experiment.Id,
            TrialNumber = 1,
            CallerComponent = "AuditRestartHarness",
            AccessPurpose = "EvaluationRange",
            DatasetPartition = "Training",
            CandleContentFingerprint = "HARNESS01",
            AccessedAtUtc = now,
            ReturnedCandleCount = 1,
            FlushAttemptCount = 1,
            PersistedAtUtc = now,
            RecorderVersion = "ValidationCandleAccess/v2",
            CreatedAtUtc = now
        };
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();
        var hash = canonicalizer.ComputeSha256(access);
        access.AccessPayloadHash = hash;
        access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = hasher.ComputeSetHash(entries);
        var (idsJson, hashesJson) = hasher.BuildManifestJsons(entries);

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = CreateBatchId(options.FixtureId),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = idsJson,
            ExpectedPayloadHashesJson = hashesJson,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Created,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
            RowVersion = 1
        };
        await batches.AddAsync(batch);
        execution.Status = ValidationAuditExecutionStatus.FlushManifested;
        execution.UpdatedAtUtc = DateTime.UtcNow;
        await executions.UpdateAsync(execution);

        if (options.CrashPoint == "AfterBatchManifestCreatedBeforeEventWrite")
        {
            WriteStateHint(options, execution, batchCount: 1, eventCount: 0);
            Environment.Exit(CrashExitCode);
        }

        var persist = await audits.AddRangeIdempotentByAccessEventIdAsync([access]);
        if (!persist.IsFullyConfirmed)
        {
            return Fail("Event persist was not fully confirmed.");
        }

        if (options.CrashPoint == "AfterEventCommitBeforeCursorAdvance")
        {
            // Manifest Created, event durable, cursor still 0 / batch unconfirmed.
            WriteStateHint(options, execution, batchCount: 1, eventCount: 1);
            Environment.Exit(CrashExitCode);
        }

        batch.Status = ValidationAuditBatchStatus.Confirmed;
        batch.ConfirmedAtUtc = DateTime.UtcNow;
        batch.UpdatedAtUtc = batch.ConfirmedAtUtc.Value;
        batch.ConfirmationAttemptCount = 1;
        batch.RowVersion++;
        await batches.UpdateAsync(batch);

        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
        execution.UpdatedAtUtc = DateTime.UtcNow;
        execution.RowVersion++;
        await executions.UpdateAsync(execution);

        if (options.CrashPoint == "AfterEventsConfirmedBeforeExecutionCompleted")
        {
            WriteStateHint(options, execution, batchCount: 1, eventCount: 1);
            Environment.Exit(CrashExitCode);
        }

        return Fail($"Unhandled crash-point '{options.CrashPoint}'.");
    }

    private static async Task<int> RunRecoverAsync(IServiceProvider sp, HarnessArgs options)
    {
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var recovery = sp.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var finalizer = sp.GetRequiredService<IValidationAuditExecutionFinalizer>();
        var verifier = sp.GetRequiredService<IValidationAuditCompletenessVerifier>();
        var batches = sp.GetRequiredService<IValidationAuditBatchRepository>();
        var audits = sp.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var trials = sp.GetRequiredService<IValidationParameterTrialRepository>();

        var execution = await executions.GetByAuditExecutionIdAsync(options.FixtureId);
        if (execution is null)
        {
            return Fail($"Audit execution {options.FixtureId} not found for recovery.");
        }

        var batchListBefore = await batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
        var accessRowsBefore = (await audits.GetByExperimentIdAsync(execution.ValidationExperimentId))
            .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
            .ToList();
        var trialListBefore = await trials.GetByExperimentIdAsync(execution.ValidationExperimentId);
        var trialBefore = trialListBefore.First(t => t.Id == execution.ValidationTrialId);
        var completenessBefore = verifier.Verify(trialBefore, execution, batchListBefore, accessRowsBefore);
        var beforeRecoveryExecutionStatus = execution.Status;
        var beforeRecoveryFinalExpectedSequence = execution.FinalExpectedSequence;

        var recoveryResult = await recovery.RecoverAsync(
            execution.AuditExecutionId,
            new ValidationAuditExecutionRecoveryRequest
            {
                CurrentLeaseOwner = "harness-recover-owner",
                IsResume = true,
                TrialStatus = trialBefore.Status
            });
        execution = await executions.GetByAuditExecutionIdAsync(options.FixtureId) ?? execution;

        var oldScopeExecutionId = execution.ScopeExecutionId;
        Guid? newAuditExecutionId = null;
        Guid? newScopeExecutionId = null;
        int? newAttemptNumber = null;
        int? inMemoryAccessCountBeforeCrash = await ReadAccessHintAsync(options);
        if (options.CrashPoint == "AfterAuditExecutionCreatedBeforeFirstFlush"
            && recoveryResult.MustRerunTrial)
        {
            var supersession = sp.GetRequiredService<IValidationAuditExecutionSupersessionService>();
            var replacement = await supersession.SupersedeForRerunAsync(
                execution.AuditExecutionId,
                Guid.NewGuid().ToString("N"),
                recoveryResult.FailureCode ?? "PROCESS_INTERRUPTED_BEFORE_FLUSH",
                leaseOwner: "harness-recover-owner");
            newAuditExecutionId = replacement.AuditExecutionId;
            newScopeExecutionId = replacement.ScopeExecutionId;
            newAttemptNumber = replacement.AttemptNumber;
            execution = await executions.GetByAuditExecutionIdAsync(options.FixtureId) ?? execution;
        }

        var oldExecution = await executions.GetByAuditExecutionIdAsync(options.FixtureId);

        var finalizerInvoked = false;
        ValidationAuditExecutionCompletionResult? completeResult = null;
        if (options.CrashPoint == "AfterEventsConfirmedBeforeExecutionCompleted"
            && recoveryResult.CanContinueSameExecution
            && !recoveryResult.MustRerunTrial
            && execution.FinalExpectedSequence is null
            && execution.LastConfirmedSequence > 0)
        {
            finalizerInvoked = true;
            completeResult = await finalizer.CompleteAsync(
                execution.AuditExecutionId,
                execution.LastConfirmedSequence);
            execution = await executions.GetByAuditExecutionIdAsync(options.FixtureId) ?? execution;
        }

        var batchList = await batches.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
        var accessRows = (await audits.GetByExperimentIdAsync(execution.ValidationExperimentId))
            .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
            .ToList();
        var trialList = await trials.GetByExperimentIdAsync(execution.ValidationExperimentId);
        var trial = trialList.First(t => t.Id == execution.ValidationTrialId);
        var completeness = verifier.Verify(trial, execution, batchList, accessRows);

        var payload = new
        {
            FixtureId = options.FixtureId,
            CrashPoint = options.CrashPoint,
            BeforeRecoveryExecutionStatus = beforeRecoveryExecutionStatus.ToString(),
            BeforeRecoveryCompletenessIsComplete = completenessBefore.IsComplete,
            BeforeRecoveryCompletenessCode = completenessBefore.CompletionCode.ToString(),
            BeforeRecoveryFinalExpectedSequence = beforeRecoveryFinalExpectedSequence,
            ExecutionStatus = execution.Status.ToString(),
            LastConfirmedSequence = execution.LastConfirmedSequence,
            FinalExpectedSequence = execution.FinalExpectedSequence,
            ConfirmedEventCount = execution.ConfirmedEventCount,
            BatchCount = batchList.Count,
            ConfirmedBatchCount = batchList.Count(b => b.Status == ValidationAuditBatchStatus.Confirmed),
            EventCount = accessRows.Count,
            RecoveryDecision = recoveryResult.RecoveryDecision.ToString(),
            CanContinueSameExecution = recoveryResult.CanContinueSameExecution,
            MustRerunTrial = recoveryResult.MustRerunTrial,
            RecoveredLastConfirmedSequence = recoveryResult.RecoveredLastConfirmedSequence,
            RecoveryIsComplete = recoveryResult.IsComplete,
            RecoveryFailureCode = recoveryResult.FailureCode,
            CompletenessCode = completeness.CompletionCode.ToString(),
            CompletenessIsComplete = completeness.IsComplete,
            FinalizerInvoked = finalizerInvoked,
            FinalizerIsComplete = completeResult?.IsComplete,
            FinalizerCode = completeResult?.CompletionCode.ToString(),
            TrialAuditCompletionStatus = trial.AuditCompletionStatus.ToString(),
            OldAuditExecutionId = options.FixtureId,
            NewAuditExecutionId = newAuditExecutionId,
            OldScopeExecutionId = oldScopeExecutionId,
            NewScopeExecutionId = newScopeExecutionId,
            NewAttemptNumber = newAttemptNumber,
            NewFirstSequence = newAuditExecutionId is null ? (int?)null : 1,
            InMemoryAccessCountBeforeCrash = inMemoryAccessCountBeforeCrash,
            OldExecutionStatus = oldExecution?.Status.ToString()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (!string.IsNullOrWhiteSpace(options.ResultPath))
        {
            await File.WriteAllTextAsync(options.ResultPath, json);
        }

        return 0;
    }

    private static string AccessHintPath(HarnessArgs options) =>
        Path.Combine(Path.GetTempPath(), $"e2c1-access-{options.FixtureId:N}.txt");

    private static async Task WriteAccessHintAsync(HarnessArgs options, int count)
    {
        await File.WriteAllTextAsync(AccessHintPath(options), count.ToString());
    }

    private static async Task<int?> ReadAccessHintAsync(HarnessArgs options)
    {
        var path = AccessHintPath(options);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path);
        return int.TryParse(text.Trim(), out var count) ? count : null;
    }

    private static void WriteStateHint(
        HarnessArgs options,
        ValidationAuditExecution execution,
        int batchCount,
        int eventCount)
    {
        // Intentionally no recovery — crash simulation ends the process.
        Console.Error.WriteLine(
            $"CRASH_POINT={options.CrashPoint}; AuditExecutionId={execution.AuditExecutionId}; " +
            $"Batches={batchCount}; Events={eventCount}; LastConfirmed={execution.LastConfirmedSequence}");
    }

    private static async Task<(ValidationExperiment Experiment, ValidationParameterTrial Trial)> EnsureFixtureAsync(
        MomoQuantDbContext db,
        Guid fixtureId)
    {
        var name = $"E2C1-Harness-{fixtureId:N}";
        var existing = await db.ValidationExperiments.FirstOrDefaultAsync(e => e.Name == name);
        if (existing is not null)
        {
            var trial = await db.ValidationParameterTrials
                .FirstAsync(t => t.ValidationExperimentId == existing.Id && t.TrialNumber == 1);
            return (existing, trial);
        }

        var now = DateTime.UtcNow;
        var experiment = new ValidationExperiment
        {
            Name = name,
            ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration,
            Status = ValidationExperimentStatus.Draft,
            StrategyCode = "PSBR",
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            Exchange = "binance",
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            RequestedStartUtc = now.AddDays(-10),
            RequestedEndUtc = now,
            SplitRatio = 0.7m,
            CandleDataSnapshotJson = "{}",
            CandleDataFingerprint = "harness",
            WarmupSnapshotJson = "{}",
            ParameterSearchSpaceSnapshotJson = "{}",
            OptimizationObjectiveSnapshotJson = "{}",
            QualificationProfileSnapshotJson = "{}",
            DraftConfigurationJson = "{}",
            DiagnosticsJson = "[]",
            OverlayResultsJson = "{}",
            ComparisonJson = "{}",
            RegimeComparisonJson = "{}",
            ParameterStabilityJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.ValidationExperiments.Add(experiment);
        await db.SaveChangesAsync();

        var trialNew = new ValidationParameterTrial
        {
            ValidationExperimentId = experiment.Id,
            TrialNumber = 1,
            ParameterSnapshotJson = "{}",
            ParameterFingerprint = $"fp-{fixtureId:N}"[..Math.Min(64, $"fp-{fixtureId:N}".Length)],
            Status = ValidationTrialStatus.Running,
            StartedAtUtc = now,
            AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated,
            StrategyLabRunId = 1,
            GuardrailDecision = "Qualified"
        };
        db.ValidationParameterTrials.Add(trialNew);
        await db.SaveChangesAsync();
        return (experiment, trialNew);
    }

    private static Guid CreateScopeId(Guid fixtureId)
    {
        // Deterministic, distinct from AuditExecutionId (= fixtureId).
        var bytes = fixtureId.ToByteArray();
        bytes[0] ^= 0x5A;
        bytes[1] ^= 0xA5;
        return new Guid(bytes);
    }

    private static Guid CreateBatchId(Guid fixtureId)
    {
        var bytes = fixtureId.ToByteArray();
        bytes[2] ^= 0x5A;
        bytes[3] ^= 0xA5;
        return new Guid(bytes);
    }

    private static Guid CreateEventId(Guid fixtureId)
    {
        var bytes = fixtureId.ToByteArray();
        bytes[4] ^= 0x5A;
        bytes[5] ^= 0xA5;
        return new Guid(bytes);
    }

    private static async Task CleanupFixtureAsync(MomoQuantDbContext db, Guid fixtureId, long experimentId)
    {
        var auditIds = await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId || e.AuditExecutionId == fixtureId)
            .Select(e => e.AuditExecutionId)
            .ToListAsync();

        if (auditIds.Count > 0)
        {
            await db.ValidationAuditBatches
                .Where(b => auditIds.Contains(b.AuditExecutionId))
                .ExecuteDeleteAsync();
        }

        await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId || e.AuditExecutionId == fixtureId)
            .ExecuteDeleteAsync();
        await db.ValidationCandleAccessAudits
            .Where(a => a.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
    }

    private static void EnsureTestDatabase(string connection)
    {
        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(connection);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid MySQL connection string.", ex);
        }

        var dbName = builder.Database?.Trim();
        if (string.IsNullOrWhiteSpace(dbName)
            || !dbName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Harness refuses connection: database name must end with '_test' (got '{dbName}').");
        }
    }

    private static ServiceProvider BuildServices(string connection)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection
            })
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddPersistence(config);
        services.AddSingleton<IValidationAuditPayloadSetHasher, ValidationAuditPayloadSetHasher>();
        services.AddScoped<IValidationAuditCompletenessVerifier, ValidationAuditCompletenessVerifier>();
        services.AddScoped<IValidationAuditExecutionFactory, ValidationAuditExecutionService>();
        services.AddScoped<IValidationAuditExecutionSupersessionService, ValidationAuditExecutionSupersessionService>();
        services.AddScoped<IValidationAuditExecutionRecoveryService, ValidationAuditExecutionRecoveryService>();
        services.AddScoped<IValidationAuditExecutionFinalizer, ValidationAuditExecutionFinalizer>();
        services.AddScoped<IValidationTrialAuditCompletionGate, ValidationTrialAuditCompletionGate>();
        return services.BuildServiceProvider();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

/// <summary>Minimal bound training scope for crash-before-flush harness (in-memory access only).</summary>
internal sealed class HarnessBoundTrainingScope : IValidationTrainingCandleScope
{
    private readonly List<ValidationCandleAccessRecord> _log = new();

    public HarnessBoundTrainingScope(
        long experimentId,
        Guid scopeExecutionId,
        Guid boundAuditExecutionId,
        long trialId,
        DateTime segmentStartUtc,
        DateTime boundaryUtc)
    {
        ValidationExperimentId = experimentId;
        ScopeExecutionId = scopeExecutionId;
        BoundAuditExecutionId = boundAuditExecutionId;
        ActiveTrialId = trialId;
        SegmentStartUtc = segmentStartUtc;
        SegmentEndExclusiveUtc = boundaryUtc;
        ValidationBoundaryUtc = boundaryUtc;
        Partition = new ValidationCandlePartitionMetadata
        {
            ValidationExperimentId = experimentId,
            RequiredWarmupCandleCount = 0,
            AvailableWarmupCandleCount = 0,
            EvaluationCandleCount = 1,
            TotalCandleCount = 1,
            WarmupStatus = ValidationWarmupStatus.NotRequired,
            TrainingEvaluationStartUtc = segmentStartUtc,
            TrainingEvaluationEndExclusiveUtc = boundaryUtc,
            ValidationBoundaryUtc = boundaryUtc,
            SymbolId = 1,
            SymbolName = "HARNESS",
            Timeframe = "15m",
            RequirementsVersion = "Harness"
        };
    }

    public Guid ScopeExecutionId { get; }
    public Guid? BoundAuditExecutionId { get; }
    public string? CorrelationId { get; set; }
    public long? ActiveTrialId { get; set; }
    public int? ActiveTrialNumber { get; set; }
    public IReadOnlyList<ValidationCandleAccessRecord> AccessLog => _log;
    public long ValidationExperimentId { get; }
    public DateTime SegmentStartUtc { get; }
    public DateTime SegmentEndExclusiveUtc { get; }
    public DateTime ValidationBoundaryUtc { get; }
    public ValidationCandlePartitionMetadata Partition { get; }

    public void RecordEvaluationAccess(string callerComponent)
    {
        _log.Add(new ValidationCandleAccessRecord
        {
            AccessEventId = Guid.NewGuid(),
            ScopeExecutionId = ScopeExecutionId,
            ScopeSequenceNumber = 1,
            ValidationExperimentId = ValidationExperimentId,
            TrialId = ActiveTrialId,
            TrialNumber = 1,
            CallerComponent = callerComponent,
            AccessPurpose = ValidationCandleAccessPurpose.EvaluationRange,
            DatasetPartition = "Training",
            RequestedCandleCount = 1,
            ReturnedCandleCount = 1,
            CandleContentFingerprint = "HARNESS01",
            AccessedAtUtc = DateTime.UtcNow,
            RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion
        });
    }

    public IReadOnlyList<Candle> GetWarmupBefore(DateTime beforeOpenTimeUtc, int count, ValidationCandleAccessContext context) => [];
    public IReadOnlyList<Candle> GetWarmupBefore(ValidationWarmupAccessRequest request) => [];
    public IReadOnlyList<Candle> GetEvaluationRange(DateTime? fromUtc, DateTime? toUtcExclusive, ValidationCandleAccessContext context) => [];
    public IReadOnlyList<Candle> GetEvaluationRange(ValidationEvaluationAccessRequest request) => [];
    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, ValidationCandleAccessContext context) => null;
    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent) => null;
    public IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent) => [];
    public StrategyLabDataset CreateStrategyLabDataset(ValidationDatasetMaterializationRequest request) =>
        throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class HarnessArgs
{
    public string Phase { get; init; } = "";
    public string CrashPoint { get; init; } = "";
    public Guid FixtureId { get; init; }
    public string Connection { get; init; } = "";
    public string? ResultPath { get; init; }

    public static HarnessArgs Parse(string[] args)
    {
        string? phase = null;
        string? crash = null;
        string? fixture = null;
        string? connection = null;
        string? resultPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            string? value = null;
            if (key.Contains('=', StringComparison.Ordinal))
            {
                var parts = key.Split('=', 2);
                key = parts[0];
                value = parts[1];
            }
            else if (i + 1 < args.Length)
            {
                value = args[++i];
            }

            switch (key)
            {
                case "--phase":
                    phase = value;
                    break;
                case "--crash-point":
                    crash = value;
                    break;
                case "--fixture-id":
                    fixture = value;
                    break;
                case "--connection":
                    connection = value;
                    break;
                case "--result-path":
                    resultPath = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(phase)
            || string.IsNullOrWhiteSpace(crash)
            || string.IsNullOrWhiteSpace(fixture)
            || string.IsNullOrWhiteSpace(connection))
        {
            throw new ArgumentException(
                "Required: --phase write|recover --crash-point <name> --fixture-id <guid> --connection <mysql>");
        }

        return new HarnessArgs
        {
            Phase = phase.Trim().ToLowerInvariant(),
            CrashPoint = crash.Trim(),
            FixtureId = Guid.Parse(fixture),
            Connection = connection,
            ResultPath = resultPath
        };
    }
}
