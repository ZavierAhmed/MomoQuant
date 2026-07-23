using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationTrainingDurabilityTests
{
    [Fact]
    public void ProgressCalculator_UsesPersistedTerminalTrials()
    {
        var experiment = new ValidationExperiment
        {
            Id = 23,
            Name = "c2",
            StrategyCode = "TEST",
            MaximumTrials = 25,
            PercentComplete = 99m
        };

        var trials = Enumerable.Range(1, 20)
            .Select(i => new ValidationParameterTrial
            {
                ValidationExperimentId = 23,
                TrialNumber = i,
                ParameterFingerprint = $"FP{i:D2}",
                Status = ValidationTrialStatus.Completed,
                StartedAtUtc = DateTime.UtcNow.AddHours(-2),
                CompletedAtUtc = DateTime.UtcNow.AddHours(-1)
            })
            .ToList();

        var progress = ValidationTrainingProgressCalculator.Calculate(experiment, trials, generatedTrialCount: 25);

        Assert.Equal(25, progress.RequestedTrialCount);
        Assert.Equal(25, progress.GeneratedTrialCount);
        Assert.Equal(20, progress.CompletedTrialCount);
        Assert.Equal(0, progress.PendingTrialCount);
        Assert.Equal(65m, progress.ProgressPercent);
    }

    [Fact]
    public void LifecycleGate_Resume_AllowsInterruptedAndFailed()
    {
        Assert.True(ValidationLifecycleGate.CanResumeTraining(ValidationExperimentStatus.Failed));
        Assert.True(ValidationLifecycleGate.CanResumeTraining(ValidationExperimentStatus.TrainingInterrupted));
        Assert.False(ValidationLifecycleGate.CanResumeTraining(ValidationExperimentStatus.DataReady));
    }

    [Fact]
    public async Task ExecutionLease_RejectsSecondOwnerUntilExpired()
    {
        var repo = new AtomicInMemoryLeaseRepository();
        var service = new ValidationTrainingExecutionLeaseService(repo);

        var first = await service.TryAcquireAsync(23, "owner-a", TimeSpan.FromMinutes(5));
        var second = await service.TryAcquireAsync(23, "owner-b", TimeSpan.FromMinutes(5));

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
    }

    [Fact]
    public async Task ExecutionLease_ExpiredLeaseCanBeReclaimed()
    {
        var repo = new AtomicInMemoryLeaseRepository();
        var service = new ValidationTrainingExecutionLeaseService(repo);
        await repo.TryAcquireAtomicAsync(
            23,
            "stale",
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddHours(-2));

        var reclaimed = await service.TryAcquireAsync(23, "owner-new", TimeSpan.FromMinutes(5));
        Assert.True(reclaimed.Acquired);

        var oldOwnerRelease = await service.ReleaseAsync(23, "stale");
        Assert.Equal(ValidationLeaseOperationStatus.Conflict, oldOwnerRelease.Status);
    }

    [Fact]
    public async Task ExecutionLease_WrongOwnerCannotHeartbeatOrRelease()
    {
        var repo = new AtomicInMemoryLeaseRepository();
        var service = new ValidationTrainingExecutionLeaseService(repo);
        Assert.True((await service.TryAcquireAsync(23, "owner-a", TimeSpan.FromMinutes(5))).Acquired);

        var hb = await service.HeartbeatAsync(23, "owner-b", TimeSpan.FromMinutes(5));
        var rel = await service.ReleaseAsync(23, "owner-b");

        Assert.Equal(ValidationLeaseOperationStatus.Conflict, hb.Status);
        Assert.Equal(ValidationLeaseOperationStatus.Conflict, rel.Status);
        Assert.True(await service.IsActiveAsync(23));
    }

    [Fact]
    public async Task ExecutionLease_OwnerHeartbeatPreservesAcquiredAtUtc()
    {
        var repo = new AtomicInMemoryLeaseRepository();
        var service = new ValidationTrainingExecutionLeaseService(repo);
        var acquiredAt = DateTime.UtcNow.AddMinutes(-10);
        await repo.TryAcquireAtomicAsync(23, "owner-a", acquiredAt, DateTime.UtcNow.AddMinutes(5), acquiredAt);

        var before = await repo.GetByExperimentIdAsync(23);
        Assert.NotNull(before);
        var originalAcquired = before!.AcquiredAtUtc;

        var hb = await service.HeartbeatAsync(23, "owner-a", TimeSpan.FromMinutes(5));
        Assert.True(hb.Succeeded);

        var after = await repo.GetByExperimentIdAsync(23);
        Assert.NotNull(after);
        Assert.Equal(originalAcquired, after!.AcquiredAtUtc);
        Assert.True(after.HeartbeatAtUtc >= before.HeartbeatAtUtc);
    }

    [Fact]
    public async Task ExecutionLease_ConcurrentAcquire_ExactlyOneWins()
    {
        var repo = new AtomicInMemoryLeaseRepository();
        var service = new ValidationTrainingExecutionLeaseService(repo);
        var barrier = new Barrier(2);

        async Task<(bool Acquired, string? Conflict)> Worker(string owner)
        {
            barrier.SignalAndWait();
            return await service.TryAcquireAsync(99, owner, TimeSpan.FromMinutes(5));
        }

        var t1 = Task.Run(() => Worker("worker-a"));
        var t2 = Task.Run(() => Worker("worker-b"));
        await Task.WhenAll(t1, t2);

        var wins = new[] { t1.Result, t2.Result }.Count(r => r.Acquired);
        Assert.Equal(1, wins);
        Assert.Equal(1, new[] { t1.Result, t2.Result }.Count(r => !r.Acquired));

        var loser = t1.Result.Acquired ? "worker-b" : "worker-a";
        Assert.Equal(ValidationLeaseOperationStatus.Conflict, (await service.HeartbeatAsync(99, loser, TimeSpan.FromMinutes(1))).Status);
        Assert.Equal(ValidationLeaseOperationStatus.Conflict, (await service.ReleaseAsync(99, loser)).Status);
    }

    [Fact]
    public void DbRetry_IsTransient_ForMySqlConnectionMessages()
    {
        var ex = new Exception("Unable to connect to any of the specified MySQL hosts.");
        Assert.True(ValidationTrainingDbRetry.IsTransient(ex));
        Assert.True(TransientDatabaseRetryPolicy.IsTransient(ex));
    }

    /// <summary>In-memory lease store with monitor lock to simulate atomic conditional acquire.</summary>
    private sealed class AtomicInMemoryLeaseRepository : IValidationExperimentExecutionLeaseRepository
    {
        private readonly object _gate = new();
        private ValidationExperimentExecutionLease? _lease;

        public Task<ValidationExperimentExecutionLease?> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(
                    _lease?.ValidationExperimentId == experimentId
                        ? Clone(_lease)
                        : null);
            }
        }

        public Task<bool> TryAcquireAtomicAsync(
            long experimentId,
            string leaseOwner,
            DateTime acquiredAtUtc,
            DateTime expiresAtUtc,
            DateTime heartbeatAtUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_lease is null || _lease.ValidationExperimentId != experimentId)
                {
                    _lease = new ValidationExperimentExecutionLease
                    {
                        ValidationExperimentId = experimentId,
                        LeaseOwner = leaseOwner,
                        AcquiredAtUtc = acquiredAtUtc,
                        ExpiresAtUtc = expiresAtUtc,
                        HeartbeatAtUtc = heartbeatAtUtc
                    };
                    return Task.FromResult(true);
                }

                if (string.Equals(_lease.LeaseOwner, leaseOwner, StringComparison.Ordinal))
                {
                    _lease.ExpiresAtUtc = expiresAtUtc;
                    _lease.HeartbeatAtUtc = heartbeatAtUtc;
                    return Task.FromResult(true);
                }

                if (_lease.ExpiresAtUtc <= acquiredAtUtc)
                {
                    _lease.LeaseOwner = leaseOwner;
                    _lease.AcquiredAtUtc = acquiredAtUtc;
                    _lease.ExpiresAtUtc = expiresAtUtc;
                    _lease.HeartbeatAtUtc = heartbeatAtUtc;
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        }

        public Task<bool> TryHeartbeatOwnedAsync(
            long experimentId,
            string leaseOwner,
            DateTime expiresAtUtc,
            DateTime heartbeatAtUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_lease is null
                    || _lease.ValidationExperimentId != experimentId
                    || !string.Equals(_lease.LeaseOwner, leaseOwner, StringComparison.Ordinal))
                {
                    return Task.FromResult(false);
                }

                _lease.ExpiresAtUtc = expiresAtUtc;
                _lease.HeartbeatAtUtc = heartbeatAtUtc;
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryReleaseOwnedAsync(
            long experimentId,
            string leaseOwner,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_lease is null
                    || _lease.ValidationExperimentId != experimentId
                    || !string.Equals(_lease.LeaseOwner, leaseOwner, StringComparison.Ordinal))
                {
                    return Task.FromResult(false);
                }

                _lease = null;
                return Task.FromResult(true);
            }
        }

        public Task UpsertAsync(ValidationExperimentExecutionLease lease, CancellationToken cancellationToken = default) =>
            TryAcquireAtomicAsync(
                lease.ValidationExperimentId,
                lease.LeaseOwner,
                lease.AcquiredAtUtc,
                lease.ExpiresAtUtc,
                lease.HeartbeatAtUtc,
                cancellationToken).ContinueWith(_ => { }, cancellationToken);

        public Task ReleaseAsync(long experimentId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_lease?.ValidationExperimentId == experimentId)
                {
                    _lease = null;
                }

                return Task.CompletedTask;
            }
        }

        private static ValidationExperimentExecutionLease Clone(ValidationExperimentExecutionLease src) =>
            new()
            {
                Id = src.Id,
                ValidationExperimentId = src.ValidationExperimentId,
                LeaseOwner = src.LeaseOwner,
                AcquiredAtUtc = src.AcquiredAtUtc,
                ExpiresAtUtc = src.ExpiresAtUtc,
                HeartbeatAtUtc = src.HeartbeatAtUtc
            };
    }
}
