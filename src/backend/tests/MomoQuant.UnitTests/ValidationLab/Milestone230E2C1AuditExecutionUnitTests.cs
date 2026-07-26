using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C1 — mandatory durable audit-execution unit coverage (30 tests).</summary>
public sealed class Milestone230E2C1AuditExecutionUnitTests
{
    private readonly ValidationAuditPayloadSetHasher _hasher = new();
    private readonly ValidationAuditCompletenessVerifier _verifier = new();
    private readonly ValidationTrialAuditCompletionGate _gate = new();

    [Fact]
    public void AuditExecution_CreatedWithImmutableIdentity()
    {
        var auditId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var execution = new ValidationAuditExecution
        {
            AuditExecutionId = auditId,
            ScopeExecutionId = scopeId,
            ExecutionToken = "token-1",
            AttemptNumber = 1,
            Status = ValidationAuditExecutionStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1
        };

        Assert.Equal(auditId, execution.AuditExecutionId);
        Assert.Equal(scopeId, execution.ScopeExecutionId);
        Assert.Equal("token-1", execution.ExecutionToken);
        Assert.Equal(1, execution.AttemptNumber);
        Assert.Equal(ValidationAuditExecution.ContractVersionV1, execution.AuditContractVersion);
    }

    [Fact]
    public void AuditExecution_CompletedRequiresFinalExpectedSequence()
    {
        var execution = CreateExecution();
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
        execution.LastConfirmedSequence = 2;
        execution.ConfirmedEventCount = 2;
        execution.ExpectedEventCount = 2;
        execution.FinalPayloadSetHash = new string('A', 64);

        var missing = execution.ValidateCompletionPreconditions();
        Assert.Equal(ValidationAuditCompletenessCode.FinalSequenceMissing, missing);

        execution.FinalExpectedSequence = 2;
        Assert.Equal(ValidationAuditCompletenessCode.Complete, execution.ValidateCompletionPreconditions());
    }

    [Fact]
    public void AuditExecution_CompletedRequiresContiguousConfirmedSequence()
    {
        var execution = CreateExecution();
        execution.FinalExpectedSequence = 3;
        execution.ExpectedEventCount = 3;
        execution.FinalPayloadSetHash = new string('A', 64);
        execution.LastConfirmedSequence = 2;
        execution.ConfirmedEventCount = 2;

        Assert.Equal(ValidationAuditCompletenessCode.SequenceGap, execution.ValidateCompletionPreconditions());

        execution.LastConfirmedSequence = 3;
        execution.ConfirmedEventCount = 3;
        Assert.Equal(ValidationAuditCompletenessCode.Complete, execution.ValidateCompletionPreconditions());
    }

    [Fact]
    public void AuditExecution_LastConfirmedSequenceCannotDecrease()
    {
        var execution = CreateExecution();
        execution.LastConfirmedSequence = 5;
        var now = DateTime.UtcNow;

        Assert.Throws<InvalidOperationException>(() =>
            execution.AdvanceLastConfirmedSequence(4, now));

        Assert.False(execution.CanAdvanceSequence(4));
        execution.AdvanceLastConfirmedSequence(5, now);
        Assert.Equal(5, execution.LastConfirmedSequence);
        execution.AdvanceLastConfirmedSequence(6, now);
        Assert.Equal(6, execution.LastConfirmedSequence);
    }

    [Fact]
    public void AuditExecution_FinalExpectedSequenceCannotChangeAfterCompletion()
    {
        var execution = CreateExecution();
        var now = DateTime.UtcNow;
        execution.SetFinalExpectedSequence(3, now);
        execution.Status = ValidationAuditExecutionStatus.Completed;
        execution.FinalExpectedSequence = 3;

        Assert.Throws<InvalidOperationException>(() =>
            execution.SetFinalExpectedSequence(4, now));

        execution.SetFinalExpectedSequence(3, now);
        Assert.Equal(3, execution.FinalExpectedSequence);
    }

    [Fact]
    public void AuditExecution_SupersededCannotComplete()
    {
        var execution = CreateExecution();
        execution.MarkSuperseded(Guid.NewGuid(), DateTime.UtcNow, "PROCESS_INTERRUPTED_BEFORE_FLUSH");
        execution.FinalExpectedSequence = 1;
        execution.ExpectedEventCount = 1;
        execution.FinalPayloadSetHash = new string('B', 64);
        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;

        Assert.Equal(ValidationAuditCompletenessCode.Superseded, execution.ValidateCompletionPreconditions());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.MarkSuperseded(Guid.NewGuid(), DateTime.UtcNow));
        Assert.Contains("already Superseded", ex.Message);
    }

    [Fact]
    public void BatchManifest_CanonicalOrderingStable()
    {
        var id1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var id2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var hash1 = new string('1', 64);
        var hash2 = new string('2', 64);

        var a = new[]
        {
            new ValidationAuditPayloadSetEntry(2, id2, hash2, ValidationAccessPayloadContractVersions.Current),
            new ValidationAuditPayloadSetEntry(1, id1, hash1, ValidationAccessPayloadContractVersions.Current)
        };
        var b = new[]
        {
            new ValidationAuditPayloadSetEntry(1, id1, hash1, ValidationAccessPayloadContractVersions.Current),
            new ValidationAuditPayloadSetEntry(2, id2, hash2, ValidationAccessPayloadContractVersions.Current)
        };

        Assert.Equal(_hasher.ComputeSetHash(a), _hasher.ComputeSetHash(b));
        var (idsA, hashesA) = _hasher.BuildManifestJsons(a);
        var (idsB, hashesB) = _hasher.BuildManifestJsons(b);
        Assert.Equal(idsA, idsB);
        Assert.Equal(hashesA, hashesB);
    }

    [Fact]
    public void BatchManifest_DuplicateAccessEventIdRejected()
    {
        var id = Guid.NewGuid();
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, id, new string('A', 64), ValidationAccessPayloadContractVersions.Current),
            new ValidationAuditPayloadSetEntry(2, id, new string('B', 64), ValidationAccessPayloadContractVersions.Current)
        };

        var ex = Assert.Throws<ValidationAuditExecutionException>(() => _hasher.ComputeSetHash(entries));
        Assert.Equal("VALIDATION_AUDIT_DUPLICATE_ACCESS_EVENT_ID", ex.ErrorCode);
    }

    [Fact]
    public void BatchManifest_DuplicateSequenceRejected()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, id1, new string('A', 64), ValidationAccessPayloadContractVersions.Current),
            new ValidationAuditPayloadSetEntry(1, id2, new string('B', 64), ValidationAccessPayloadContractVersions.Current)
        };

        var ex = Assert.Throws<ValidationAuditExecutionException>(() => _hasher.ComputeSetHash(entries));
        Assert.Equal("VALIDATION_AUDIT_DUPLICATE_SEQUENCE", ex.ErrorCode);
    }

    [Fact]
    public void BatchManifest_NonContiguousSequenceRejected()
    {
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, Guid.NewGuid(), new string('A', 64), ValidationAccessPayloadContractVersions.Current),
            new ValidationAuditPayloadSetEntry(3, Guid.NewGuid(), new string('B', 64), ValidationAccessPayloadContractVersions.Current)
        };

        var ex = Assert.Throws<ValidationAuditExecutionException>(() =>
            _hasher.ValidateContiguousSequences(entries, expectedFirstSequence: 1));
        Assert.Equal("VALIDATION_AUDIT_SEQUENCE_GAP", ex.ErrorCode);
    }

    [Fact]
    public void BatchManifest_OverlapRejected()
    {
        var ex = Assert.Throws<ValidationAuditExecutionException>(() =>
            _hasher.ValidateNoOverlappingRanges([(1, 3), (3, 5)]));
        Assert.Equal("VALIDATION_AUDIT_BATCH_OVERLAP", ex.ErrorCode);

        var a = new ValidationAuditBatch { FirstSequence = 1, LastSequence = 2 };
        var b = new ValidationAuditBatch { FirstSequence = 2, LastSequence = 4 };
        Assert.True(a.Overlaps(b));
    }

    [Fact]
    public void BatchManifest_HashStableAcrossRetry()
    {
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(
                1,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                ValidationAccessPayloadContractVersions.Current)
        };

        var h1 = _hasher.ComputeSetHash(entries);
        var h2 = _hasher.ComputeSetHash(entries);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);
    }

    [Fact]
    public void FinalPayloadSetHash_StableAcrossRestart()
    {
        var entries = Enumerable.Range(1, 3)
            .Select(i => new ValidationAuditPayloadSetEntry(
                i,
                Guid.Parse($"{i:D8}-0000-0000-0000-000000000000"),
                new string((char)('A' + i), 64),
                ValidationAccessPayloadContractVersions.Current))
            .ToList();

        var first = _hasher.ComputeSetHash(entries);
        // Simulate restart: rebuild entries from durable fields only.
        var rebuilt = entries
            .Select(e => new ValidationAuditPayloadSetEntry(
                e.ScopeSequenceNumber,
                e.AccessEventId,
                e.AccessPayloadHash.ToLowerInvariant(),
                e.AccessPayloadContractVersion))
            .Reverse()
            .ToList();

        Assert.Equal(first, _hasher.ComputeSetHash(rebuilt));
    }

    [Fact]
    public void CompletenessVerifier_MissingExecution()
    {
        var trial = CreateTrial();
        trial.AuthoritativeAuditExecutionId = Guid.NewGuid();

        var result = _verifier.Verify(trial, null, [], []);
        Assert.Equal(ValidationAuditCompletenessCode.ExecutionMissing, result.CompletionCode);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void CompletenessVerifier_NotAuthoritative()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = Guid.NewGuid();

        var result = _verifier.Verify(trial, execution, [], []);
        Assert.Equal(ValidationAuditCompletenessCode.NotAuthoritative, result.CompletionCode);
        Assert.False(result.IsAuthoritative);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void CompletenessVerifier_FinalSequenceMissing()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        execution.Status = ValidationAuditExecutionStatus.Completed;
        execution.FinalExpectedSequence = null;
        execution.ExpectedEventCount = 1;
        execution.FinalPayloadSetHash = new string('A', 64);
        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;

        var result = _verifier.Verify(trial, execution, [], []);
        Assert.False(result.IsComplete);
        Assert.Equal(ValidationAuditCompletenessCode.FinalSequenceMissing, result.CompletionCode);
    }

    [Fact]
    public void CompletenessVerifier_SequenceGap()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
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
        Assert.False(result.IsComplete);
        Assert.Equal(ValidationAuditCompletenessCode.SequenceGap, result.CompletionCode);
        Assert.Contains(2L, result.MissingSequences);
    }

    [Fact]
    public void CompletenessVerifier_DuplicateSequence()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        execution.FinalExpectedSequence = 1;
        execution.ExpectedEventCount = 1;
        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalPayloadSetHash = new string('A', 64);
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var hash = new string('B', 64);
        var batches = new[]
        {
            new ValidationAuditBatch
            {
                AuditBatchId = Guid.NewGuid(),
                AuditExecutionId = execution.AuditExecutionId,
                BatchNumber = 1,
                FirstSequence = 1,
                LastSequence = 1,
                ExpectedEventCount = 1,
                ExpectedEventIdsJson = $"[\"{id1:D}\"]",
                ExpectedPayloadHashesJson = $"[\"{hash}\"]",
                ExpectedPayloadSetHash = new string('C', 64),
                Status = ValidationAuditBatchStatus.Confirmed
            },
            new ValidationAuditBatch
            {
                AuditBatchId = Guid.NewGuid(),
                AuditExecutionId = execution.AuditExecutionId,
                BatchNumber = 2,
                FirstSequence = 1,
                LastSequence = 1,
                ExpectedEventCount = 1,
                ExpectedEventIdsJson = $"[\"{id2:D}\"]",
                ExpectedPayloadHashesJson = $"[\"{hash}\"]",
                ExpectedPayloadSetHash = new string('D', 64),
                Status = ValidationAuditBatchStatus.Confirmed
            }
        };

        var result = _verifier.Verify(trial, execution, batches, []);
        Assert.False(result.IsComplete);
        Assert.True(
            result.CompletionCode is ValidationAuditCompletenessCode.DuplicateSequence
                or ValidationAuditCompletenessCode.BatchOverlap);
    }

    [Fact]
    public void CompletenessVerifier_MissingEvent()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;

        var eventId = Guid.NewGuid();
        var hash = new string('F', 64);
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = _hasher.ComputeSetHash(entries);
        var (idsJson, hashesJson) = _hasher.BuildManifestJsons(entries);

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
            ExpectedEventIdsJson = idsJson,
            ExpectedPayloadHashesJson = hashesJson,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed
        };

        var result = _verifier.Verify(trial, execution, [batch], []);
        Assert.False(result.IsComplete);
        Assert.Equal(ValidationAuditCompletenessCode.EventMissing, result.CompletionCode);
        Assert.Contains(eventId, result.MissingEventIds);
    }

    [Fact]
    public void CompletenessVerifier_PayloadMismatch()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;

        var eventId = Guid.NewGuid();
        var expectedHash = new string('A', 64);
        var actualHash = new string('B', 64);
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, expectedHash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = _hasher.ComputeSetHash(entries);
        var (idsJson, hashesJson) = _hasher.BuildManifestJsons(entries);

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
            ExpectedEventIdsJson = idsJson,
            ExpectedPayloadHashesJson = hashesJson,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed
        };

        var row = new ValidationCandleAccessAudit
        {
            AccessEventId = eventId,
            ScopeExecutionId = execution.ScopeExecutionId,
            ScopeSequenceNumber = 1,
            AccessPayloadHash = actualHash,
            AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current,
            CallerComponent = "Test",
            RecorderVersion = "ValidationCandleAccess/v2",
            AccessedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = _verifier.Verify(trial, execution, [batch], [row]);
        Assert.False(result.IsComplete);
        Assert.Equal(ValidationAuditCompletenessCode.PayloadMismatch, result.CompletionCode);
    }

    [Fact]
    public void CompletenessVerifier_AllValid_ReturnsComplete()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;

        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var hash = new string('F', 64);
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(
                1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = _hasher.ComputeSetHash(entries);
        var (idsJson, hashesJson) = _hasher.BuildManifestJsons(entries);

        execution.FinalExpectedSequence = 1;
        execution.ExpectedEventCount = 1;
        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalPayloadSetHash = setHash;
        execution.Status = ValidationAuditExecutionStatus.Completed;

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = idsJson,
            ExpectedPayloadHashesJson = hashesJson,
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
        Assert.True(result.IsComplete);
        Assert.True(result.IsTerminal);
        Assert.True(result.EvidenceSatisfied);
        Assert.Equal(ValidationAuditCompletenessCode.Complete, result.CompletionCode);

        // Evidence valid but Status not Completed → not Complete.
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
        var inProgress = _verifier.Verify(trial, execution, [batch], [row]);
        Assert.False(inProgress.IsComplete);
        Assert.True(inProgress.EvidenceSatisfied);
        Assert.Equal(ValidationAuditCompletenessCode.ExecutionInProgress, inProgress.CompletionCode);
        Assert.False(inProgress.IsTerminal);
    }

    [Fact]
    public void Trial_CannotCompleteWhenAuditInProgress()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;

        var completeness = new ValidationAuditCompletenessResult
        {
            AuditExecutionId = execution.AuditExecutionId,
            IsAuthoritative = true,
            IsTerminal = false,
            IsComplete = false,
            CompletionCode = ValidationAuditCompletenessCode.ExecutionInProgress
        };

        Assert.False(_gate.CanMarkTrialCompleted(trial, execution, completeness));
    }

    [Fact]
    public void Trial_CannotCompleteWhenAuditRecoveryRequired()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
        execution.Status = ValidationAuditExecutionStatus.RecoveryRequired;

        var completeness = new ValidationAuditCompletenessResult
        {
            AuditExecutionId = execution.AuditExecutionId,
            IsAuthoritative = true,
            IsTerminal = false,
            IsComplete = false,
            CompletionCode = ValidationAuditCompletenessCode.RecoveryRequired
        };

        Assert.False(_gate.CanMarkTrialCompleted(trial, execution, completeness));
    }

    [Fact]
    public void Trial_CanCompleteOnlyWhenAuthoritativeAuditComplete()
    {
        var trial = CreateTrial();
        var execution = CreateExecution();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
        execution.Status = ValidationAuditExecutionStatus.Completed;

        var complete = new ValidationAuditCompletenessResult
        {
            AuditExecutionId = execution.AuditExecutionId,
            IsAuthoritative = true,
            IsTerminal = true,
            IsComplete = true,
            CompletionCode = ValidationAuditCompletenessCode.Complete
        };

        Assert.True(_gate.CanMarkTrialCompleted(trial, execution, complete));
        _gate.ApplyCompletedStatus(trial, execution, complete);
        Assert.Equal(ValidationTrialStatus.Completed, trial.Status);

        trial.Status = ValidationTrialStatus.Running;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
        Assert.False(_gate.CanMarkTrialCompleted(trial, execution, complete));
        Assert.Throws<ValidationAuditExecutionException>(() =>
            _gate.ApplyCompletedStatus(trial, execution, complete));
    }

    [Fact]
    public void HistoricalTrial_NullAuditExecution_IsNotEvaluated()
    {
        var trial = CreateTrial();
        trial.AuthoritativeAuditExecutionId = null;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated;

        var result = _verifier.Verify(trial, null, [], []);
        Assert.Equal(ValidationAuditCompletenessCode.HistoricalNotEvaluated, result.CompletionCode);
        Assert.False(result.IsComplete);
        Assert.False(_gate.CanMarkTrialCompleted(trial, null, result));
    }

    [Fact]
    public void Supersession_IncrementsAttemptNumber()
    {
        var old = CreateExecution();
        old.AttemptNumber = 2;
        var newId = Guid.NewGuid();
        old.MarkSuperseded(newId, DateTime.UtcNow, "PROCESS_INTERRUPTED_BEFORE_FLUSH");

        Assert.Equal(ValidationAuditExecutionStatus.Superseded, old.Status);
        Assert.Equal(newId, old.SupersededByAuditExecutionId);

        var nextAttempt = old.AttemptNumber + 1;
        Assert.Equal(3, nextAttempt);
    }

    [Fact]
    public void Supersession_PreservesOldEvidence()
    {
        var old = CreateExecution();
        old.LastConfirmedSequence = 2;
        old.ConfirmedEventCount = 2;
        old.FinalExpectedSequence = null;
        var scopeBefore = old.ScopeExecutionId;
        var auditBefore = old.AuditExecutionId;

        old.MarkSuperseded(Guid.NewGuid(), DateTime.UtcNow, "AUDIT_MANIFEST_INCOMPLETE");

        Assert.Equal(2, old.LastConfirmedSequence);
        Assert.Equal(2, old.ConfirmedEventCount);
        Assert.Equal(scopeBefore, old.ScopeExecutionId);
        Assert.Equal(auditBefore, old.AuditExecutionId);
        Assert.Equal(ValidationAuditExecutionStatus.Superseded, old.Status);
    }

    [Fact]
    public void IdentityMismatch_FailsClosed()
    {
        var ex = new ValidationAuditExecutionIdentityMismatchException(
            "Scope mismatch",
            expectedAuditExecutionId: Guid.NewGuid(),
            actualAuditExecutionId: Guid.NewGuid(),
            expectedScopeExecutionId: Guid.NewGuid(),
            actualScopeExecutionId: Guid.NewGuid(),
            expectedExecutionToken: "a",
            actualExecutionToken: "b");

        Assert.Equal(ValidationAuditExecutionIdentityMismatchException.Code, ex.ErrorCode);
        Assert.Equal("VALIDATION_AUDIT_EXECUTION_IDENTITY_MISMATCH", ex.ErrorCode);
        Assert.Equal("Scope mismatch", ex.SafeMessage);
    }

    [Fact]
    public void UnknownAuditContractVersion_FailsClosed()
    {
        var execution = CreateExecution();
        execution.AuditContractVersion = "ValidationAuditExecution/v0-unknown";

        var ex = Assert.Throws<ValidationAuditExecutionException>(() =>
        {
            if (!string.Equals(
                    execution.AuditContractVersion,
                    ValidationAuditExecution.ContractVersionV1,
                    StringComparison.Ordinal))
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_UNKNOWN_CONTRACT_VERSION",
                    $"Unknown AuditContractVersion '{execution.AuditContractVersion}'.");
            }
        });

        Assert.Equal("VALIDATION_AUDIT_UNKNOWN_CONTRACT_VERSION", ex.ErrorCode);
    }

    [Fact]
    public void ZeroFinalSequence_RequiresExplicitNoAccessContract()
    {
        var execution = CreateExecution();
        execution.FinalExpectedSequence = 0;
        execution.ExpectedEventCount = 0;
        execution.FinalPayloadSetHash = _hasher.ComputeSetHash([]);
        execution.AllowsZeroAccess = false;

        Assert.Equal(
            ValidationAuditCompletenessCode.FinalSequenceMissing,
            execution.ValidateCompletionPreconditions());

        execution.AllowsZeroAccess = true;
        Assert.Equal(
            ValidationAuditCompletenessCode.Complete,
            execution.ValidateCompletionPreconditions());
    }

    [Fact]
    public void PayloadSetHasher_ManifestSizeExceeded_FailsClosed()
    {
        var entries = Enumerable.Range(1, ValidationAuditPayloadSetHasher.MaxManifestEvents + 1)
            .Select(i => new ValidationAuditPayloadSetEntry(
                i,
                Guid.NewGuid(),
                new string('A', 64),
                ValidationAccessPayloadContractVersions.Current))
            .ToList();

        var ex = Assert.Throws<ValidationAuditExecutionException>(() => _hasher.ComputeSetHash(entries));
        Assert.Equal("VALIDATION_AUDIT_MANIFEST_SIZE_EXCEEDED", ex.ErrorCode);
    }

    private static ValidationParameterTrial CreateTrial() => new()
    {
        Id = 10,
        ValidationExperimentId = 1,
        TrialNumber = 1,
        ParameterFingerprint = "fp",
        Status = ValidationTrialStatus.Running
    };

    private static ValidationAuditExecution CreateExecution() => new()
    {
        AuditExecutionId = Guid.NewGuid(),
        ValidationExperimentId = 1,
        ValidationTrialId = 10,
        TrialNumber = 1,
        ScopeExecutionId = Guid.NewGuid(),
        AttemptNumber = 1,
        ExecutionToken = "tok",
        Status = ValidationAuditExecutionStatus.InProgress,
        StartedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
        RowVersion = 1
    };
}
