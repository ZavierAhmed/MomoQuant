using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Settings;

namespace MomoQuant.Persistence.Repositories;

public sealed class TradingSettingsRepository : ITradingSettingsRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public TradingSettingsRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.AppSettings
            .AsNoTracking()
            .Where(item => item.SettingKey.StartsWith("TradingSettings."))
            .ToListAsync(cancellationToken);

        return items.ToDictionary(item => item.SettingKey, item => item.SettingValue, StringComparer.OrdinalIgnoreCase);
    }

    public async Task UpsertAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var keys = values.Keys.ToList();
        var existing = await _dbContext.AppSettings
            .Where(item => keys.Contains(item.SettingKey))
            .ToListAsync(cancellationToken);

        foreach (var pair in values)
        {
            var setting = existing.FirstOrDefault(item => string.Equals(item.SettingKey, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (setting is null)
            {
                _dbContext.AppSettings.Add(new AppSetting
                {
                    SettingKey = pair.Key,
                    SettingValue = pair.Value,
                    ValueType = SettingValueType.String,
                    Category = "TradingSettings",
                    IsSensitive = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                continue;
            }

            setting.SettingValue = pair.Value;
            setting.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
