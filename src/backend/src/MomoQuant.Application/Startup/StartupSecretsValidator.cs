using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MomoQuant.Application.Options;

namespace MomoQuant.Application.Startup;

/// <summary>
/// Fails fast when required secrets are missing or are weak placeholders.
/// Skipped for the Testing environment (integration/unit host).
/// </summary>
public static class StartupSecretsValidator
{
    private static readonly string[] WeakJwtFragments =
    [
        "change_me",
        "change-me",
        "changeme",
        "change-this",
        "changethis",
        "dev-only",
        "placeholder",
        "your-secret",
        "secret-key-here",
        "momo-quant-jwt-secret"
    ];

    private static readonly string[] WeakPasswordTokens =
    [
        "change_me",
        "change-me",
        "changeme",
        "change-this",
        "changethis",
        "password",
        "your-password",
        "todo"
    ];

    public const int MinimumJwtSecretLength = 32;

    public static void ValidateOrThrow(IConfiguration configuration, IHostEnvironment environment)
    {
        if (IsTestHost(environment))
        {
            return;
        }

        var errors = new List<string>();

        ValidateConnectionString(
            configuration.GetConnectionString("DefaultConnection"),
            "ConnectionStrings:DefaultConnection",
            errors);

        ValidateRedisConnection(
            configuration.GetConnectionString("Redis"),
            "ConnectionStrings:Redis",
            errors);

        var jwtSecret = configuration.GetSection(JwtSettings.SectionName)["Secret"];
        ValidateJwtSecret(jwtSecret, errors);

        if (environment.IsDevelopment())
        {
            var seedPassword = configuration.GetSection(SeedSettings.SectionName)["AdminPassword"];
            if (IsMissingOrWeakPassword(seedPassword))
            {
                errors.Add(
                    "Seed:AdminPassword is missing or uses a weak/placeholder value. " +
                    "Set it via user-secrets or environment variables after rotation.");
            }
        }

        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Startup secrets validation failed. Configure secrets via user-secrets or environment variables " +
            "(see docs/11-local-secrets-and-hosting-security.md). Issues:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
    }

    private static bool IsTestHost(IHostEnvironment environment) =>
        environment.IsEnvironment("Testing")
        || string.Equals(
            Environment.GetEnvironmentVariable("MOMO_SKIP_SECRETS_VALIDATION"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateConnectionString(string? connectionString, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add($"{key} is missing.");
            return;
        }

        var password = ExtractPassword(connectionString);
        if (IsMissingOrWeakPassword(password))
        {
            errors.Add($"{key} password is missing or uses a weak/placeholder value.");
        }
    }

    private static void ValidateRedisConnection(string? redis, string key, List<string> errors)
    {
        // Redis is optional when the section is absent (some hosts may not configure it yet),
        // but when present it must not use empty/placeholder passwords.
        if (string.IsNullOrWhiteSpace(redis))
        {
            return;
        }

        var match = Regex.Match(redis, @"password=([^,]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        var password = match.Groups[1].Value.Trim();
        if (IsMissingOrWeakPassword(password))
        {
            errors.Add($"{key} password is missing or uses a weak/placeholder value.");
        }
    }

    private static void ValidateJwtSecret(string? secret, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            errors.Add("Jwt:Secret is missing.");
            return;
        }

        if (secret.Length < MinimumJwtSecretLength)
        {
            errors.Add($"Jwt:Secret must be at least {MinimumJwtSecretLength} characters.");
        }

        var normalized = secret.Trim().ToLowerInvariant();
        if (WeakJwtFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
        {
            errors.Add("Jwt:Secret looks like a weak or placeholder value and must be rotated.");
        }
    }

    private static bool IsMissingOrWeakPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return true;
        }

        var normalized = password.Trim().ToLowerInvariant();
        if (normalized is "change_me" or "change-me" or "changeme" or "change-this" or "changethis" or "todo")
        {
            return true;
        }

        return WeakPasswordTokens.Any(token =>
            string.Equals(normalized, token, StringComparison.Ordinal));
    }

    private static string? ExtractPassword(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            if (!key.Equals("Password", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("Pwd", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return part[(separator + 1)..].Trim();
        }

        return null;
    }
}
