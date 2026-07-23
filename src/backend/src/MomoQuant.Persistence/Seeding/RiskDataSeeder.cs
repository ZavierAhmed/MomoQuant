using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Persistence.Seeding;

public interface IRiskDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public sealed class RiskDataSeeder : IRiskDataSeeder
{
    private readonly MomoQuantDbContext _dbContext;
    private readonly ILogger<RiskDataSeeder> _logger;

    public RiskDataSeeder(MomoQuantDbContext dbContext, ILogger<RiskDataSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureProfileAsync(
            "Conservative",
            "Conservative risk profile with tighter limits.",
            isDefault: false,
            CreateConservativeRules(),
            cancellationToken);

        await EnsureProfileAsync(
            "Balanced",
            "Balanced risk profile with standard MVP limits.",
            isDefault: true,
            CreateBalancedRules(),
            cancellationToken);

        await EnsureProfileAsync(
            "Aggressive",
            "Aggressive risk profile with wider limits.",
            isDefault: false,
            CreateAggressiveRules(),
            cancellationToken);

        await EnsureProfileAsync(
            "Benchmark Research Risk",
            "Loose risk profile for strategy edge discovery in benchmark research mode.",
            isDefault: false,
            CreateBenchmarkResearchRules(),
            cancellationToken);

        await EnsureProfileAsync(
            "Paper Validation Risk",
            "Moderate risk profile for realistic paper validation benchmarks.",
            isDefault: false,
            CreatePaperValidationRules(),
            cancellationToken);

        await EnsureProfileAsync(
            "Live Candidate Risk",
            "Strict simulation-only candidate profile used before any live-readiness decisions.",
            isDefault: false,
            CreateLiveCandidateRules(),
            cancellationToken);
    }

    private async Task EnsureProfileAsync(
        string name,
        string description,
        bool isDefault,
        IReadOnlyList<(string Key, string Value, SettingValueType Type)> rules,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.RiskProfiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Name == name, cancellationToken);

        if (profile is null)
        {
            var now = DateTime.UtcNow;
            profile = new RiskProfile
            {
                Name = name,
                Description = description,
                IsDefault = isDefault,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _dbContext.RiskProfiles.Add(profile);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Seeded risk profile {ProfileName}", name);
            }
            catch (DbUpdateException ex) when (IsDuplicateProfileException(ex))
            {
                DetachAddedProfiles();
                profile = await _dbContext.RiskProfiles.AsNoTracking()
                    .FirstAsync(item => item.Name == name, cancellationToken);
            }
        }

        foreach (var (key, value, valueType) in rules)
        {
            var exists = await _dbContext.RiskRules.AsNoTracking()
                .AnyAsync(
                    rule => rule.RiskProfileId == profile.Id && rule.RuleKey == key,
                    cancellationToken);

            if (exists)
            {
                continue;
            }

            var now = DateTime.UtcNow;
            _dbContext.RiskRules.Add(new RiskRule
            {
                RiskProfileId = profile.Id,
                RuleKey = key,
                RuleValue = value,
                ValueType = valueType,
                IsEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateRuleException(ex))
        {
            DetachAddedRules();
            _logger.LogDebug(ex, "Risk rule seed skipped because defaults already exist for {ProfileName}.", name);
        }
    }

    private static IReadOnlyList<(string Key, string Value, SettingValueType Type)> CreateConservativeRules() =>
    [
        (RiskRuleKeys.MaxRiskPerTradePercent, "0.25", SettingValueType.Decimal),
        (RiskRuleKeys.MaxDailyLossPercent, "1.5", SettingValueType.Decimal),
        (RiskRuleKeys.MaxWeeklyLossPercent, "4", SettingValueType.Decimal),
        (RiskRuleKeys.MaxOpenPositions, "1", SettingValueType.Int),
        (RiskRuleKeys.MaxExposurePerSymbolPercent, "20", SettingValueType.Decimal),
        (RiskRuleKeys.MaxTotalExposurePercent, "40", SettingValueType.Decimal),
        (RiskRuleKeys.MaxCorrelatedExposurePercent, "50", SettingValueType.Decimal),
        (RiskRuleKeys.MaxConsecutiveLosses, "2", SettingValueType.Int),
        (RiskRuleKeys.MinConfidenceScore, "85", SettingValueType.Decimal),
        (RiskRuleKeys.MaxSpreadPercent, "0.04", SettingValueType.Decimal),
        (RiskRuleKeys.MaxAtrPercent, "2", SettingValueType.Decimal),
        (RiskRuleKeys.EmergencyStopEnabled, "false", SettingValueType.Bool),
        (RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        (RiskRuleKeys.MinRewardRiskRatio, "1.3", SettingValueType.Decimal)
    ];

    private static IReadOnlyList<(string Key, string Value, SettingValueType Type)> CreateBalancedRules() =>
    [
        (RiskRuleKeys.MaxRiskPerTradePercent, "0.5", SettingValueType.Decimal),
        (RiskRuleKeys.MaxDailyLossPercent, "2", SettingValueType.Decimal),
        (RiskRuleKeys.MaxWeeklyLossPercent, "5", SettingValueType.Decimal),
        (RiskRuleKeys.MaxOpenPositions, "2", SettingValueType.Int),
        (RiskRuleKeys.MaxExposurePerSymbolPercent, "25", SettingValueType.Decimal),
        (RiskRuleKeys.MaxTotalExposurePercent, "50", SettingValueType.Decimal),
        (RiskRuleKeys.MaxCorrelatedExposurePercent, "50", SettingValueType.Decimal),
        (RiskRuleKeys.MaxConsecutiveLosses, "3", SettingValueType.Int),
        (RiskRuleKeys.MinConfidenceScore, "80", SettingValueType.Decimal),
        (RiskRuleKeys.MaxSpreadPercent, "0.05", SettingValueType.Decimal),
        (RiskRuleKeys.MaxAtrPercent, "2.5", SettingValueType.Decimal),
        (RiskRuleKeys.EmergencyStopEnabled, "false", SettingValueType.Bool),
        (RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        (RiskRuleKeys.MinRewardRiskRatio, "1.2", SettingValueType.Decimal)
    ];

    private static IReadOnlyList<(string Key, string Value, SettingValueType Type)> CreateAggressiveRules() =>
    [
        (RiskRuleKeys.MaxRiskPerTradePercent, "1", SettingValueType.Decimal),
        (RiskRuleKeys.MaxDailyLossPercent, "3", SettingValueType.Decimal),
        (RiskRuleKeys.MaxWeeklyLossPercent, "7", SettingValueType.Decimal),
        (RiskRuleKeys.MaxOpenPositions, "3", SettingValueType.Int),
        (RiskRuleKeys.MaxExposurePerSymbolPercent, "35", SettingValueType.Decimal),
        (RiskRuleKeys.MaxTotalExposurePercent, "70", SettingValueType.Decimal),
        (RiskRuleKeys.MaxCorrelatedExposurePercent, "50", SettingValueType.Decimal),
        (RiskRuleKeys.MaxConsecutiveLosses, "4", SettingValueType.Int),
        (RiskRuleKeys.MinConfidenceScore, "75", SettingValueType.Decimal),
        (RiskRuleKeys.MaxSpreadPercent, "0.08", SettingValueType.Decimal),
        (RiskRuleKeys.MaxAtrPercent, "3.5", SettingValueType.Decimal),
        (RiskRuleKeys.EmergencyStopEnabled, "false", SettingValueType.Bool),
        (RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        (RiskRuleKeys.MinRewardRiskRatio, "1.1", SettingValueType.Decimal)
    ];

    private static IReadOnlyList<(string Key, string Value, SettingValueType Type)> CreateBenchmarkResearchRules() =>
    [
        (RiskRuleKeys.MaxRiskPerTradePercent, "2.0", SettingValueType.Decimal),
        (RiskRuleKeys.MaxDailyLossPercent, "20", SettingValueType.Decimal),
        (RiskRuleKeys.MaxWeeklyLossPercent, "40", SettingValueType.Decimal),
        (RiskRuleKeys.MaxOpenPositions, "5", SettingValueType.Int),
        (RiskRuleKeys.MaxExposurePerSymbolPercent, "60", SettingValueType.Decimal),
        (RiskRuleKeys.MaxTotalExposurePercent, "90", SettingValueType.Decimal),
        (RiskRuleKeys.MaxConsecutiveLosses, "10", SettingValueType.Int),
        (RiskRuleKeys.MinConfidenceScore, "0", SettingValueType.Decimal),
        (RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        (RiskRuleKeys.MinRewardRiskRatio, "1.0", SettingValueType.Decimal)
    ];

    private static IReadOnlyList<(string Key, string Value, SettingValueType Type)> CreatePaperValidationRules() =>
    [
        (RiskRuleKeys.MaxRiskPerTradePercent, "1.0", SettingValueType.Decimal),
        (RiskRuleKeys.MaxDailyLossPercent, "6", SettingValueType.Decimal),
        (RiskRuleKeys.MaxWeeklyLossPercent, "15", SettingValueType.Decimal),
        (RiskRuleKeys.MaxOpenPositions, "3", SettingValueType.Int),
        (RiskRuleKeys.MaxExposurePerSymbolPercent, "35", SettingValueType.Decimal),
        (RiskRuleKeys.MaxTotalExposurePercent, "60", SettingValueType.Decimal),
        (RiskRuleKeys.MaxConsecutiveLosses, "4", SettingValueType.Int),
        (RiskRuleKeys.MinConfidenceScore, "55", SettingValueType.Decimal),
        (RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        (RiskRuleKeys.MinRewardRiskRatio, "1.5", SettingValueType.Decimal)
    ];

    private static IReadOnlyList<(string Key, string Value, SettingValueType Type)> CreateLiveCandidateRules() =>
    [
        (RiskRuleKeys.MaxRiskPerTradePercent, "0.5", SettingValueType.Decimal),
        (RiskRuleKeys.MaxDailyLossPercent, "3", SettingValueType.Decimal),
        (RiskRuleKeys.MaxWeeklyLossPercent, "8", SettingValueType.Decimal),
        (RiskRuleKeys.MaxOpenPositions, "2", SettingValueType.Int),
        (RiskRuleKeys.MaxExposurePerSymbolPercent, "20", SettingValueType.Decimal),
        (RiskRuleKeys.MaxTotalExposurePercent, "35", SettingValueType.Decimal),
        (RiskRuleKeys.MaxConsecutiveLosses, "3", SettingValueType.Int),
        (RiskRuleKeys.MinConfidenceScore, "65", SettingValueType.Decimal),
        (RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        (RiskRuleKeys.MinRewardRiskRatio, "1.8", SettingValueType.Decimal)
    ];

    private void DetachAddedProfiles()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<RiskProfile>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }

    private void DetachAddedRules()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<RiskRule>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateProfileException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_RiskProfiles_Name", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDuplicateRuleException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_RiskRules", StringComparison.OrdinalIgnoreCase) == true;
}
