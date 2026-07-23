using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Research;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0B — ResearchOperationStatus survives host restart (MySQL-backed store).
/// </summary>
[Collection("Integration")]
public sealed class Milestone230BOperationDurabilityTests
{
    private const string OperationType = ResearchOperationStatusCodes.ValidationTrainingType;

    [Fact]
    public async Task OperationStatus_SurvivesHostRestart_Monotonic_Stale_CancelOwnership()
    {
        var correlationId = $"m230b-op-{Guid.NewGuid():N}";
        var entityId = $"230{RandomNumberGenerator.GetInt32(100_000, 999_999)}";
        var operationId = $"vl-train-{entityId}";
        var ownerEmail = $"owner-{Guid.NewGuid():N}@momoquant.test";
        var strangerEmail = $"stranger-{Guid.NewGuid():N}@momoquant.test";
        var adminEmail = $"admin-{Guid.NewGuid():N}@momoquant.test";
        var ownerPassword = CreateStrongPassword();
        var strangerPassword = CreateStrongPassword();
        var adminPassword = CreateStrongPassword();
        var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        var createdUserIds = new List<long>();

        ResearchOperationStatus hostAState;
        await using (var hostA = new ClockAwareWebApplicationFactory(clock))
        {
            await EnsureUsersAsync(hostA, createdUserIds,
                (ownerEmail, ownerPassword, UserRole.Trader),
                (strangerEmail, strangerPassword, UserRole.Trader),
                (adminEmail, adminPassword, UserRole.Admin));

            await using (var scope = hostA.Services.CreateAsyncScope())
            {
                var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusService>();
                hostAState = await ops.UpsertValidationTrainingAsync(new ResearchOperationStatus
                {
                    OperationId = operationId,
                    CorrelationId = correlationId,
                    OperationType = OperationType,
                    EntityId = entityId,
                    Stage = "TrainingRunning",
                    Status = "TrainingRunning",
                    PercentComplete = 42.5m,
                    RequestedWorkCount = 10,
                    CompletedWorkCount = 4,
                    FailedWorkCount = 1,
                    ActiveWorkItem = "Trial 5",
                    StartedAtUtc = clock.GetUtcNow().UtcDateTime,
                    LastProgressAtUtc = clock.GetUtcNow().UtcDateTime,
                    LastHeartbeatAtUtc = clock.GetUtcNow().UtcDateTime,
                    DiagnosticReference = $"ValidationExperiment:{entityId}",
                    LeaseOwner = ownerEmail
                });
            }
        } // Host A fully disposed

        await using var hostB = new ClockAwareWebApplicationFactory(clock);
        try
        {
            ResearchOperationStatus recovered;
            await using (var scope = hostB.Services.CreateAsyncScope())
            {
                var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusService>();
                recovered = (await ops.GetByOperationIdAsync(operationId))!;
            }

            Assert.NotNull(recovered);
            Assert.Equal(hostAState.OperationId, recovered.OperationId);
            Assert.Equal(hostAState.CorrelationId, recovered.CorrelationId);
            Assert.Equal(hostAState.OperationType, recovered.OperationType);
            Assert.Equal(hostAState.EntityId, recovered.EntityId);
            Assert.Equal(hostAState.Stage, recovered.Stage);
            Assert.Equal(hostAState.Status, recovered.Status);
            Assert.Equal(hostAState.PercentComplete, recovered.PercentComplete);
            Assert.Equal(hostAState.RequestedWorkCount, recovered.RequestedWorkCount);
            Assert.Equal(hostAState.CompletedWorkCount, recovered.CompletedWorkCount);
            Assert.Equal(hostAState.FailedWorkCount, recovered.FailedWorkCount);
            Assert.Equal(hostAState.ActiveWorkItem, recovered.ActiveWorkItem);
            Assert.Equal(hostAState.LeaseOwner, recovered.LeaseOwner);
            Assert.Equal(hostAState.DiagnosticReference, recovered.DiagnosticReference);

            // API path (same durable store) — ResearchRead required
            var client = hostB.CreateClient();
            var viewerToken = await LoginAsync(client, ownerEmail, ownerPassword);
            var apiResponse = await SendAsync(
                client,
                HttpMethod.Get,
                $"/api/v1/validation-lab/experiments/{entityId}/operation-status",
                viewerToken);
            Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
            var apiPayload = await apiResponse.Content
                .ReadFromJsonAsync<ApiResponse<ResearchOperationStatus>>(IntegrationTestJson.Options);
            Assert.NotNull(apiPayload?.Data);
            Assert.Equal(42.5m, apiPayload!.Data!.PercentComplete);
            Assert.Equal(operationId, apiPayload.Data.OperationId);

            // Advance progress; regression rejected
            await using (var scope = hostB.Services.CreateAsyncScope())
            {
                var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusService>();
                var advanced = await ops.AdvanceProgressAsync(
                    operationId,
                    percentComplete: 55m,
                    completedWorkCount: 6,
                    failedWorkCount: 1,
                    stage: "TrainingRunning",
                    activeWorkItem: "Trial 7");
                Assert.True(advanced.Succeeded);
                Assert.Equal(55m, advanced.Data!.PercentComplete);
                Assert.Equal(6, advanced.Data.CompletedWorkCount);

                var regress = await ops.AdvanceProgressAsync(
                    operationId,
                    percentComplete: 50m,
                    completedWorkCount: 5,
                    failedWorkCount: 1);
                Assert.False(regress.Succeeded);
                Assert.Equal(
                    ResearchOperationStatusCodes.ProgressRegressionRejected,
                    regress.ErrorField);

                var afterRegress = await ops.GetByOperationIdAsync(operationId);
                Assert.Equal(55m, afterRegress!.PercentComplete);
                Assert.Equal(6, afterRegress.CompletedWorkCount);
            }

            // Stale detection via injectable clock (no Thread.Sleep)
            clock.Advance(TimeSpan.FromMinutes(30));
            await using (var scope = hostB.Services.CreateAsyncScope())
            {
                var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusService>();
                var stale = await ops.DetectAndMarkStaleAsync(operationId, TimeSpan.FromMinutes(5));
                Assert.NotNull(stale);
                Assert.Equal(ResearchOperationStatusCodes.Stale, stale!.Status);
                Assert.Equal(ResearchOperationStatusCodes.HeartbeatStale, stale.ErrorCode);
            }

            var strangerToken = await LoginAsync(client, strangerEmail, strangerPassword);
            var ownerToken = await LoginAsync(client, ownerEmail, ownerPassword);
            var adminToken = await LoginAsync(client, adminEmail, adminPassword);

            var forbidden = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/validation-lab/experiments/{entityId}/operation-status/cancel",
                strangerToken);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.NotEqual(HttpStatusCode.NotFound, forbidden.StatusCode);

            // Re-seed running state so owner cancel can succeed after stale mark
            await using (var scope = hostB.Services.CreateAsyncScope())
            {
                var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusService>();
                await ops.UpsertValidationTrainingAsync(new ResearchOperationStatus
                {
                    OperationId = operationId,
                    CorrelationId = correlationId,
                    OperationType = OperationType,
                    EntityId = entityId,
                    Stage = "TrainingRunning",
                    Status = "TrainingRunning",
                    PercentComplete = 55m,
                    RequestedWorkCount = 10,
                    CompletedWorkCount = 6,
                    FailedWorkCount = 1,
                    StartedAtUtc = clock.GetUtcNow().UtcDateTime,
                    LastProgressAtUtc = clock.GetUtcNow().UtcDateTime,
                    LastHeartbeatAtUtc = clock.GetUtcNow().UtcDateTime,
                    LeaseOwner = ownerEmail,
                    DiagnosticReference = $"ValidationExperiment:{entityId}"
                });
            }

            var ownerCancel = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/validation-lab/experiments/{entityId}/operation-status/cancel",
                ownerToken);
            Assert.Equal(HttpStatusCode.OK, ownerCancel.StatusCode);
            var ownerPayload = await ownerCancel.Content
                .ReadFromJsonAsync<ApiResponse<ResearchOperationStatus>>(IntegrationTestJson.Options);
            Assert.Equal(ResearchOperationStatusCodes.Cancelled, ownerPayload!.Data!.Status);

            // Admin can cancel a fresh operation
            var adminOpId = $"{operationId}-admin";
            await using (var scope = hostB.Services.CreateAsyncScope())
            {
                var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusService>();
                await ops.UpsertValidationTrainingAsync(new ResearchOperationStatus
                {
                    OperationId = adminOpId,
                    CorrelationId = correlationId,
                    OperationType = OperationType,
                    EntityId = $"{entityId}9",
                    Stage = "TrainingRunning",
                    Status = "TrainingRunning",
                    PercentComplete = 10m,
                    RequestedWorkCount = 3,
                    CompletedWorkCount = 0,
                    FailedWorkCount = 0,
                    LeaseOwner = ownerEmail,
                    StartedAtUtc = clock.GetUtcNow().UtcDateTime,
                    LastHeartbeatAtUtc = clock.GetUtcNow().UtcDateTime,
                    DiagnosticReference = $"ValidationExperiment:{entityId}9"
                });
                var adminCancel = await ops.CancelAsync(adminOpId, adminEmail, callerIsAdmin: true);
                Assert.True(adminCancel.Succeeded);
                Assert.Equal(ResearchOperationStatusCodes.Cancelled, adminCancel.Data!.Status);
            }
        }
        finally
        {
            await CleanupAsync(hostB, correlationId, createdUserIds);
        }
    }

    private static async Task EnsureUsersAsync(
        WebApplicationFactory<Program> factory,
        List<long> createdUserIds,
        params (string Email, string Password, UserRole Role)[] users)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTime.UtcNow;

        foreach (var (email, password, role) in users)
        {
            var normalized = email.Trim().ToLowerInvariant();
            var roleEntity = await db.Roles.FirstAsync(r => r.Name == role);
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
            if (existing is not null)
            {
                db.Users.Remove(existing);
                await db.SaveChangesAsync();
            }

            var user = new User
            {
                FullName = email,
                Email = normalized,
                PasswordHash = hasher.Hash(password),
                RoleId = roleEntity.Id,
                Role = roleEntity.Name,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            createdUserIds.Add(user.Id);
        }
    }

    private static async Task CleanupAsync(
        WebApplicationFactory<Program> factory,
        string correlationId,
        IReadOnlyList<long> userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ops = scope.ServiceProvider.GetRequiredService<IResearchOperationStatusRepository>();
        await ops.DeleteByCorrelationIdAsync(correlationId);

        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        if (userIds.Count > 0)
        {
            await db.AuditLogs.Where(a => a.UserId != null && userIds.Contains(a.UserId.Value)).ExecuteDeleteAsync();
            await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        Assert.True(response.IsSuccessStatusCode, $"Login failed for disposable identity ({(int)response.StatusCode}).");
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(IntegrationTestJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Data?.AccessToken));
        return payload!.Data!.AccessToken;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static string CreateStrongPassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return "Aa1!" + Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y');
    }

    private sealed class ClockAwareWebApplicationFactory : MomoQuantWebApplicationFactory
    {
        private readonly ControllableTimeProvider _clock;

        public ClockAwareWebApplicationFactory(ControllableTimeProvider clock) => _clock = clock;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
            });
        }
    }
}
