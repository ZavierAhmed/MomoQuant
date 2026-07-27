using System.Runtime.ExceptionServices;
using System.Text.Json;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C2 — failure aggregate precedence, idempotency, and JSON merge behavior.</summary>
public sealed class Milestone230E2C2FailureAggregateUnitTests
{
    private static readonly DateTime FixedUtc = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BoundaryAndAuditFailure_BoundaryIsPrimaryAndBothAreRetained()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            BoundaryException(),
            ValidationTrainingFailurePhase.TrialBody,
            occurredAtUtc: FixedUtc);
        aggregate.Observe(
            AuditException(),
            ValidationTrainingFailurePhase.TrialScopeFlush,
            occurredAtUtc: FixedUtc.AddSeconds(1));

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(ValidationTrainingFailureCategory.Boundary, aggregate.PrimaryFailure.Category);
        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.ValidationDataLeakage);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
        Assert.True(aggregate.IsQualificationBlocking);
    }

    [Fact]
    public void TrialAndAuditFailure_AuditIsPrimaryAndBothAreRetained()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            TrialException(),
            ValidationTrainingFailurePhase.TrialBody,
            occurredAtUtc: FixedUtc);
        aggregate.Observe(
            AuditException(),
            ValidationTrainingFailurePhase.TrialScopeFlush,
            occurredAtUtc: FixedUtc.AddSeconds(1));

        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, aggregate.PrimaryFailure!.Code);
        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, aggregate.PrimaryFailure.Category);
        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.TrialExecutionFailed);
    }

    [Fact]
    public void BoundaryAuditAndCleanup_FollowsDeterministicPrecedence()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(new OperationCanceledException(), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc.AddSeconds(3));
        aggregate.Observe(TrialException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc.AddSeconds(2));
        aggregate.Observe(AuditException(), ValidationTrainingFailurePhase.TrialScopeFlush, occurredAtUtc: FixedUtc.AddSeconds(1));
        aggregate.Observe(BoundaryException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc);

        var ordered = aggregate.AllFailures.Select(f => f.Code).ToList();
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(
            [
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                ValidationTrainingFailureCodes.TrialExecutionFailed,
                ValidationTrainingFailureCodes.TrainingCleanupFailed
            ],
            ordered);
    }

    [Fact]
    public void TrialAndCleanup_TrialIsPrimary()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(new OperationCanceledException(), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc.AddSeconds(1));
        aggregate.Observe(TrialException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc);

        Assert.Equal(ValidationTrainingFailureCodes.TrialExecutionFailed, aggregate.PrimaryFailure!.Code);
        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.True(aggregate.HasTrialExecutionFailure);
        Assert.True(aggregate.HasCleanupFailure);
    }

    [Fact]
    public void CleanupOnly_CleanupIsPrimary()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(new OperationCanceledException(), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc);

        Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, aggregate.PrimaryFailure!.Code);
        Assert.Equal(ValidationTrainingFailureCategory.Cleanup, aggregate.PrimaryFailure.Category);
        Assert.False(aggregate.PrimaryFailure.IsQualificationBlocking);
        Assert.False(aggregate.IsQualificationBlocking);
    }

    [Fact]
    public void DuplicateFailureCode_IsStoredOnce()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(AuditException(), ValidationTrainingFailurePhase.TrialScopeFlush, occurredAtUtc: FixedUtc);
        aggregate.Observe(AuditException(), ValidationTrainingFailurePhase.TrialScopeFlush, occurredAtUtc: FixedUtc.AddSeconds(5));

        Assert.Single(aggregate.AllFailures);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, aggregate.PrimaryFailure!.Code);
    }

    [Fact]
    public void ExistingFailureReasons_ArePreservedAndReorderedByPrecedence()
    {
        var existingJson = ValidationTrainingFailureJson.SerializeRecords(
        [
            new ValidationTrainingFailureRecord
            {
                Code = ValidationTrainingFailureCodes.TrialExecutionFailed,
                Category = ValidationTrainingFailureCategory.TrialExecution,
                Precedence = ValidationTrainingFailurePrecedence.TrialExecution,
                Phase = ValidationTrainingFailurePhase.TrialBody,
                UserSafeMessage = "Trial failed earlier.",
                OccurredAtUtc = FixedUtc.AddMinutes(-5),
                IsQualificationBlocking = false
            }
        ]);

        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(existingJson);
        aggregate.Observe(AuditException(), ValidationTrainingFailurePhase.TrialScopeFlush, occurredAtUtc: FixedUtc);
        aggregate.Observe(BoundaryException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc.AddSeconds(1));

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(3, aggregate.AllFailures.Count);
        Assert.Equal(
            ValidationTrainingFailureCodes.ValidationDataLeakage,
            aggregate.AllFailures[0].Code);
        Assert.Equal(
            ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
            aggregate.AllFailures[1].Code);
        Assert.Equal(
            ValidationTrainingFailureCodes.TrialExecutionFailed,
            aggregate.AllFailures[2].Code);
    }

    [Fact]
    public void RepeatedAggregation_IsIdempotent()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(BoundaryException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc);
        aggregate.Observe(AuditException(), ValidationTrainingFailurePhase.TrialScopeFlush, occurredAtUtc: FixedUtc.AddSeconds(1));

        var secondPass = new ValidationTrainingFailureAggregate();
        secondPass.MergeFrom(aggregate);
        secondPass.MergeFrom(aggregate);

        Assert.Equal(aggregate.AllFailures.Count, secondPass.AllFailures.Count);
        Assert.Equal(aggregate.PrimaryFailure!.LogicalIdentity, secondPass.PrimaryFailure!.LogicalIdentity);
        Assert.Equal(
            aggregate.AllFailures.Select(f => f.LogicalIdentity),
            secondPass.AllFailures.Select(f => f.LogicalIdentity));
    }

    [Fact]
    public void LowerPrecedenceFailure_CannotReplacePrimary()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(BoundaryException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc);
        aggregate.Observe(TrialException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc.AddSeconds(1));
        aggregate.Observe(new OperationCanceledException(), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc.AddSeconds(2));

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(3, aggregate.AllFailures.Count);
    }

    [Fact]
    public void MalformedExistingFailureJson_FailsSafelyWithoutMaskingOriginalFailure()
    {
        var aggregate = ValidationTrainingFailurePersistence.MergeExisting("{ not-valid-json ");
        aggregate.Observe(BoundaryException(), ValidationTrainingFailurePhase.TrialBody, occurredAtUtc: FixedUtc);

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Contains(
            aggregate.AllFailures,
            f => f.Code == ValidationTrainingFailureCodes.TrainingCleanupFailed);
        Assert.True(aggregate.IsQualificationBlocking);
    }

    [Fact]
    public void BodyExceptionStackOrigin_RemainsAfterSimultaneousFlushFailure()
    {
        var body = new InvalidOperationException("Trial body marker");
        var flush = AuditException();
        var bodyDispatch = ExceptionDispatchInfo.Capture(body);
        var flushDispatch = ExceptionDispatchInfo.Capture(flush);

        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.ObserveDispatchInfo(bodyDispatch, ValidationTrainingFailurePhase.TrialBody);
        aggregate.ObserveDispatchInfo(flushDispatch, ValidationTrainingFailurePhase.TrialScopeFlush);

        var thrown = Assert.Throws<ValidationAccessEvidencePersistenceException>(() =>
            aggregate.ThrowPrimary(bodyDispatch, flushDispatch));

        Assert.Same(flush, thrown);
        Assert.Equal("Trial body marker", body.Message);

        var experiment = new ValidationExperiment
        {
            Id = 42,
            DiagnosticsJson = "[]"
        };
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, experiment.PrimaryFailureReason);
        Assert.False(experiment.IsQualificationCapable);
    }

    private static ValidationDataLeakageException BoundaryException() =>
        new(
            validationExperimentId: 1,
            validationBoundaryUtc: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            callerComponent: "E2C2Test",
            requestedStartUtc: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            requestedEndUtc: null,
            message: "ValidationDataLeakageDetected");

    private static ValidationAccessEvidencePersistenceException AuditException()
    {
        var eventId = Guid.NewGuid();
        return new ValidationAccessEvidencePersistenceException(new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = [eventId],
            MissingEventIds = [eventId],
            CommitStatus = ValidationAccessBatchCommitStatus.FailedPermanent,
            VerificationStatus = ValidationAccessBatchVerificationStatus.FailedPermanent,
            RecoveryStatus = ValidationAccessBatchRecoveryStatus.RetryExhausted,
            CompletedAtUtc = FixedUtc
        });
    }

    private static InvalidOperationException TrialException() =>
        new("Validation training trial execution failed.");
}

/// <summary>Shared by unit tests; integration infrastructure exposes the same helpers publicly.</summary>
internal static class E2C2FailureReasonTestHelpers
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
}
