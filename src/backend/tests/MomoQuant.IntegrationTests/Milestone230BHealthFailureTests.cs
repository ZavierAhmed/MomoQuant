using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0B — public /api/health component failure mapping (no secrets in payload).
/// </summary>
[Collection("Integration")]
public sealed class Milestone230BHealthFailureTests
{
    [Fact]
    public async Task Health_BothHealthy_ReturnsHealthy_WithoutSecrets()
    {
        await using var factory = new HealthStubFactory(
            mysql: SystemHealthStatus.Healthy,
            redis: SystemHealthStatus.Healthy);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal("Healthy", root.GetProperty("components").GetProperty("mysql").GetProperty("status").GetString());
        Assert.Equal("Healthy", root.GetProperty("components").GetProperty("redis").GetProperty("status").GetString());
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_RedisUnavailable_ReturnsDegraded()
    {
        await using var factory = new HealthStubFactory(
            mysql: SystemHealthStatus.Healthy,
            redis: SystemHealthStatus.Unhealthy);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Degraded", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", doc.RootElement.GetProperty("components").GetProperty("mysql").GetProperty("status").GetString());
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("components").GetProperty("redis").GetProperty("status").GetString());
        Assert.DoesNotContain("SecretRedis", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-leak", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_MysqlUnavailable_ReturnsUnhealthy()
    {
        await using var factory = new HealthStubFactory(
            mysql: SystemHealthStatus.Unhealthy,
            redis: SystemHealthStatus.Healthy);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("components").GetProperty("mysql").GetProperty("status").GetString());
        Assert.Equal("Healthy", doc.RootElement.GetProperty("components").GetProperty("redis").GetProperty("status").GetString());
        Assert.DoesNotContain("momo_user", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IntegrationTest_DbPassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-leak", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HealthStubFactory : MomoQuantWebApplicationFactory
    {
        private readonly SystemHealthStatus _mysql;
        private readonly SystemHealthStatus _redis;

        public HealthStubFactory(SystemHealthStatus mysql, SystemHealthStatus redis)
        {
            _mysql = mysql;
            _redis = redis;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHealthCheckProvider>();
                services.AddSingleton<IHealthCheckProvider>(new StubHealthProvider("Database", MonitoringSubsystem.Database, _mysql));
                services.AddSingleton<IHealthCheckProvider>(new StubHealthProvider("Redis", MonitoringSubsystem.Redis, _redis));
            });
        }
    }

    private sealed class StubHealthProvider : IHealthCheckProvider
    {
        private readonly SystemHealthStatus _status;
        private readonly MonitoringSubsystem _subsystem;

        public StubHealthProvider(string name, MonitoringSubsystem subsystem, SystemHealthStatus status)
        {
            ComponentName = name;
            _subsystem = subsystem;
            _status = status;
        }

        public string ComponentName { get; }

        public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult
            {
                Name = ComponentName,
                Subsystem = _subsystem,
                Status = _status,
                LatencyMs = 1,
                Message = "Secret=do-not-leak;Password=x"
            });
    }
}
