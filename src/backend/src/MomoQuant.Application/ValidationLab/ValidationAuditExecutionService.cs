using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationAuditExecutionFactory
{
    Task<ValidationAuditExecution> CreateForTrialAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        string? leaseOwner,
        string executionToken,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates and assigns a durable audit execution for a trial (WP1/WP3).</summary>
public sealed class ValidationAuditExecutionService : IValidationAuditExecutionFactory
{
    private readonly IValidationAuditExecutionRepository _executions;

    public ValidationAuditExecutionService(IValidationAuditExecutionRepository executions)
    {
        _executions = executions;
    }

    public async Task<ValidationAuditExecution> CreateForTrialAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        string? leaseOwner,
        string executionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trial);
        if (string.IsNullOrWhiteSpace(executionToken))
        {
            throw new ArgumentException("ExecutionToken is required.", nameof(executionToken));
        }

        if (executionToken.Length > 128)
        {
            executionToken = executionToken[..128];
        }

        var now = DateTime.UtcNow;
        var attemptNumber = trial.AuditAttemptNumber > 0
            ? trial.AuditAttemptNumber + 1
            : 1;

        var execution = new ValidationAuditExecution
        {
            AuditExecutionId = Guid.NewGuid(),
            ValidationExperimentId = experiment.Id,
            ValidationTrialId = trial.Id,
            TrialNumber = trial.TrialNumber,
            ScopeExecutionId = Guid.NewGuid(),
            AttemptNumber = attemptNumber,
            ExecutionToken = executionToken,
            LeaseOwner = string.IsNullOrWhiteSpace(leaseOwner) ? null : leaseOwner.Trim(),
            ExecutionType = ValidationAuditExecutionType.Trial,
            Status = ValidationAuditExecutionStatus.InProgress,
            StartedAtUtc = now,
            UpdatedAtUtc = now,
            RecoveryStatus = ValidationAuditRecoveryStatus.None,
            LastConfirmedSequence = 0,
            ConfirmedEventCount = 0,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
            AllowsZeroAccess = false,
            RowVersion = 1
        };

        return await _executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial, cancellationToken)
            .ConfigureAwait(false);
    }
}

public interface IValidationAuditExecutionSupersessionService
{
    Task<ValidationAuditExecution> SupersedeForRerunAsync(
        Guid existingAuditExecutionId,
        string newExecutionToken,
        string reasonCode,
        string? leaseOwner = null,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationAuditExecutionSupersessionService : IValidationAuditExecutionSupersessionService
{
    private readonly IValidationAuditExecutionRepository _executions;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationAuditUnitOfWork _uow;

    public ValidationAuditExecutionSupersessionService(
        IValidationAuditExecutionRepository executions,
        IValidationParameterTrialRepository trials,
        IValidationAuditUnitOfWork uow)
    {
        _executions = executions;
        _trials = trials;
        _uow = uow;
    }

    public async Task<ValidationAuditExecution> SupersedeForRerunAsync(
        Guid existingAuditExecutionId,
        string newExecutionToken,
        string reasonCode,
        string? leaseOwner = null,
        CancellationToken cancellationToken = default)
    {
        if (existingAuditExecutionId == Guid.Empty)
        {
            throw new ArgumentException("existingAuditExecutionId is required.", nameof(existingAuditExecutionId));
        }

        if (string.IsNullOrWhiteSpace(newExecutionToken))
        {
            throw new ArgumentException("newExecutionToken is required.", nameof(newExecutionToken));
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("reasonCode is required.", nameof(reasonCode));
        }

        ValidationAuditExecution? created = null;

        await _uow.ExecuteInTransactionAsync(async () =>
        {
            var existing = await _executions.GetByAuditExecutionIdAsync(existingAuditExecutionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_EXECUTION_MISSING",
                    $"Audit execution {existingAuditExecutionId} was not found.");

            if (existing.Status == ValidationAuditExecutionStatus.Completed)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_CANNOT_SUPERSEDE_COMPLETED",
                    $"Audit execution {existing.AuditExecutionId} is Completed and cannot be superseded.");
            }

            if (existing.Status == ValidationAuditExecutionStatus.Superseded)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_ALREADY_SUPERSEDED",
                    $"Audit execution {existing.AuditExecutionId} is already Superseded.");
            }

            var trials = await _trials.GetByExperimentIdAsync(existing.ValidationExperimentId, cancellationToken)
                .ConfigureAwait(false);
            var trial = trials.FirstOrDefault(t => t.Id == existing.ValidationTrialId)
                ?? throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_TRIAL_MISSING",
                    $"Trial {existing.ValidationTrialId} was not found for audit supersession.");

            var now = DateTime.UtcNow;
            var newId = Guid.NewGuid();
            existing.MarkSuperseded(newId, now, reasonCode);
            existing.RowVersion++;
            await _executions.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);

            var token = newExecutionToken.Length > 128 ? newExecutionToken[..128] : newExecutionToken;
            var attemptNumber = Math.Max(existing.AttemptNumber + 1, trial.AuditAttemptNumber + 1);

            created = new ValidationAuditExecution
            {
                AuditExecutionId = newId,
                ValidationExperimentId = existing.ValidationExperimentId,
                ValidationTrialId = existing.ValidationTrialId,
                TrialNumber = existing.TrialNumber,
                ScopeExecutionId = Guid.NewGuid(),
                AttemptNumber = attemptNumber,
                ExecutionToken = token,
                LeaseOwner = string.IsNullOrWhiteSpace(leaseOwner)
                    ? existing.LeaseOwner
                    : leaseOwner.Trim(),
                ExecutionType = ValidationAuditExecutionType.Trial,
                Status = ValidationAuditExecutionStatus.InProgress,
                StartedAtUtc = now,
                UpdatedAtUtc = now,
                RecoveryStatus = ValidationAuditRecoveryStatus.None,
                LastConfirmedSequence = 0,
                ConfirmedEventCount = 0,
                AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
                AllowsZeroAccess = false,
                RowVersion = 1
            };

            await _executions.AddAsync(created, cancellationToken).ConfigureAwait(false);

            trial.AuthoritativeAuditExecutionId = created.AuditExecutionId;
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
            trial.AuditAttemptNumber = created.AttemptNumber;
            await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return created
            ?? throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_SUPERSESSION_FAILED",
                "Supersession did not produce a new audit execution.");
    }
}
