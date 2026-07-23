using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Creates ephemeral Admin identities in the disposable *_test database for JWT API calls.
/// Does not touch admin@momoquant.local or other seeded accounts.
/// </summary>
internal static class IntegrationDisposableAuth
{
    public static string CreateStrongPassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return "Aa1!" + Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y');
    }

    public static async Task<(HttpClient Client, long UserId)> CreateAuthorizedAdminClientAsync(
        MomoQuantWebApplicationFactory factory,
        string emailPrefix = "m230b-admin")
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@momoquant.test";
        var password = CreateStrongPassword();
        long userId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var roleEntity = await db.Roles.FirstAsync(r => r.Name == UserRole.Admin);
            var now = DateTime.UtcNow;
            var user = new User
            {
                FullName = email,
                Email = email,
                PasswordHash = hasher.Hash(password),
                RoleId = roleEntity.Id,
                Role = roleEntity.Name,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        Assert.True(login.IsSuccessStatusCode, $"Disposable admin login failed: {(int)login.StatusCode}");
        var payload = await login.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(IntegrationTestJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Data?.AccessToken));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload!.Data!.AccessToken);
        return (client, userId);
    }

    public static async Task DeleteUsersAsync(MomoQuantWebApplicationFactory factory, params long[] userIds)
    {
        if (userIds.Length == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await db.AuditLogs.Where(a => a.UserId != null && userIds.Contains(a.UserId.Value)).ExecuteDeleteAsync();
        await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
    }
}
