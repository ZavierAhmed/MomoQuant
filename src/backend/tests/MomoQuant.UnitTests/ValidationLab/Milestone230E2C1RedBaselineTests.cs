using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E2C1 — former pre-fix defects now assert safe (green) behavior.
/// These replace the original red-baseline adversarial tests.
/// </summary>
public sealed class Milestone230E2C1RedBaselineTests
{
    private readonly ValidationAuditCompletenessVerifier _verifier = new();
    private readonly ValidationTrialAuditCompletionGate _gate = new();
    private readonly ValidationAuditPayloadSetHasher _hasher = new();

    [Fact]
    public void TrialAccessBeginsWithoutDurableAuditExecution_CurrentlyAllowed()
    {
        // Post-fix: new trials must bind an authoritative audit execution before completion.
        var trial = NewTrial();
        trial.AuthoritativeAuditExecutionId = null;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated;

        var completeness = ValidationAuditCompletenessResult.HistoricalNotEvaluated();
        Assert.False(_gate.CanMarkTrialCompleted(trial, null, completeness));
    }

    [Fact]
    public void CrashBeforeFirstFlush_ZeroRowsCannotProveCompletion()
    {
        var trial = NewTrial();
        var execution = NewExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        execution.Status = ValidationAuditExecutionStatus.InProgress;
        execution.FinalExpectedSequence = null;

        var result = _verifier.Verify(trial, execution, [], []);
        Assert.False(result.IsComplete);
        Assert.NotEqual(ValidationAuditCompletenessCode.Complete, result.CompletionCode);
    }

    [Fact]
    public void CrashAfterEventCommit_NewRecorderCannotRecoverCurrentCursor()
    {
        // Durable LastConfirmedSequence is the recovery source — memory cursor is not trusted alone.
        var execution = NewExecution();
        execution.LastConfirmedSequence = 2;
        Assert.True(execution.CanAdvanceSequence(2));
        Assert.False(execution.CanAdvanceSequence(1));
    }

    [Fact]
    public void EventRowsWithoutTerminalMarker_AreCurrentlyTreatedAsSufficient()
    {
        // Post-fix: event rows alone never yield IsComplete without Status=Completed.
        var trial = NewTrial();
        var execution = NewExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;

        var eventId = Guid.NewGuid();
        var hash = new string('A', 64);
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = _hasher.ComputeSetHash(entries);
        var (ids, hashes) = _hasher.BuildManifestJsons(entries);

        execution.FinalExpectedSequence = 1;
        execution.ExpectedEventCount = 1;
        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalPayloadSetHash = setHash;
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = ids,
            ExpectedPayloadHashesJson = hashes,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed
        };
        var row = new ValidationCandleAccessAudit
        {
            AccessEventId = eventId,
            ScopeExecutionId = execution.ScopeExecutionId,
            ScopeSequenceNumber = 1,
            AccessPayloadHash = hash,
            AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current,
            CallerComponent = "Test",
            RecorderVersion = "ValidationCandleAccess/v2",
            AccessedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = _verifier.Verify(trial, execution, [batch], [row]);
        Assert.False(result.IsComplete);
        Assert.Equal(ValidationAuditCompletenessCode.ExecutionInProgress, result.CompletionCode);
    }

    [Fact]
    public void CompletedStrategyLabRun_CanCurrentlyCompleteAuditIncompleteTrial()
    {
        // Post-fix gate: StrategyLab-complete metrics cannot mark Completed without audit Complete.
        var trial = NewTrial();
        trial.Status = ValidationTrialStatus.Completed;
        trial.AuthoritativeAuditExecutionId = null;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated;

        var completeness = ValidationAuditCompletenessResult.HistoricalNotEvaluated();
        Assert.False(_gate.CanMarkTrialCompleted(trial, null, completeness));
    }

    [Fact]
    public void MultipleScopeExecutions_HaveNoAuthoritativeIdentity()
    {
        var trial = NewTrial();
        var authoritative = NewExecution();
        var other = NewExecution();
        trial.AuthoritativeAuditExecutionId = authoritative.AuditExecutionId;

        var result = _verifier.Verify(trial, other, [], []);
        Assert.False(result.IsAuthoritative);
        Assert.Equal(ValidationAuditCompletenessCode.NotAuthoritative, result.CompletionCode);
    }

    [Fact]
    public void TrialCanCurrentlyBecomeCompletedBeforeAuditTerminalCompletion()
    {
        var trial = NewTrial();
        var execution = NewExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;

        var completeness = new ValidationAuditCompletenessResult
        {
            AuditExecutionId = execution.AuditExecutionId,
            IsAuthoritative = true,
            IsComplete = false,
            CompletionCode = ValidationAuditCompletenessCode.ExecutionInProgress
        };

        Assert.False(_gate.CanMarkTrialCompleted(trial, execution, completeness));
    }

    [Fact]
    public void MissingSequence_IsNotCurrentlyDetected()
    {
        var trial = NewTrial();
        var execution = NewExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        execution.FinalExpectedSequence = 2;
        execution.ExpectedEventCount = 2;
        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalPayloadSetHash = new string('C', 64);
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;

        var eventId = Guid.NewGuid();
        var hash = new string('D', 64);
        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = $"[\"{eventId:D}\"]",
            ExpectedPayloadHashesJson = $"[\"{hash}\"]",
            ExpectedPayloadSetHash = new string('E', 64),
            Status = ValidationAuditBatchStatus.Confirmed
        };
        var row = new ValidationCandleAccessAudit
        {
            AccessEventId = eventId,
            ScopeExecutionId = execution.ScopeExecutionId,
            ScopeSequenceNumber = 1,
            AccessPayloadHash = hash,
            AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current,
            CallerComponent = "Test",
            RecorderVersion = "ValidationCandleAccess/v2",
            AccessedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = _verifier.Verify(trial, execution, [batch], [row]);
        Assert.Equal(ValidationAuditCompletenessCode.SequenceGap, result.CompletionCode);
        Assert.Contains(2L, result.MissingSequences);
    }

    [Fact]
    public void DuplicateSequence_IsNotCurrentlyDetected()
    {
        var ex = Assert.Throws<ValidationAuditExecutionException>(() =>
            _hasher.ComputeSetHash(
            [
                new ValidationAuditPayloadSetEntry(1, Guid.NewGuid(), new string('A', 64), ValidationAccessPayloadContractVersions.Current),
                new ValidationAuditPayloadSetEntry(1, Guid.NewGuid(), new string('B', 64), ValidationAccessPayloadContractVersions.Current)
            ]));
        Assert.Equal("VALIDATION_AUDIT_DUPLICATE_SEQUENCE", ex.ErrorCode);
    }

    [Fact]
    public void BatchManifest_DoesNotCurrentlyExistBeforePersistence()
    {
        // Post-fix: durable batch entity exists and validates range integrity before use.
        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = Guid.NewGuid(),
            FirstSequence = 1,
            LastSequence = 2,
            ExpectedEventCount = 2,
            Status = ValidationAuditBatchStatus.Created
        };
        batch.ValidateRangeIntegrity();
        Assert.Equal(ValidationAuditBatchStatus.Created, batch.Status);
    }

    [Fact]
    public void FinalExpectedSequence_DoesNotCurrentlyExist()
    {
        var execution = NewExecution();
        Assert.Null(execution.FinalExpectedSequence);
        execution.SetFinalExpectedSequence(3, DateTime.UtcNow);
        Assert.Equal(3, execution.FinalExpectedSequence);
    }

    [Fact]
    public void SupersededExecutionConcept_DoesNotCurrentlyExist()
    {
        var execution = NewExecution();
        var successor = Guid.NewGuid();
        execution.MarkSuperseded(successor, DateTime.UtcNow, "PROCESS_INTERRUPTED_BEFORE_FLUSH");
        Assert.Equal(ValidationAuditExecutionStatus.Superseded, execution.Status);
        Assert.Equal(successor, execution.SupersededByAuditExecutionId);
        Assert.Equal(
            ValidationAuditCompletenessCode.Superseded,
            execution.ValidateCompletionPreconditions());
    }

    private static ValidationParameterTrial NewTrial() => new()
    {
        Id = 99,
        ValidationExperimentId = 1,
        TrialNumber = 1,
        ParameterFingerprint = "red-fp",
        Status = ValidationTrialStatus.Running
    };

    private static ValidationAuditExecution NewExecution() => new()
    {
        AuditExecutionId = Guid.NewGuid(),
        ValidationExperimentId = 1,
        ValidationTrialId = 99,
        TrialNumber = 1,
        ScopeExecutionId = Guid.NewGuid(),
        AttemptNumber = 1,
        ExecutionToken = "red-tok",
        Status = ValidationAuditExecutionStatus.InProgress,
        StartedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
        RowVersion = 1
    };
}
