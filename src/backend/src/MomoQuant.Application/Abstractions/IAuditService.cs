namespace MomoQuant.Application.Abstractions;

/// <summary>
/// Legacy best-effort operational telemetry. This is not authoritative audit evidence.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityType,
        long? entityId = null,
        long? userId = null,
        string? oldValueJson = null,
        string? newValueJson = null,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);
}
