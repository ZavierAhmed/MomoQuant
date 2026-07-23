using MomoQuant.Application.Monitoring;
using MomoQuant.Application.Monitoring.Dtos;

namespace MomoQuant.UnitTests.Monitoring;

public sealed class PublicHealthResponseMapperTests
{
    [Fact]
    public void Map_BothHealthy_ReturnsHealthy()
    {
        var result = PublicHealthResponseMapper.Map(
            "MOMO Quant",
            Component("Database", "Healthy", 12),
            Component("Redis", "Healthy", 3),
            new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("Healthy", result.Status);
        Assert.Equal("MOMO Quant", result.Application);
        Assert.Equal(result.CheckedAtUtc, result.TimestampUtc);
        Assert.Equal("Healthy", result.Components.Mysql.Status);
        Assert.Equal(12, result.Components.Mysql.DurationMs);
        Assert.Equal("Healthy", result.Components.Redis.Status);
        Assert.Equal(3, result.Components.Redis.DurationMs);
    }

    [Fact]
    public void Map_MysqlUnhealthy_ReturnsUnhealthy()
    {
        var result = PublicHealthResponseMapper.Map(
            "MOMO Quant",
            Component("Database", "Unhealthy", 40),
            Component("Redis", "Healthy", 2));

        Assert.Equal("Unhealthy", result.Status);
        Assert.Equal("Unhealthy", result.Components.Mysql.Status);
        Assert.Equal("Healthy", result.Components.Redis.Status);
    }

    [Fact]
    public void Map_RedisUnhealthy_ReturnsDegraded()
    {
        var result = PublicHealthResponseMapper.Map(
            "MOMO Quant",
            Component("Database", "Healthy", 5),
            Component("Redis", "Unhealthy", 100));

        Assert.Equal("Degraded", result.Status);
        Assert.Equal("Healthy", result.Components.Mysql.Status);
        Assert.Equal("Unhealthy", result.Components.Redis.Status);
    }

    [Fact]
    public void Map_MissingComponents_TreatedAsUnknown_DegradesWhenMysqlUnknown()
    {
        var result = PublicHealthResponseMapper.Map("MOMO Quant", null, null);

        Assert.Equal("Degraded", result.Status);
        Assert.Equal("Unknown", result.Components.Mysql.Status);
        Assert.Equal("Unknown", result.Components.Redis.Status);
        Assert.Null(result.Components.Mysql.DurationMs);
        Assert.Null(result.Components.Redis.DurationMs);
    }

    [Fact]
    public void Map_DoesNotLeakComponentMessagesOrSecrets()
    {
        var result = PublicHealthResponseMapper.Map(
            "MOMO Quant",
            new ComponentHealthDto
            {
                Name = "Database",
                Status = "Healthy",
                LatencyMs = 4,
                Message = "Server=prod;Password=super-secret;User=root"
            },
            new ComponentHealthDto
            {
                Name = "Redis",
                Status = "Unhealthy",
                LatencyMs = 90,
                Message = "redis://:SecretRedisPassword@10.0.0.5:6379"
            });

        Assert.Equal("Degraded", result.Status);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("super-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecretRedisPassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should-not-appear", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Message", json, StringComparison.OrdinalIgnoreCase);
    }

    private static ComponentHealthDto Component(string name, string status, int latencyMs) => new()
    {
        Name = name,
        Status = status,
        LatencyMs = latencyMs,
        Message = "should-not-appear-in-public-payload"
    };
}
