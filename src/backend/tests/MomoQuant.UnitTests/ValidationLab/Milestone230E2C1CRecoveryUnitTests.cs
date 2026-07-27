using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E2C1C — lease-aware fresh-execution detection via recovery service.
/// </summary>
public sealed class Milestone230E2C1CRecoveryUnitTests
{
    [Fact]
    public async Task RecoverAsync_SameLeaseOwner_NotResume_ReturnsNoRecoveryNeeded()
    {
        var execution = NewExecution(leaseOwner: "owner-a");
        var trial = NewTrial();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        var service = CreateService(execution, trial);

        var result = await service.RecoverAsync(
            execution.AuditExecutionId,
            new ValidationAuditExecutionRecoveryRequest
            {
                CurrentLeaseOwner = "owner-a",
                IsResume = false,
                TrialStatus = ValidationTrialStatus.Running
            });

        Assert.Equal(ValidationAuditRecoveryDecision.NoRecoveryNeeded, result.RecoveryDecision);
        Assert.False(result.MustRerunTrial);
    }

    [Fact]
    public async Task RecoverAsync_DifferentLeaseOwner_ReturnsSupersedeAndRerun()
    {
        var execution = NewExecution(leaseOwner: "harness-write-owner");
        var trial = NewTrial();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        var service = CreateService(execution, trial);

        var result = await service.RecoverAsync(
            execution.AuditExecutionId,
            new ValidationAuditExecutionRecoveryRequest
            {
                CurrentLeaseOwner = "harness-recover-owner",
                IsResume = false,
                TrialStatus = ValidationTrialStatus.Running
            });

        Assert.Equal(ValidationAuditRecoveryDecision.SupersedeAndRerun, result.RecoveryDecision);
        Assert.True(result.MustRerunTrial);
        Assert.Equal(ValidationAuditExecutionStatus.RecoveryRequired, execution.Status);
    }

    [Fact]
    public async Task RecoverAsync_IsResume_ReturnsSupersedeAndRerun()
    {
        var execution = NewExecution(leaseOwner: "harness-write-owner");
        var trial = NewTrial();
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        var service = CreateService(execution, trial);

        var result = await service.RecoverAsync(
            execution.AuditExecutionId,
            new ValidationAuditExecutionRecoveryRequest
            {
                CurrentLeaseOwner = "harness-write-owner",
                IsResume = true,
                TrialStatus = ValidationTrialStatus.Running
            });

        Assert.Equal(ValidationAuditRecoveryDecision.SupersedeAndRerun, result.RecoveryDecision);
        Assert.True(result.MustRerunTrial);
        Assert.Equal("PROCESS_INTERRUPTED_BEFORE_FLUSH", result.FailureCode);
    }

    [Fact]
    public async Task RecoverAsync_TrialInterrupted_ReturnsSupersedeAndRerun()
    {
        var execution = NewExecution(leaseOwner: "owner-a");
        var trial = NewTrial(status: ValidationTrialStatus.Interrupted);
        trial.AuthoritativeAuditExecutionId = execution.AuditExecutionId;
        var service = CreateService(execution, trial);

        var result = await service.RecoverAsync(
            execution.AuditExecutionId,
            new ValidationAuditExecutionRecoveryRequest
            {
                CurrentLeaseOwner = "owner-a",
                IsResume = false,
                TrialStatus = ValidationTrialStatus.Interrupted
            });

        Assert.Equal(ValidationAuditRecoveryDecision.SupersedeAndRerun, result.RecoveryDecision);
        Assert.True(result.MustRerunTrial);
    }

    private static ValidationAuditExecutionRecoveryService CreateService(
        ValidationAuditExecution execution,
        ValidationParameterTrial trial)
    {
        var executions = new FakeAuditExecutionRepository(execution);
        var trials = new FakeTrialRepository(trial);
        return new ValidationAuditExecutionRecoveryService(
            executions,
            new FakeAuditBatchRepository(),
            new FakeAccessAuditRepository(),
            trials,
            new ValidationAuditCompletenessVerifier(),
            new FakeAuditUnitOfWork());
    }

    private static ValidationAuditExecution NewExecution(string leaseOwner) =>
        new()
        {
            AuditExecutionId = Guid.NewGuid(),
            ValidationExperimentId = 1,
            ValidationTrialId = 10,
            TrialNumber = 1,
            ScopeExecutionId = Guid.NewGuid(),
            AttemptNumber = 1,
            ExecutionToken = "token",
            LeaseOwner = leaseOwner,
            Status = ValidationAuditExecutionStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RecoveryStatus = ValidationAuditRecoveryStatus.None,
            LastConfirmedSequence = 0,
            ConfirmedEventCount = 0,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
            RowVersion = 1
        };

    private static ValidationParameterTrial NewTrial(
        ValidationTrialStatus status = ValidationTrialStatus.Running) =>
        new()
        {
            Id = 10,
            ValidationExperimentId = 1,
            TrialNumber = 1,
            ParameterSnapshotJson = "{}",
            ParameterFingerprint = "fp",
            Status = status,
            AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress,
            AuthoritativeAuditExecutionId = null
        };

    private sealed class FakeAuditUnitOfWork : IValidationAuditUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            action();
    }

    private sealed class FakeAuditExecutionRepository : IValidationAuditExecutionRepository
    {
        private readonly ValidationAuditExecution _execution;

        public FakeAuditExecutionRepository(ValidationAuditExecution execution) => _execution = execution;

        public Task<ValidationAuditExecution?> GetByAuditExecutionIdAsync(
            Guid auditExecutionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ValidationAuditExecution?>(
                auditExecutionId == _execution.AuditExecutionId ? _execution : null);

        public Task<IReadOnlyList<ValidationAuditExecution>> GetActiveByTrialIdAsync(
            long validationTrialId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditExecution>>(
                validationTrialId == _execution.ValidationTrialId
                    ? [_execution]
                    : Array.Empty<ValidationAuditExecution>());

        public Task<IReadOnlyList<ValidationAuditExecution>> GetByTrialIdAsync(
            long validationTrialId,
            CancellationToken cancellationToken = default) =>
            GetActiveByTrialIdAsync(validationTrialId, cancellationToken);

        public Task AddAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ValidationAuditExecution> CreateAndAssignTrialAuthoritativeAsync(
            ValidationAuditExecution execution,
            ValidationParameterTrial trial,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(execution);

        public Task UpdateAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
        {
            if (execution.AuditExecutionId == _execution.AuditExecutionId)
            {
                _execution.Status = execution.Status;
                _execution.RecoveryStatus = execution.RecoveryStatus;
                _execution.FailureCode = execution.FailureCode;
                _execution.UpdatedAtUtc = execution.UpdatedAtUtc;
                _execution.RowVersion = execution.RowVersion;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditBatchRepository : IValidationAuditBatchRepository
    {
        public Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(
            Guid auditBatchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ValidationAuditBatch?>(null);

        public Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(
            Guid auditExecutionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditBatch>>(Array.Empty<ValidationAuditBatch>());

        public Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ValidationAuditBatch> GetOrCreateManifestAsync(
            ValidationAuditBatch proposed,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(proposed);
    }

    private sealed class FakeAccessAuditRepository : IValidationCandleAccessAuditRepository
    {
        public Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ValidationAccessBatchPersistResult.EmptyNoWork());

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(Array.Empty<ValidationCandleAccessAudit>());
    }

    private sealed class FakeTrialRepository : IValidationParameterTrialRepository
    {
        private readonly ValidationParameterTrial _trial;

        public FakeTrialRepository(ValidationParameterTrial trial) => _trial = trial;

        public Task<ValidationParameterTrial?> GetByExperimentAndFingerprintAsync(
            long experimentId,
            string parameterFingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ValidationParameterTrial?>(_trial);

        public Task<IReadOnlyList<ValidationParameterTrial>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationParameterTrial>>([_trial]);

        public Task AddAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default)
        {
            _trial.AuditCompletionStatus = trial.AuditCompletionStatus;
            _trial.Status = trial.Status;
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(
            IEnumerable<ValidationParameterTrial> trials,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
