using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C2B — phase-aware identity, cleanup outcomes, and revalidation helpers.</summary>
public sealed class Milestone230E2C2BFailureAggregateUnitTests
{
    private static readonly DateTime FixedUtc = new(2026, 7, 27, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LogicalIdentity_IncludesPrecedenceCodeAndPhase()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new InvalidOperationException("hb"),
            ValidationTrainingFailurePhase.LeaseHeartbeat,
            occurredAtUtc: FixedUtc);
        aggregate.Observe(
            new InvalidOperationException("rel"),
            ValidationTrainingFailurePhase.LeaseRelease,
            occurredAtUtc: FixedUtc.AddSeconds(1));

        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.Equal(
            $"{(int)ValidationTrainingFailurePrecedence.Cleanup}:{ValidationTrainingFailureCodes.TrainingCleanupFailed}:{ValidationTrainingFailurePhase.LeaseHeartbeat}",
            aggregate.AllFailures[0].LogicalIdentity);
        Assert.Equal(
            $"{(int)ValidationTrainingFailurePrecedence.Cleanup}:{ValidationTrainingFailureCodes.TrainingCleanupFailed}:{ValidationTrainingFailurePhase.LeaseRelease}",
            aggregate.AllFailures[1].LogicalIdentity);
    }

    [Fact]
    public void OperationStatusHeartbeatAndReleaseFailures_RetainDistinctPhases()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(new InvalidOperationException("op"), ValidationTrainingFailurePhase.OperationStatusSync, occurredAtUtc: FixedUtc);
        aggregate.Observe(new InvalidOperationException("hb"), ValidationTrainingFailurePhase.LeaseHeartbeat, occurredAtUtc: FixedUtc.AddSeconds(1));
        aggregate.Observe(new InvalidOperationException("rel"), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc.AddSeconds(2));

        Assert.Equal(3, aggregate.AllFailures.Count);
        Assert.Equal(
            [
                ValidationTrainingFailurePhase.OperationStatusSync,
                ValidationTrainingFailurePhase.LeaseHeartbeat,
                ValidationTrainingFailurePhase.LeaseRelease
            ],
            aggregate.AllFailures.Select(f => f.Phase).ToArray());
        Assert.All(aggregate.AllFailures, f => Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, f.Code));
        Assert.Equal(3, aggregate.AllFailures.Select(f => f.LogicalIdentity).Distinct().Count());
    }

    [Fact]
    public void RepeatedSamePhaseFailure_RemainsIdempotent()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(new InvalidOperationException("a"), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc);
        aggregate.Observe(new InvalidOperationException("b"), ValidationTrainingFailurePhase.LeaseRelease, occurredAtUtc: FixedUtc.AddSeconds(5));

        Assert.Single(aggregate.AllFailures);
        Assert.Equal(ValidationTrainingFailurePhase.LeaseRelease, aggregate.PrimaryFailure!.Phase);
    }

    [Fact]
    public void GenericFinalizerException_IsAuditDurability()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new InvalidOperationException("generic finalizer"),
            ValidationTrainingFailurePhase.AuditFinalization,
            occurredAtUtc: FixedUtc);

        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, aggregate.PrimaryFailure!.Category);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, aggregate.PrimaryFailure.Code);
        Assert.Equal(ValidationTrainingFailurePhase.AuditFinalization, aggregate.PrimaryFailure.Phase);
        Assert.False(aggregate.HasCleanupFailure);
    }

    [Fact]
    public void GenericVerifierException_IsAuditDurability()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new InvalidOperationException("generic verifier"),
            ValidationTrainingFailurePhase.CompletenessVerification,
            occurredAtUtc: FixedUtc);

        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, aggregate.PrimaryFailure!.Category);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, aggregate.PrimaryFailure.Code);
        Assert.Equal(ValidationTrainingFailurePhase.CompletenessVerification, aggregate.PrimaryFailure.Phase);
        Assert.False(aggregate.HasCleanupFailure);
    }

    [Fact]
    public void RevalidationFailure_AppendsRankIneligibleReasons()
    {
        var trial = new ValidationParameterTrial
        {
            RankIneligibleReasonsJson = """["ExistingReason"]"""
        };

        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
            trial,
            ["SequenceGap", "ExistingReason"]);

        var codes = System.Text.Json.JsonSerializer.Deserialize<string[]>(trial.RankIneligibleReasonsJson!)!;
        Assert.Contains("ExistingReason", codes);
        Assert.Contains("SequenceGap", codes);
        Assert.Equal(2, codes.Length);
    }

    [Fact]
    public void CleanupPersistenceFailure_DoesNotMaskOriginalPrimary()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new ValidationDataLeakageException(
                validationExperimentId: 1,
                validationBoundaryUtc: FixedUtc,
                callerComponent: "E2C2B",
                requestedStartUtc: FixedUtc,
                requestedEndUtc: FixedUtc.AddHours(1),
                message: "boundary"),
            ValidationTrainingFailurePhase.TrialBody,
            occurredAtUtc: FixedUtc);
        aggregate.Observe(
            new OperationCanceledException("release"),
            ValidationTrainingFailurePhase.LeaseRelease,
            occurredAtUtc: FixedUtc.AddSeconds(1));

        // Persistence of the cleanup failure itself also fails — capture separately.
        aggregate.Observe(
            new InvalidOperationException("persist failed"),
            ValidationTrainingFailurePhase.ExperimentStatusPersistence,
            occurredAtUtc: FixedUtc.AddSeconds(2));

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(3, aggregate.AllFailures.Count);
        Assert.Contains(aggregate.AllFailures, f => f.Phase == ValidationTrainingFailurePhase.LeaseRelease);
        Assert.Contains(aggregate.AllFailures, f => f.Phase == ValidationTrainingFailurePhase.ExperimentStatusPersistence);

        var outcome = ValidationTrainingCleanupOutcome.Failed(
            aggregate.PrimaryFailure.Code,
            aggregate.PrimaryFailure.UserSafeMessage,
            aggregate);
        Assert.False(outcome.Succeeded);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, outcome.ErrorCode);
        Assert.DoesNotContain("persist failed", outcome.UserSafeErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupOnly_OutcomeIsFailedAndNonQualified()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new InvalidOperationException("lease release failed"),
            ValidationTrainingFailurePhase.LeaseRelease,
            occurredAtUtc: FixedUtc);

        var experiment = new ValidationExperiment
        {
            Id = 42,
            Status = ValidationExperimentStatus.TrainingCompleted,
            IsQualificationCapable = true
        };
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
        Assert.False(experiment.IsQualificationCapable);
        Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, experiment.PrimaryFailureReason);

        var outcome = ValidationTrainingCleanupOutcome.Failed(
            ValidationTrainingFailureCodes.TrainingCleanupFailed,
            ValidationTrainingFailureHandler.UserSafeCleanupMessage,
            aggregate);
        Assert.False(outcome.Succeeded);
        Assert.False(ValidationLifecycleGate.CanFreeze(ValidationExperimentStatus.Failed));
    }
}
