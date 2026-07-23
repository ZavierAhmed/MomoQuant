using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Services;

public sealed class AuditService : IAuditService
{
    private readonly MomoQuantDbContext _dbContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(MomoQuantDbContext dbContext, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

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
        try
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Severity = LogSeverity.Info,
                OldValueJson = oldValueJson,
                NewValueJson = newValueJson,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for action {Action}.", action);
        }
    }
}
