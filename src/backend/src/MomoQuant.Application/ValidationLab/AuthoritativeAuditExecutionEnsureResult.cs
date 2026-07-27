using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Result of ensuring a trial has an authoritative durable audit execution before access.
/// </summary>
public sealed class AuthoritativeAuditExecutionEnsureResult
{
    public required ValidationAuditExecution Execution { get; init; }

    /// <summary>When true, training must not invoke StrategyLabRunner; only finalizer/verifier may run.</summary>
    public bool FinalizationOnly { get; init; }

    /// <summary>
    /// Completed execution passed verifier revalidation; verification-only path (no runner, no scope access).
    /// </summary>
    public bool VerifiedFinalizationOnly { get; init; }

    /// <summary>
    /// Completed or corrupt execution cannot be re-entered; do not invoke StrategyLabRunner under it.
    /// </summary>
    public bool FailClosed { get; init; }

    public ValidationAuditRecoveryDecision? RecoveryDecision { get; init; }

    public ValidationAuditCompletenessCode? CompletenessCode { get; init; }
}
