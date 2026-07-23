using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;

namespace MomoQuant.Persistence.Seeding;

public interface IIdentityDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public sealed class IdentityDataSeeder : IIdentityDataSeeder
{
    private readonly MomoQuantDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly SeedSettings _seedSettings;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        MomoQuantDbContext dbContext,
        IPasswordHasher passwordHasher,
        IHostEnvironment hostEnvironment,
        IOptions<SeedSettings> seedSettings,
        ILogger<IdentityDataSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _hostEnvironment = hostEnvironment;
        _seedSettings = seedSettings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);

        if (_hostEnvironment.IsDevelopment())
        {
            await SeedDevelopmentAdminAsync(cancellationToken);
        }
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var roleDefinitions = new[]
        {
            (UserRole.Admin, "Full platform administration access."),
            (UserRole.Trader, "Trading operations and monitoring access."),
            (UserRole.Viewer, "Read-only dashboard access.")
        };

        foreach (var (name, description) in roleDefinitions)
        {
            var exists = await _dbContext.Roles.AnyAsync(role => role.Name == name, cancellationToken);
            if (exists)
            {
                continue;
            }

            var now = DateTime.UtcNow;
            _dbContext.Roles.Add(new Role
            {
                Name = name,
                Description = description,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            _logger.LogInformation("Seeded role {RoleName}", name);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateRoleException(ex))
        {
            _logger.LogDebug(ex, "Role seed skipped because another process already created the roles.");
        }
    }

    private async Task SeedDevelopmentAdminAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_seedSettings.AdminPassword))
        {
            _logger.LogWarning(
                "Skipping development admin seed because Seed:AdminPassword is not configured. " +
                "Set it via user-secrets or environment variables after rotation.");
            return;
        }

        var adminEmail = _seedSettings.AdminEmail.Trim().ToLowerInvariant();
        var adminExists = await _dbContext.Users.AnyAsync(user => user.Email == adminEmail, cancellationToken);
        if (adminExists)
        {
            return;
        }

        var adminRole = await _dbContext.Roles.FirstAsync(role => role.Name == UserRole.Admin, cancellationToken);
        var now = DateTime.UtcNow;

        _dbContext.Users.Add(new User
        {
            FullName = _seedSettings.AdminFullName,
            Email = adminEmail,
            PasswordHash = _passwordHasher.Hash(_seedSettings.AdminPassword),
            RoleId = adminRole.Id,
            Role = adminRole.Name,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded development admin user {AdminEmail}", adminEmail);
        }
        catch (DbUpdateException ex) when (IsDuplicateUserException(ex))
        {
            _logger.LogDebug(ex, "Development admin seed skipped because the user already exists.");
        }
    }

    private static bool IsDuplicateRoleException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_Roles_Name", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDuplicateUserException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase) == true;
}
