using MomoQuant.Application.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C2C — structural phase classification without message inference.</summary>
public sealed class Milestone230E2C2CPhaseClassificationUnitTests
{
    [Fact]
    public void GenericVerifierException_WithUnrelatedMessage_IsCompletenessVerification()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new ValidationAuditCompletenessVerificationException("boom"),
            ValidationTrainingFailurePhase.CompletenessVerification);

        var primary = aggregate.PrimaryFailure;
        Assert.NotNull(primary);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, primary!.Code);
        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, primary.Category);
        Assert.Equal(ValidationTrainingFailurePhase.CompletenessVerification, primary.Phase);
        Assert.DoesNotContain("boom", primary.UserSafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericFinalizerException_WithUnrelatedMessage_IsAuditFinalization()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new InvalidOperationException("database unavailable"),
            ValidationTrainingFailurePhase.AuditFinalization);

        var primary = aggregate.PrimaryFailure;
        Assert.NotNull(primary);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, primary!.Code);
        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, primary.Category);
        Assert.Equal(ValidationTrainingFailurePhase.AuditFinalization, primary.Phase);
        Assert.DoesNotContain("database unavailable", primary.UserSafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceFailure_DoesNotReplaceBoundaryPrimary()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.Observe(
            new ValidationDataLeakageException(
                1,
                DateTime.UtcNow,
                "caller",
                DateTime.UtcNow,
                null,
                "ValidationDataLeakageDetected"),
            ValidationTrainingFailurePhase.TrialBody);
        aggregate.Observe(
            new InvalidOperationException("E2C2 simulated trial persistence failure."),
            ValidationTrainingFailurePhase.TrialStatusPersistence);

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.Equal(ValidationTrainingFailurePhase.TrialStatusPersistence, aggregate.AllFailures[1].Phase);
        Assert.DoesNotContain(
            "E2C2 simulated",
            aggregate.PrimaryFailure.UserSafeMessage,
            StringComparison.Ordinal);
    }
}
