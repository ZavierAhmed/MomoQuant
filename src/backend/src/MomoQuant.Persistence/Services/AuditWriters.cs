using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Audit;
using MomoQuant.Domain.Audit;

namespace MomoQuant.Persistence.Services;

public sealed class RequiredAuditWriter : IRequiredAuditWriter
{
    private readonly MomoQuantDbContext _dbContext;

    public RequiredAuditWriter(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public void AttachRequired(RequiredAuditRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = AuditWritePayloadProtection.PrepareRequired(request);
        _dbContext.AuditLogs.Add(ToEntity(payload));
    }

    internal static AuditLog ToEntity(PreparedAuditPayload payload) => new()
    {
        UserId = payload.UserId,
        TradingSessionId = payload.TradingSessionId,
        Action = payload.Action,
        EntityType = payload.EntityType,
        EntityId = payload.EntityId,
        Severity = payload.Severity,
        OldValueJson = payload.OldValueJson,
        NewValueJson = payload.NewValueJson,
        IpAddress = payload.IpAddress,
        UserAgent = payload.UserAgent,
        CreatedAtUtc = payload.CreatedAtUtc
    };
}

public sealed class AuditTelemetryWriter : IAuditTelemetryWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditTelemetryWriter> _logger;

    public AuditTelemetryWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditTelemetryWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task WriteTelemetryAsync(
        AuditTelemetryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var payload = AuditWritePayloadProtection.PrepareTelemetry(request);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var isolatedContext = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            isolatedContext.AuditLogs.Add(RequiredAuditWriter.ToEntity(payload));
            await isolatedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _logger.LogError(
                "Best-effort audit telemetry could not be persisted for action {Action}.",
                SafeAction(request.Action));
        }
    }

    private static string SafeAction(string action) =>
        !string.IsNullOrWhiteSpace(action)
        && action.Length <= 128
        && action.All(character =>
            character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9'
            || character == '_')
            ? action
            : "INVALID_AUDIT_ACTION";
}
