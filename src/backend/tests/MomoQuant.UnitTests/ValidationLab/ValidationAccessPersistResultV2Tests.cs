using MomoQuant.Application.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E2B WP5 — v2 result contract truthfulness.
/// ID existence alone must never produce IsFullyConfirmed.
/// </summary>
public sealed class ValidationAccessPersistResultV2Tests
{
    private static readonly Guid Id1 = Guid.NewGuid();
    private static readonly Guid Id2 = Guid.NewGuid();

    [Fact]
    public void ContractVersion_IsV2()
    {
        Assert.Equal("ValidationAccessBatchPersistResult/v2", new ValidationAccessBatchPersistResult().ResultContractVersion);
    }

    [Fact]
    public void IsFullyConfirmed_RequiresFullyPayloadConfirmedVerificationStatus()
    {
        // Every requested ID is "confirmed", but verification never reached FullyPayloadConfirmed
        // (e.g. only ID existence was checked). This must NOT count as full confirmation.
        var idOnly = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1, Id2 },
            ConfirmedMatchingEventIds = new[] { Id1, Id2 },
            CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
            VerificationStatus = ValidationAccessBatchVerificationStatus.NotAttempted
        };
        Assert.False(idOnly.IsFullyConfirmed);
    }

    [Fact]
    public void IsFullyConfirmed_True_OnlyWithFullPayloadConfirmation()
    {
        var confirmed = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1, Id2 },
            ConfirmedMatchingEventIds = new[] { Id2, Id1 },
            CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
            VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed
        };
        Assert.True(confirmed.IsFullyConfirmed);
    }

    [Fact]
    public void PartialPayloadConfirmation_IsNotFullyConfirmed()
    {
        var partial = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1, Id2 },
            ConfirmedMatchingEventIds = new[] { Id1 },
            MissingEventIds = new[] { Id2 },
            CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
            VerificationStatus = ValidationAccessBatchVerificationStatus.PartiallyPayloadConfirmed
        };
        Assert.False(partial.IsFullyConfirmed);
        Assert.Equal(1, partial.MissingCount);
    }

    [Fact]
    public void ConfirmationUnavailable_IsNotFullyConfirmed()
    {
        var unavailable = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1 },
            CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
            VerificationStatus = ValidationAccessBatchVerificationStatus.ConfirmationUnavailable
        };
        Assert.False(unavailable.IsFullyConfirmed);
    }

    [Fact]
    public void PayloadConflict_IsNotFullyConfirmed_EvenWithMatchingSets()
    {
        var conflict = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1 },
            ConfirmedMatchingEventIds = new[] { Id1 },
            PayloadConflictEventIds = new[] { Id1 },
            CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
            VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed
        };
        Assert.False(conflict.IsFullyConfirmed);
    }

    [Fact]
    public void InputConflict_IsNotFullyConfirmed()
    {
        var conflict = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1 },
            ConfirmedMatchingEventIds = new[] { Id1 },
            InputConflictEventIds = new[] { Id1 },
            CommitStatus = ValidationAccessBatchCommitStatus.NotAttempted,
            VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed
        };
        Assert.False(conflict.IsFullyConfirmed);
    }

    [Fact]
    public void MissingRequestedId_IsNotFullyConfirmed()
    {
        var missing = new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = new[] { Id1, Id2 },
            ConfirmedMatchingEventIds = new[] { Id1 },
            VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed
        };
        Assert.False(missing.IsFullyConfirmed);
    }

    [Fact]
    public void CommitStatusValues_DistinguishAllRequiredOutcomes()
    {
        var values = Enum.GetNames<ValidationAccessBatchCommitStatus>();
        Assert.Contains("NotAttempted", values);
        Assert.Contains("CommitSucceeded", values);
        Assert.Contains("CommitOutcomeUnknown", values);
        Assert.Contains("KnownRolledBack", values);
        Assert.Contains("FailedPermanent", values);
    }

    [Fact]
    public void VerificationAndRecoveryStatusValues_DistinguishAllRequiredOutcomes()
    {
        var verification = Enum.GetNames<ValidationAccessBatchVerificationStatus>();
        Assert.Contains("FullyPayloadConfirmed", verification);
        Assert.Contains("PartiallyPayloadConfirmed", verification);
        Assert.Contains("ConfirmationUnavailable", verification);
        Assert.Contains("PayloadConflict", verification);
        Assert.Contains("InputConflict", verification);

        var recovery = Enum.GetNames<ValidationAccessBatchRecoveryStatus>();
        Assert.Contains("ConfirmedAfterNormalCommit", recovery);
        Assert.Contains("ConfirmedAfterAmbiguousCommit", recovery);
        Assert.Contains("MissingEventsRetriedAndConfirmed", recovery);
        Assert.Contains("RetryExhausted", recovery);
    }

    [Fact]
    public void RetryPolicy_DefaultsAndEligibilityRules()
    {
        var policy = new ValidationAccessPersistenceRetryPolicy();
        Assert.Equal(3, policy.MaxPersistenceAttempts);
        Assert.Equal(3, policy.MaxConfirmationAttempts);
        Assert.True(policy.RecoveryConfirmationTimeout > TimeSpan.Zero);

        var result = new ValidationAccessBatchPersistResult();
        Assert.True(policy.IsRetryEligible(
            new ValidationAccessCommitOutcomeUnknownException("ambiguous")));
        Assert.False(policy.IsRetryEligible(
            new ValidationAccessInputBatchConflictException(
                Guid.NewGuid(), Array.Empty<string>(), new[] { "F" }, result)));
        Assert.False(policy.IsRetryEligible(
            new ValidationAccessPersistedPayloadConflictException(
                Guid.NewGuid(), "A", "B", new[] { "F" }, result)));
        Assert.False(policy.IsRetryEligible(new InvalidOperationException("permanent schema failure")));
        Assert.True(policy.IsRetryEligible(new TimeoutException("transient failure while connecting")));
    }

    [Fact]
    public void TypedExceptions_DeriveFromEvidencePersistenceBase_AndCarrySafeCodes()
    {
        var result = new ValidationAccessBatchPersistResult();

        var input = new ValidationAccessInputBatchConflictException(
            Guid.NewGuid(), new[] { "H1", "H2" }, new[] { "ScopeSequenceNumber" }, result);
        Assert.IsAssignableFrom<ValidationAccessEvidencePersistenceException>(input);
        Assert.Equal("VALIDATION_ACCESS_INPUT_BATCH_CONFLICT", input.ErrorCode);

        var persisted = new ValidationAccessPersistedPayloadConflictException(
            Guid.NewGuid(), "H1", "H2", new[] { "CallerComponent" }, result);
        Assert.IsAssignableFrom<ValidationAccessEvidencePersistenceException>(persisted);
        Assert.Equal("VALIDATION_ACCESS_PERSISTED_PAYLOAD_CONFLICT", persisted.ErrorCode);
        Assert.Contains("CallerComponent", persisted.SafeMessage);
        Assert.DoesNotContain("H1", persisted.Message); // hashes are not leaked into messages

        var unavailable = new ValidationAccessConfirmationUnavailableException(result, null);
        Assert.IsAssignableFrom<ValidationAccessEvidencePersistenceException>(unavailable);
        Assert.Equal("VALIDATION_ACCESS_CONFIRMATION_UNAVAILABLE", unavailable.ErrorCode);

        var exhausted = new ValidationAccessPersistenceRetryExhaustedException(3, result, null);
        Assert.IsAssignableFrom<ValidationAccessEvidencePersistenceException>(exhausted);
        Assert.Equal("VALIDATION_ACCESS_PERSISTENCE_RETRY_EXHAUSTED", exhausted.ErrorCode);
        Assert.Equal(3, exhausted.Attempts);

        var unknown = new ValidationAccessCommitOutcomeUnknownException("ambiguous");
        Assert.True(unknown.CommitMayHaveSucceeded);
        Assert.Equal("VALIDATION_ACCESS_COMMIT_OUTCOME_UNKNOWN", unknown.ErrorCode);
    }
}
