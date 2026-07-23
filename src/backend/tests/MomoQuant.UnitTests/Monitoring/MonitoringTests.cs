using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Audit.Dtos;
using MomoQuant.Application.Audit.Services;
using MomoQuant.Application.Monitoring;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Application.Monitoring.Services;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Monitoring;

namespace MomoQuant.UnitTests.Monitoring;

public sealed class MonitoringTests
{
    [Fact]
    public async Task OverallHealth_ReturnsHealthy_WhenAllComponentsHealthy()
    {
        var providers = new IHealthCheckProvider[]
        {
            StubProvider("API", SystemHealthStatus.Healthy),
            StubProvider("Database", SystemHealthStatus.Healthy),
            StubProvider("Redis", SystemHealthStatus.Unknown),
            StubProvider("AI Service", SystemHealthStatus.Healthy)
        };

        var service = CreateSystemHealthService(providers);
        var result = await service.GetOverallHealthAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Healthy", result.Data!.Status);
        Assert.Equal(4, result.Data.Components.Count);
    }

    [Fact]
    public async Task OverallHealth_ReturnsDegraded_WhenAiUnavailableButCoreHealthy()
    {
        var providers = new IHealthCheckProvider[]
        {
            StubProvider("API", SystemHealthStatus.Healthy),
            StubProvider("Database", SystemHealthStatus.Healthy),
            StubProvider("Redis", SystemHealthStatus.Unknown),
            StubProvider("AI Service", SystemHealthStatus.Degraded)
        };

        var service = CreateSystemHealthService(providers);
        var result = await service.GetOverallHealthAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Degraded", result.Data!.Status);
    }

    [Fact]
    public async Task OverallHealth_ReturnsUnhealthy_WhenDatabaseUnavailable()
    {
        var providers = new IHealthCheckProvider[]
        {
            StubProvider("API", SystemHealthStatus.Healthy),
            StubProvider("Database", SystemHealthStatus.Unhealthy),
            StubProvider("Redis", SystemHealthStatus.Unknown),
            StubProvider("AI Service", SystemHealthStatus.Healthy)
        };

        var service = CreateSystemHealthService(providers);
        var result = await service.GetOverallHealthAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Unhealthy", result.Data!.Status);
    }

    [Fact]
    public async Task AuditLogService_CreatesAndQueriesAuditLog()
    {
        var repository = new InMemoryAuditLogRepository();
        await repository.AddAsync(new AuditLog
        {
            Action = "BACKTEST_STARTED",
            EntityType = "BacktestRun",
            UserId = 1,
            Severity = LogSeverity.Info,
            CreatedAtUtc = DateTime.UtcNow
        });
        await repository.SaveChangesAsync();

        var service = new AuditLogService(repository, new AuditLogQueryValidator());
        var result = await service.GetLogsAsync(new AuditLogQuery { Page = 1, PageSize = 50 });

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task AuditLogService_FiltersByUserActionAndDate()
    {
        var repository = new InMemoryAuditLogRepository();
        await repository.AddAsync(new AuditLog
        {
            Action = "USER_LOGGED_IN",
            EntityType = "User",
            UserId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await repository.AddAsync(new AuditLog
        {
            Action = "BACKTEST_STARTED",
            EntityType = "BacktestRun",
            UserId = 2,
            CreatedAtUtc = DateTime.UtcNow
        });
        await repository.SaveChangesAsync();

        var service = new AuditLogService(repository, new AuditLogQueryValidator());
        var result = await service.GetLogsAsync(new AuditLogQuery
        {
            UserId = 2,
            EventType = "BACKTEST_STARTED",
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow.AddHours(1)
        });

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.Items);
        Assert.Equal("BACKTEST_STARTED", result.Data.Items[0].Action);
    }

    [Fact]
    public async Task AuditLogService_PaginationWorks()
    {
        var repository = new InMemoryAuditLogRepository();
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(new AuditLog
            {
                Action = $"ACTION_{i}",
                EntityType = "Test",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await repository.SaveChangesAsync();
        var service = new AuditLogService(repository, new AuditLogQueryValidator());
        var result = await service.GetLogsAsync(new AuditLogQuery { Page = 2, PageSize = 2 });

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal(5, result.Data.TotalCount);
        Assert.Equal(3, result.Data.TotalPages);
    }

    [Fact]
    public async Task SystemHealthLogService_StoresLogs()
    {
        var repository = new InMemorySystemHealthLogRepository();
        var service = new SystemHealthLogService(repository, new MonitoringQueryValidator(), NullLogger<SystemHealthLogService>.Instance);

        await service.PersistAsync(new HealthCheckResult
        {
            Name = "Database",
            Subsystem = MonitoringSubsystem.Database,
            Status = SystemHealthStatus.Healthy,
            Message = "ok"
        });

        var result = await service.GetPagedAsync(new MonitoringQuery { Page = 1, PageSize = 50 });
        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task RecentErrors_ReturnsOnlyErrorAndCritical()
    {
        var repository = new InMemorySystemHealthLogRepository();
        await repository.AddAsync(new SystemHealthLog
        {
            ServiceName = "Database",
            Status = SystemHealthStatus.Unhealthy,
            Severity = LogSeverity.Error,
            Message = "db error",
            CheckedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await repository.AddAsync(new SystemHealthLog
        {
            ServiceName = "Api",
            Status = SystemHealthStatus.Healthy,
            Severity = LogSeverity.Info,
            Message = "ok",
            CheckedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await repository.SaveChangesAsync();

        var service = new RecentErrorService(repository, new InMemoryAuditLogRepository(), new MonitoringQueryValidator());
        var result = await service.GetRecentErrorsAsync(new MonitoringQuery { Limit = 20 });

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Error", result.Data[0].Severity);
    }

    [Fact]
    public async Task SafetyEvents_ReturnsRiskAndEmergencyEvents()
    {
        var auditRepository = new InMemoryAuditLogRepository();
        await auditRepository.AddAsync(new AuditLog
        {
            Action = "RISK_EMERGENCY_STOP_RULE_CHANGED",
            EntityType = "RiskRule",
            Severity = LogSeverity.Critical,
            CreatedAtUtc = DateTime.UtcNow
        });
        await auditRepository.SaveChangesAsync();

        var service = new RecentErrorService(new InMemorySystemHealthLogRepository(), auditRepository, new MonitoringQueryValidator());
        var result = await service.GetSafetyEventsAsync(new MonitoringQuery { Limit = 20 });

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("RISK_EMERGENCY_STOP_RULE_CHANGED", result.Data[0].EventType);
    }

    [Fact]
    public void MonitoringQueryValidator_RejectsInvalidDateRange()
    {
        var validator = new MonitoringQueryValidator();
        var result = validator.Validate(new MonitoringQuery
        {
            FromUtc = DateTime.UtcNow,
            ToUtc = DateTime.UtcNow.AddHours(-1)
        });

        Assert.False(result.Succeeded);
        Assert.Equal("fromUtc", result.ErrorField);
    }

    [Fact]
    public void MonitoringQueryValidator_EnforcesPageSizeMaximum()
    {
        var validator = new MonitoringQueryValidator();
        var result = validator.Validate(new MonitoringQuery { PageSize = 500 });

        Assert.False(result.Succeeded);
        Assert.Equal("pageSize", result.ErrorField);
    }

    [Fact]
    public async Task AuditLogService_DoesNotReturnSecrets()
    {
        var repository = new InMemoryAuditLogRepository();
        await repository.AddAsync(new AuditLog
        {
            Action = "USER_CREATED",
            EntityType = "User",
            NewValueJson = """{"email":"user@test.com","password":"secret123"}""",
            CreatedAtUtc = DateTime.UtcNow
        });
        await repository.SaveChangesAsync();

        var service = new AuditLogService(repository, new AuditLogQueryValidator());
        var result = await service.GetLogsAsync(new AuditLogQuery());

        Assert.True(result.Succeeded);
        Assert.Contains("[REDACTED]", result.Data!.Items[0].NewValuesJson);
        Assert.DoesNotContain("secret123", result.Data.Items[0].NewValuesJson);
    }

    private static SystemHealthService CreateSystemHealthService(IEnumerable<IHealthCheckProvider> providers)
    {
        var logRepository = new InMemorySystemHealthLogRepository();
        var logService = new SystemHealthLogService(logRepository, new MonitoringQueryValidator(), NullLogger<SystemHealthLogService>.Instance);
        var subsystemProvider = new Mock<ISubsystemHealthCheckProvider>();
        subsystemProvider.Setup(provider => provider.CheckAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<HealthCheckResult>());

        return new SystemHealthService(
            providers,
            subsystemProvider.Object,
            logService,
            NullLogger<SystemHealthService>.Instance);
    }

    private static IHealthCheckProvider StubProvider(string name, SystemHealthStatus status) =>
        Mock.Of<IHealthCheckProvider>(provider =>
            provider.ComponentName == name &&
            provider.CheckAsync(It.IsAny<CancellationToken>()) == Task.FromResult(new HealthCheckResult
            {
                Name = name,
                Subsystem = MonitoringSubsystem.Unknown,
                Status = status,
                Message = status.ToString()
            }));
}

internal sealed class InMemoryAuditLogRepository : MomoQuant.Application.Abstractions.IAuditLogRepository
{
    private readonly List<AuditLog> _logs = [];
    private long _nextId = 1;

    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        auditLog.Id = _nextId++;
        _logs.Add(auditLog);
        return Task.CompletedTask;
    }

    public Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_logs.FirstOrDefault(log => log.Id == id));

    public Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        AuditLogQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(filter).OrderByDescending(log => log.CreatedAtUtc).ToList();
        return Task.FromResult<(IReadOnlyList<AuditLog>, int)>((
            query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList(),
            query.Count));
    }

    public Task<IReadOnlyList<(string Action, int Count)>> GetActionCountsAsync(
        AuditLogQueryFilter filter,
        int top,
        CancellationToken cancellationToken = default)
    {
        var counts = BuildQuery(filter)
            .GroupBy(log => log.Action)
            .Select(group => (group.Key, group.Count()))
            .OrderByDescending(item => item.Item2)
            .Take(top)
            .ToList();

        return Task.FromResult<IReadOnlyList<(string, int)>>(counts);
    }

    public Task<int> CountAsync(AuditLogQueryFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildQuery(filter).Count());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private IEnumerable<AuditLog> BuildQuery(AuditLogQueryFilter filter)
    {
        var query = _logs.Where(log => log.CreatedAtUtc >= filter.FromUtc && log.CreatedAtUtc <= filter.ToUtc);
        if (filter.Severity.HasValue)
        {
            query = query.Where(log => log.Severity == filter.Severity.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(log => log.Action == filter.Action);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(log => log.UserId == filter.UserId.Value);
        }

        return query;
    }
}

internal sealed class InMemorySystemHealthLogRepository : MomoQuant.Application.Abstractions.ISystemHealthLogRepository
{
    private readonly List<SystemHealthLog> _logs = [];
    private long _nextId = 1;

    public Task AddAsync(SystemHealthLog log, CancellationToken cancellationToken = default)
    {
        log.Id = _nextId++;
        _logs.Add(log);
        return Task.CompletedTask;
    }

    public Task<SystemHealthLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_logs.FirstOrDefault(log => log.Id == id));

    public Task<(IReadOnlyList<SystemHealthLog> Items, int TotalCount)> GetPagedAsync(
        MonitoringQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(filter).OrderByDescending(log => log.CheckedAtUtc).ToList();
        return Task.FromResult<(IReadOnlyList<SystemHealthLog>, int)>((
            query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList(),
            query.Count));
    }

    public Task<IReadOnlyList<SystemHealthLog>> GetRecentAsync(
        MonitoringQueryFilter filter,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SystemHealthLog>>(BuildQuery(filter).OrderByDescending(log => log.CheckedAtUtc).Take(filter.Limit).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private IEnumerable<SystemHealthLog> BuildQuery(MonitoringQueryFilter filter)
    {
        var query = _logs.Where(log => log.CheckedAtUtc >= filter.FromUtc && log.CheckedAtUtc <= filter.ToUtc);
        if (filter.Severity.HasValue)
        {
            query = query.Where(log => log.Severity == filter.Severity.Value);
        }

        if (filter.Subsystem.HasValue)
        {
            var subsystem = filter.Subsystem.Value.ToString();
            query = query.Where(log => log.ServiceName == subsystem);
        }

        return query;
    }
}
