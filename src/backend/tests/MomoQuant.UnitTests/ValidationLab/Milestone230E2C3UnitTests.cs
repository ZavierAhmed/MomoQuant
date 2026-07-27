using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C3 — ranking, selection, evaluator, and leakage evidence unit coverage.</summary>
public sealed class Milestone230E2C3UnitTests
{
    private readonly ValidationAuditPayloadSetHasher _hasher = new();

    [Fact]
    public void Ranker_CompletedGuardrailPassedButAuditNotComplete_IsExcluded()
    {
        var incomplete = EligibleBase(1, "INC", 90m);
        incomplete.AuthoritativeAuditExecutionId = null;
        incomplete.AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated;
        incomplete.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;

        var complete = MarkAuditComplete(EligibleBase(2, "OK", 10m));

        var trials = new List<ValidationParameterTrial> { incomplete, complete };
        ValidationTrialRanker.AssignRanks(trials);

        Assert.Null(incomplete.Rank);
        Assert.Equal(ValidationTrialRankEligibility.Ineligible, incomplete.TrialRankEligibility);
        Assert.Contains(
            ValidationAuthoritativeAuditQualificationEvaluator.RankIneligibleReasonCode,
            JsonSerializer.Deserialize<string[]>(incomplete.RankIneligibleReasonsJson!)!);
        Assert.Equal(1, complete.Rank);
        Assert.DoesNotContain(incomplete, ValidationTrialRanker.OrderForRanking(trials));
    }

    [Fact]
    public void Ranker_AuthoritativeAuditComplete_RemainsEligible()
    {
        var trial = MarkAuditComplete(EligibleBase(1, "WIN", 50m));
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;

        ValidationTrialRanker.AssignRanks([trial]);

        Assert.Equal(1, trial.Rank);
        Assert.Equal(ValidationTrialRankEligibility.Eligible, trial.TrialRankEligibility);
        Assert.Same(trial, ValidationTrialRanker.SelectWinner([trial]));
    }

    [Fact]
    public void Ranker_DoesNotChangeExistingEligibleTieBreakOrder()
    {
        var a = MarkAuditComplete(EligibleBase(1, "bbb", 50m));
        a.NetExpectancyR = 1m;
        a.ProfitFactor = 2m;
        a.MaximumDrawdownPercent = null;
        a.ClosedTradeCount = 10;

        var b = MarkAuditComplete(EligibleBase(2, "aaa", 50m));
        b.NetExpectancyR = 1m;
        b.ProfitFactor = 2m;
        b.MaximumDrawdownPercent = null;
        b.ClosedTradeCount = 10;

        var c = MarkAuditComplete(EligibleBase(3, "aaa", 50m));
        c.NetExpectancyR = 1m;
        c.ProfitFactor = 2m;
        c.MaximumDrawdownPercent = null;
        c.ClosedTradeCount = 10;

        var orderA = ValidationTrialRanker.OrderForRanking([a, b, c]).Select(t => t.TrialNumber).ToArray();
        var orderB = ValidationTrialRanker.OrderForRanking([c, a, b]).Select(t => t.TrialNumber).ToArray();

        Assert.Equal(new[] { 2, 3, 1 }, orderA);
        Assert.Equal(new[] { 2, 3, 1 }, orderB);
    }

    [Fact]
    public void Selection_AuditIncompleteTrial_IsNotEligible()
    {
        var svc = new ValidationTrainingSelectionService();
        var incomplete = EligibleBase(1, "INC", 99m);
        incomplete.AuthoritativeAuditExecutionId = Guid.NewGuid();
        incomplete.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;

        var result = svc.FinalizeTrainingSelection(TrainingExperiment(), [incomplete]);

        Assert.False(result.Succeeded);
        Assert.True(result.AuditEvidenceIncomplete);
        Assert.Null(result.FailureCode);
        Assert.Equal(0, result.Population.EligibleTrialCount);
        Assert.Equal(1, result.Population.GuardrailPassedTrialCount);
        Assert.Equal(ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage, result.FailureMessage);
    }

    [Fact]
    public void Selection_StaleCompleteFlagWithoutExecution_IsNotEligible()
    {
        var svc = new ValidationTrainingSelectionService();
        var stale = EligibleBase(1, "STALE", 99m);
        stale.AuthoritativeAuditExecutionId = null;
        stale.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;

        var result = svc.FinalizeTrainingSelection(TrainingExperiment(), [stale]);

        Assert.False(result.Succeeded);
        Assert.True(result.AuditEvidenceIncomplete);
        Assert.Null(result.SelectedTrial);
        Assert.Equal(0, result.Population.EligibleTrialCount);
    }

    [Fact]
    public void SelectionIntegrity_SelectedAuditIncomplete_FailsClosed()
    {
        var integrity = new ValidationSelectionIntegrityService(
            new ValidationParameterFingerprintService(),
            new ValidationTrainingSelectionService());

        var experiment = TrainingExperiment();
        experiment.Status = ValidationExperimentStatus.TrainingCompleted;
        experiment.SelectedTrialId = 1;
        experiment.SelectedTrialParameterFingerprint = "fp";
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.Passed;

        var selected = EligibleBase(1, "fp", 10m);
        selected.AuthoritativeAuditExecutionId = null;
        selected.AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated;
        selected.Rank = 1;

        var report = integrity.Evaluate(experiment, [selected]);
        Assert.Equal(ValidationSelectionIntegrityStatus.FailedSelectedTrialIneligible, report.Status);
        Assert.False(report.IsEligibleForSelection);
        Assert.False(integrity.CanFreeze(experiment, [selected], out var reason));
        Assert.Contains("Authoritative validation audit", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HistoricalNotEvaluatedTrial_IsNotQualificationEligible()
    {
        var trial = EligibleBase(9, "HIST", 1m);
        trial.AuthoritativeAuditExecutionId = null;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated;

        var evaluator = CreateEvaluator();
        var result = await evaluator.EvaluateTrialAsync(TrainingExperiment(), trial);

        Assert.True(result.IsApplicable);
        Assert.False(result.IsQualificationEligible);
        Assert.Equal(ValidationAuditCompletenessCode.HistoricalNotEvaluated, result.CompletenessCode);
        Assert.False(ValidationAuthoritativeAuditQualificationEvaluator.MeetsCachedAuditEligibilityFields(trial));
    }

    [Fact]
    public async Task Evaluator_ValidateExistingFrozenConfiguration_IsNotApplicable()
    {
        var experiment = TrainingExperiment();
        experiment.ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration;
        var trial = MarkAuditComplete(EligibleBase(1, "x", 1m));
        var evaluator = CreateEvaluator();

        var result = await evaluator.EvaluateTrialAsync(experiment, trial);
        Assert.False(result.IsApplicable);
        Assert.False(result.IsQualificationEligible);
    }

    [Fact]
    public async Task Evaluator_AuthoritativeComplete_IsEligible()
    {
        var (trial, execution, batches, rows) = BuildCompleteBundle(eventCount: 1);
        var fakes = new AuditFakes();
        fakes.Executions.Add(execution);
        fakes.Batches.AddRange(batches);
        fakes.Access.AddRange(rows);

        var result = await CreateEvaluator(fakes).EvaluateTrialAsync(TrainingExperiment(id: trial.ValidationExperimentId), trial);
        Assert.True(result.IsQualificationEligible);
        Assert.Equal(ValidationAuditCompletenessCode.Complete, result.CompletenessCode);
        Assert.True(result.Completeness!.IsComplete);
        Assert.Equal(execution.ScopeExecutionId, result.ScopeExecutionId);
    }

    [Fact]
    public async Task Evaluator_SupersededExecution_IsNotEligible()
    {
        var (trial, execution, batches, rows) = BuildCompleteBundle(eventCount: 1);
        execution.Status = ValidationAuditExecutionStatus.Superseded;
        var fakes = new AuditFakes();
        fakes.Executions.Add(execution);
        fakes.Batches.AddRange(batches);
        fakes.Access.AddRange(rows);

        var result = await CreateEvaluator(fakes).EvaluateTrialAsync(TrainingExperiment(id: trial.ValidationExperimentId), trial);
        Assert.False(result.IsQualificationEligible);
        Assert.Equal(ValidationAuditCompletenessCode.Superseded, result.CompletenessCode);
    }

    [Fact]
    public void Leakage_AuthoritativeCompleteScopes_CanPass()
    {
        var scope = Guid.NewGuid();
        var trainEnd = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var valStart = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);
        var row = AllowedRow(scope, 1, trainEnd.AddHours(-1));
        var trial = MarkAuditComplete(EligibleBase(1, "a", 1m));
        var evaluation = EligibleEvaluation(trial.Id, scope);

        var selection = ValidationLeakageEvidenceSelector.SelectPositiveEvidence(
            [row],
            [(trial, evaluation)]);

        Assert.False(selection.AuthoritativeEvidenceIncomplete);
        Assert.Single(selection.PositiveRows);
        Assert.Equal([scope], selection.AuthoritativeScopeExecutionIds.ToArray());

        var report = new ValidationLeakageAuditor().EvaluateFromAccessEvidence(
            selection.PositiveRows, valStart, trainEnd.AddDays(-7), trainEnd, "fp");
        Assert.Equal(ValidationLeakageAuditStatus.Passed, report.Status);
    }

    [Fact]
    public void Leakage_SupersededRowsCannotSupplyPositiveEvidence()
    {
        var supersededScope = Guid.NewGuid();
        var authScope = Guid.NewGuid();
        var trainEnd = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            AllowedRow(supersededScope, 1, trainEnd.AddHours(-2)),
            AllowedRow(authScope, 1, trainEnd.AddHours(-1))
        };

        var trial = MarkAuditComplete(EligibleBase(1, "a", 1m));
        // Evaluation is incomplete — superseded scope must not be used for positive proof.
        var blocked = ValidationAuthoritativeAuditQualificationResult.Blocked(
            trial.Id,
            ValidationAuditCompletenessCode.Superseded,
            ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage,
            scopeExecutionId: supersededScope);

        var selection = ValidationLeakageEvidenceSelector.SelectPositiveEvidence(
            rows,
            [(trial, blocked)]);

        Assert.True(selection.AuthoritativeEvidenceIncomplete);
        Assert.Empty(selection.PositiveRows);
        Assert.DoesNotContain(rows[0], selection.PositiveRows);
    }

    [Fact]
    public void Leakage_ForeignScopeCannotFillSequenceGap()
    {
        var authScope = Guid.NewGuid();
        var foreignScope = Guid.NewGuid();
        var trainEnd = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            AllowedRow(authScope, 1, trainEnd.AddHours(-2)),
            AllowedRow(foreignScope, 2, trainEnd.AddHours(-1)) // would fill seq 2 if merged
        };

        var trial = MarkAuditComplete(EligibleBase(1, "a", 1m));
        var evaluation = EligibleEvaluation(trial.Id, authScope);

        var selection = ValidationLeakageEvidenceSelector.SelectPositiveEvidence(
            rows,
            [(trial, evaluation)]);

        Assert.All(selection.PositiveRows, r => Assert.Equal(authScope, r.ScopeExecutionId));
        Assert.DoesNotContain(selection.PositiveRows, r => r.ScopeExecutionId == foreignScope);
        Assert.Equal(1, selection.PositiveRows.Count);
    }

    [Fact]
    public void Leakage_DeniedSupersededAttempt_RemainsFailed()
    {
        var oldScope = Guid.NewGuid();
        var denied = AllowedRow(oldScope, 1, new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc));
        denied.WasDenied = true;
        denied.DenialCode = "ValidationDataLeakageDetected";
        denied.DenialReason = "boundary denied";

        var negatives = ValidationLeakageEvidenceSelector.CollectNegativeBlockingEvidence([denied]);
        Assert.Single(negatives);

        var report = new ValidationLeakageAuditor().EvaluateFromAccessEvidence(
            negatives,
            new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            "fp");
        Assert.Equal(ValidationLeakageAuditStatus.Failed, report.Status);
        Assert.True(report.BlocksFreezeOrPassed);
    }

    [Fact]
    public void Leakage_IncompleteAuthoritativeExecution_CannotPass()
    {
        var scope = Guid.NewGuid();
        var row = AllowedRow(scope, 1, new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc));
        var trial = EligibleBase(1, "a", 1m);
        trial.AuthoritativeAuditExecutionId = Guid.NewGuid();
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;

        var blocked = ValidationAuthoritativeAuditQualificationResult.Blocked(
            trial.Id,
            ValidationAuditCompletenessCode.SequenceGap,
            ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage,
            scopeExecutionId: scope);

        var selection = ValidationLeakageEvidenceSelector.SelectPositiveEvidence(
            [row],
            [(trial, blocked)]);

        Assert.True(selection.AuthoritativeEvidenceIncomplete);
        Assert.Empty(selection.PositiveRows);
    }

    [Fact]
    public void MixedAuthoritativeAndSupersededRows_UsesOnlyAuthoritativePositiveEvidence()
    {
        var auth = Guid.NewGuid();
        var old = Guid.NewGuid();
        var trainEnd = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            AllowedRow(old, 1, trainEnd.AddHours(-3)),
            AllowedRow(auth, 1, trainEnd.AddHours(-1))
        };

        var trial = MarkAuditComplete(EligibleBase(1, "a", 1m));
        var selection = ValidationLeakageEvidenceSelector.SelectPositiveEvidence(
            rows,
            [(trial, EligibleEvaluation(trial.Id, auth))]);

        Assert.Single(selection.PositiveRows);
        Assert.Equal(auth, selection.PositiveRows[0].ScopeExecutionId);
    }

    private static ValidationExperiment TrainingExperiment(long id = 1) => new()
    {
        Id = id,
        MaximumTrials = 25,
        ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
        AllowInfrastructureOnlyRejectedTrialFallback = false
    };

    private static ValidationParameterTrial EligibleBase(long id, string fp, decimal score) => new()
    {
        Id = id,
        ValidationExperimentId = 1,
        TrialNumber = (int)id,
        ParameterFingerprint = fp,
        ParameterSnapshotJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = fp }),
        Status = ValidationTrialStatus.Completed,
        GuardrailDecision = "Passed",
        TrainingScore = score,
        NetExpectancyR = 0.2m,
        ProfitFactor = 1.3m,
        ClosedTradeCount = 10,
        TrialRankEligibility = ValidationTrialRankEligibility.Eligible
    };

    private static ValidationParameterTrial MarkAuditComplete(ValidationParameterTrial trial)
    {
        trial.AuthoritativeAuditExecutionId ??= Guid.Parse($"00000000-0000-0000-0000-{trial.Id:D12}");
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
        return trial;
    }

    private static ValidationAuthoritativeAuditQualificationResult EligibleEvaluation(long trialId, Guid scope) =>
        new()
        {
            IsApplicable = true,
            TrialId = trialId,
            AuditExecutionId = Guid.NewGuid(),
            ScopeExecutionId = scope,
            AttemptNumber = 1,
            AuthoritativeStatus = ValidationAuditExecutionStatus.Completed,
            TrialAuditCompletionStatus = ValidationAuditCompletionStatus.Complete,
            CompletenessCode = ValidationAuditCompletenessCode.Complete,
            IsQualificationEligible = true,
            Completeness = new ValidationAuditCompletenessResult
            {
                IsAuthoritative = true,
                IsComplete = true,
                IsTerminal = true,
                CompletionCode = ValidationAuditCompletenessCode.Complete
            }
        };

    private static ValidationCandleAccessAudit AllowedRow(Guid scope, long seq, DateTime maxTs) => new()
    {
        AccessEventId = Guid.NewGuid(),
        ScopeExecutionId = scope,
        ScopeSequenceNumber = seq,
        AccessPayloadHash = new string('A', 64),
        AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current,
        CallerComponent = "Test",
        RecorderVersion = "ValidationCandleAccess/v2",
        AccessedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        MaximumReturnedTimestampUtc = maxTs,
        WasDenied = false,
        TrialNumber = 1
    };

    private (ValidationParameterTrial Trial, ValidationAuditExecution Execution, List<ValidationAuditBatch> Batches, List<ValidationCandleAccessAudit> Rows)
        BuildCompleteBundle(int eventCount)
    {
        var experimentId = 42L;
        var trial = MarkAuditComplete(EligibleBase(7, "complete", 1m));
        trial.ValidationExperimentId = experimentId;

        var execution = new ValidationAuditExecution
        {
            AuditExecutionId = trial.AuthoritativeAuditExecutionId!.Value,
            ScopeExecutionId = Guid.NewGuid(),
            ExecutionToken = "tok",
            AttemptNumber = 1,
            ValidationExperimentId = experimentId,
            ValidationTrialId = trial.Id,
            Status = ValidationAuditExecutionStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1
        };

        var entries = new List<ValidationAuditPayloadSetEntry>();
        var rows = new List<ValidationCandleAccessAudit>();
        for (var i = 1; i <= eventCount; i++)
        {
            var eventId = Guid.Parse($"{i:D8}-0000-0000-0000-000000000000");
            var hash = new string((char)('A' + i), 64);
            entries.Add(new ValidationAuditPayloadSetEntry(
                i, eventId, hash, ValidationAccessPayloadContractVersions.Current));
            rows.Add(new ValidationCandleAccessAudit
            {
                AccessEventId = eventId,
                ValidationExperimentId = experimentId,
                ScopeExecutionId = execution.ScopeExecutionId,
                ScopeSequenceNumber = i,
                AccessPayloadHash = hash,
                AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current,
                CallerComponent = "Test",
                RecorderVersion = "ValidationCandleAccess/v2",
                AccessedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var setHash = _hasher.ComputeSetHash(entries);
        var (idsJson, hashesJson) = _hasher.BuildManifestJsons(entries);
        execution.FinalExpectedSequence = eventCount;
        execution.ExpectedEventCount = eventCount;
        execution.LastConfirmedSequence = eventCount;
        execution.ConfirmedEventCount = eventCount;
        execution.FinalPayloadSetHash = setHash;

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = eventCount,
            ExpectedEventCount = eventCount,
            ExpectedEventIdsJson = idsJson,
            ExpectedPayloadHashesJson = hashesJson,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed
        };

        return (trial, execution, [batch], rows);
    }

    private static ValidationAuthoritativeAuditQualificationEvaluator CreateEvaluator(AuditFakes? fakes = null)
    {
        fakes ??= new AuditFakes();
        return new ValidationAuthoritativeAuditQualificationEvaluator(
            fakes.Executions,
            fakes.Batches,
            fakes.Access,
            new ValidationAuditCompletenessVerifier());
    }

    private sealed class AuditFakes
    {
        public ExecutionRepo Executions { get; } = new();
        public BatchRepo Batches { get; } = new();
        public AccessRepo Access { get; } = new();
    }

    private sealed class ExecutionRepo : IValidationAuditExecutionRepository
    {
        public List<ValidationAuditExecution> Items { get; } = [];

        public void Add(ValidationAuditExecution execution) => Items.Add(execution);

        public Task<ValidationAuditExecution?> GetByAuditExecutionIdAsync(Guid auditExecutionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(e => e.AuditExecutionId == auditExecutionId));

        public Task<IReadOnlyList<ValidationAuditExecution>> GetByTrialIdAsync(long validationTrialId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditExecution>>(Items.Where(e => e.ValidationTrialId == validationTrialId).ToList());

        public Task<IReadOnlyList<ValidationAuditExecution>> GetActiveByTrialIdAsync(long validationTrialId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditExecution>>(Items.Where(e => e.ValidationTrialId == validationTrialId).ToList());

        public Task AddAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
        {
            Items.Add(execution);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ValidationAuditExecution> CreateAndAssignTrialAuthoritativeAsync(
            ValidationAuditExecution execution,
            ValidationParameterTrial trial,
            CancellationToken cancellationToken = default)
        {
            Items.Add(execution);
            trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
            return Task.FromResult(execution);
        }
    }

    private sealed class BatchRepo : IValidationAuditBatchRepository
    {
        public List<ValidationAuditBatch> Items { get; } = [];

        public void AddRange(IEnumerable<ValidationAuditBatch> batches) => Items.AddRange(batches);

        public Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(Guid auditBatchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(b => b.AuditBatchId == auditBatchId));

        public Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(Guid auditExecutionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditBatch>>(Items.Where(b => b.AuditExecutionId == auditExecutionId).ToList());

        public Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default)
        {
            Items.Add(batch);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ValidationAuditBatch> GetOrCreateManifestAsync(ValidationAuditBatch proposed, CancellationToken cancellationToken = default) =>
            Task.FromResult(proposed);
    }

    private sealed class AccessRepo : IValidationCandleAccessAuditRepository
    {
        public List<ValidationCandleAccessAudit> Items { get; } = [];

        public void AddRange(IEnumerable<ValidationCandleAccessAudit> rows) => Items.AddRange(rows);

        public Task AddRangeAsync(IReadOnlyList<ValidationCandleAccessAudit> audits, CancellationToken cancellationToken = default)
        {
            Items.AddRange(audits);
            return Task.CompletedTask;
        }

        public Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            Items.AddRange(audits);
            return Task.FromResult(new ValidationAccessBatchPersistResult());
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(long experimentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(Items.Where(a => a.ValidationExperimentId == experimentId).ToList());
    }
}
