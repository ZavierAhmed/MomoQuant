using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.Abstractions;

public interface IValidationAuditExecutionRepository
{
    Task<ValidationAuditExecution?> GetByAuditExecutionIdAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationAuditExecution>> GetByTrialIdAsync(
        long validationTrialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Non-terminal executions: Created, InProgress, FlushManifested, EventsConfirmed, RecoveryRequired.
    /// Failed / Completed / Superseded are terminal.
    /// </summary>
    Task<IReadOnlyList<ValidationAuditExecution>> GetActiveByTrialIdAsync(
        long validationTrialId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default);

    Task UpdateAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transactionally inserts a new execution and assigns it as the trial's authoritative audit execution.
    /// Fail closed if another active execution already exists for the trial.
    /// </summary>
    Task<ValidationAuditExecution> CreateAndAssignTrialAuthoritativeAsync(
        ValidationAuditExecution execution,
        ValidationParameterTrial trial,
        CancellationToken cancellationToken = default);
}

public interface IValidationAuditBatchRepository
{
    Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(
        Guid auditBatchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(
        Guid auditExecutionId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default);

    Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent create/recover of a batch manifest by (AuditExecutionId, BatchNumber)
    /// or matching ExpectedPayloadSetHash for retry.
    /// </summary>
    Task<ValidationAuditBatch> GetOrCreateManifestAsync(
        ValidationAuditBatch proposed,
        CancellationToken cancellationToken = default);
}

public interface IValidationAuditUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
