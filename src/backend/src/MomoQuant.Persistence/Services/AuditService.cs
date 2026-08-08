using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;

namespace MomoQuant.Persistence.Services;

public sealed class AuditService : IAuditService
{
    private readonly IAuditTelemetryWriter _telemetryWriter;

    public AuditService(IAuditTelemetryWriter telemetryWriter) => _telemetryWriter = telemetryWriter;

    public async Task LogAsync(
        string action,
        string entityType,
        long? entityId = null,
        long? userId = null,
        string? oldValueJson = null,
        string? newValueJson = null,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        await _telemetryWriter.WriteTelemetryAsync(
            new AuditTelemetryRequest(
                action,
                entityType,
                entityId,
                userId,
                oldValueJson,
                newValueJson,
                ipAddress,
                userAgent),
            cancellationToken).ConfigureAwait(false);
    }
}
