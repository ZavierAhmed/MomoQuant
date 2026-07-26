using MomoQuant.Application.Common;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Bounded retry policy dedicated to validation access-evidence persistence.
/// Retries only transient or commit-outcome-unknown conditions; never payload/input conflicts
/// or permanent integrity failures. Attempt counts are total attempts, not retries.
/// </summary>
public interface IValidationAccessPersistenceRetryPolicy
{
    int MaxPersistenceAttempts { get; }

    int MaxConfirmationAttempts { get; }

    /// <summary>
    /// Bound for recovery confirmation performed after an ambiguous commit when the caller token
    /// is already cancelled. Recovery must never hang indefinitely.
    /// </summary>
    TimeSpan RecoveryConfirmationTimeout { get; }

    bool IsRetryEligible(Exception exception);

    Task DelayAsync(int completedAttempt, CancellationToken cancellationToken);
}

public sealed class ValidationAccessPersistenceRetryPolicy : IValidationAccessPersistenceRetryPolicy
{
    public const int DefaultMaxPersistenceAttempts = 3;
    public const int DefaultMaxConfirmationAttempts = 3;
    public static readonly TimeSpan DefaultRecoveryConfirmationTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<int, CancellationToken, Task>? _delay;

    public ValidationAccessPersistenceRetryPolicy(
        int maxPersistenceAttempts = DefaultMaxPersistenceAttempts,
        int maxConfirmationAttempts = DefaultMaxConfirmationAttempts,
        TimeSpan? recoveryConfirmationTimeout = null,
        Func<int, CancellationToken, Task>? delay = null)
    {
        if (maxPersistenceAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPersistenceAttempts));
        }

        if (maxConfirmationAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConfirmationAttempts));
        }

        MaxPersistenceAttempts = maxPersistenceAttempts;
        MaxConfirmationAttempts = maxConfirmationAttempts;
        RecoveryConfirmationTimeout = recoveryConfirmationTimeout ?? DefaultRecoveryConfirmationTimeout;
        _delay = delay;
    }

    public int MaxPersistenceAttempts { get; }

    public int MaxConfirmationAttempts { get; }

    public TimeSpan RecoveryConfirmationTimeout { get; }

    public bool IsRetryEligible(Exception exception)
    {
        // Never retry fail-closed conditions.
        if (exception is ValidationAccessInputBatchConflictException
            or ValidationAccessPersistedPayloadConflictException)
        {
            return false;
        }

        if (exception is ValidationAccessCommitOutcomeUnknownException)
        {
            return true;
        }

        return TransientDatabaseRetryPolicy.IsTransient(exception);
    }

    public Task DelayAsync(int completedAttempt, CancellationToken cancellationToken)
    {
        if (_delay is not null)
        {
            return _delay(completedAttempt, cancellationToken);
        }

        var milliseconds = Math.Min(1000, 100 * completedAttempt);
        return Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
    }
}
