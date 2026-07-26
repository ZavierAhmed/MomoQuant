namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Base type for durable validation audit-execution failures (Milestone 23.0E2C1).
/// </summary>
public class ValidationAuditExecutionException : Exception
{
    public string ErrorCode { get; }

    public ValidationAuditExecutionException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public ValidationAuditExecutionException(string errorCode, string message, Exception? innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Requested scope/audit identity does not match the durable audit-execution row.
/// Fail closed before any candle access.
/// </summary>
public sealed class ValidationAuditExecutionIdentityMismatchException : ValidationAuditExecutionException
{
    public const string Code = "VALIDATION_AUDIT_EXECUTION_IDENTITY_MISMATCH";

    public Guid? ExpectedAuditExecutionId { get; }
    public Guid? ActualAuditExecutionId { get; }
    public Guid? ExpectedScopeExecutionId { get; }
    public Guid? ActualScopeExecutionId { get; }
    public string? ExpectedExecutionToken { get; }
    public string? ActualExecutionToken { get; }
    public string SafeMessage { get; }

    public ValidationAuditExecutionIdentityMismatchException(
        string safeMessage,
        Guid? expectedAuditExecutionId = null,
        Guid? actualAuditExecutionId = null,
        Guid? expectedScopeExecutionId = null,
        Guid? actualScopeExecutionId = null,
        string? expectedExecutionToken = null,
        string? actualExecutionToken = null)
        : base(Code, safeMessage)
    {
        ExpectedAuditExecutionId = expectedAuditExecutionId;
        ActualAuditExecutionId = actualAuditExecutionId;
        ExpectedScopeExecutionId = expectedScopeExecutionId;
        ActualScopeExecutionId = actualScopeExecutionId;
        ExpectedExecutionToken = expectedExecutionToken;
        ActualExecutionToken = actualExecutionToken;
        SafeMessage = safeMessage;
    }
}
