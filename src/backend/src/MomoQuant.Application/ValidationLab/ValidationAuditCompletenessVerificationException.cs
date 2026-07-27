namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Thrown by completeness verification. Distinguishes verifier-origin failures from
/// audit-finalization failures without inspecting exception message text.
/// </summary>
public sealed class ValidationAuditCompletenessVerificationException : Exception
{
    public ValidationAuditCompletenessVerificationException(string message)
        : base(message)
    {
    }

    public ValidationAuditCompletenessVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
