using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Seeding;

public interface IStrategyDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public sealed class StrategyDataSeeder : IStrategyDataSeeder
{
    private readonly MomoQuantDbContext _dbContext;
    private readonly StrategyCatalogSettings _settings;
    private readonly ILogger<StrategyDataSeeder> _logger;

    public StrategyDataSeeder(
        MomoQuantDbContext dbContext,
        IOptions<StrategyCatalogSettings> settings,
        ILogger<StrategyDataSeeder> logger)
    {
        _dbContext = dbContext;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await CleanupDuplicateStrategiesAsync(cancellationToken);

        await EnsureCanonicalStrategiesAsync(cancellationToken);

        await DisableNonCanonicalStrategiesAsync(cancellationToken);

        if (_settings.SeedDefaultStrategies)
        {
            await SeedLegacyStrategiesAsync(cancellationToken);
        }

        await EnsureDefaultParametersAsync(cancellationToken);
        await ReconcileCanonicalParameterContractsAsync(cancellationToken);
    }

    private async Task EnsureCanonicalStrategiesAsync(CancellationToken cancellationToken)
    {
        await EnsureStrategyAsync(
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            "MOMO Adaptive Multi-Timeframe Trend Breakout",
            "Adaptive multi-timeframe trend breakout using mapped closed-HTF EMA/slope alignment with LTF EMA, ATR ratio, MACD, breakout and retest confirmation.",
            cancellationToken,
            version: "1.0.0",
            isEnabled: true);

        await EnsureStrategyAsync(
            StrategyCode.PriceStructureBreakoutRetest,
            "Price Structure Breakout + Retest",
            "Detects confirmed swing structure levels, breakout closes, retests, and confirmation using OHLC candles only.",
            cancellationToken,
            version: "1.1.0",
            isEnabled: true);

        await EnsureStrategyAsync(
            StrategyCode.MomoVolatilityRangeReversion,
            "MOMO Volatility Range Reversion",
            "Range-bound mean reversion with range-width, EMA flatness, ATR ratio, RSI, boundary sweep/reclaim and midpoint reward/risk filters.",
            cancellationToken,
            version: "1.0.0",
            isEnabled: true);

        await EnsureDefaultParametersForCodesAsync(
            [
                StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                StrategyCode.PriceStructureBreakoutRetest,
                StrategyCode.MomoVolatilityRangeReversion
            ],
            cancellationToken);
    }

    private async Task DisableNonCanonicalStrategiesAsync(CancellationToken cancellationToken)
    {
        var canonicalCodes = new HashSet<StrategyCode>
        {
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyCode.PriceStructureBreakoutRetest,
            StrategyCode.MomoVolatilityRangeReversion
        };

        var allStrategies = await _dbContext.Strategies.ToListAsync(cancellationToken);
        var nonCanonical = allStrategies.Where(strategy => !canonicalCodes.Contains(strategy.Code)).ToList();

        foreach (var strategy in nonCanonical)
        {
            if (strategy.IsEnabled)
            {
                strategy.IsEnabled = false;
                strategy.UpdatedAtUtc = DateTime.UtcNow;
                _logger.LogInformation(
                    "Strategy {StrategyCode} disabled because it is not in canonical portfolio.",
                    strategy.Code.ToCode());
            }
        }

        if (nonCanonical.Any(strategy => !strategy.IsEnabled))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedLegacyStrategiesAsync(CancellationToken cancellationToken)
    {
        await EnsureStrategyAsync(
            StrategyCode.EmaPullback,
            "EMA Pullback",
            "Trend-continuation strategy using EMA alignment and pullback entries.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.VwapMeanReversion,
            "VWAP Mean Reversion",
            "Mean-reversion strategy using VWAP deviation and RSI extremes.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.LiquiditySweep,
            "Liquidity Sweep Reclaim",
            "Stop-hunt reversal strategy that looks for liquidity sweeps and reclaims.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.BollingerSqueezeBreakout,
            "Bollinger Squeeze Breakout",
            "Volatility contraction followed by Bollinger band breakout.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.DonchianBreakout,
            "Donchian Breakout",
            "Range breakout continuation using Donchian channel levels.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.RsiDivergenceReversal,
            "RSI Divergence Reversal",
            "Momentum divergence reversal using price and RSI swing comparisons.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.MacdMomentumContinuation,
            "MACD Momentum Continuation",
            "Momentum continuation with MACD and EMA trend confirmation.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.AtrVolatilityBreakout,
            "ATR Volatility Breakout",
            "Trades volatility expansion after range compression.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.SupportResistanceBreakoutRetest,
            "Support/Resistance Breakout Retest",
            "Breakout and retest confirmation at support or resistance levels.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.SupertrendContinuation,
            "Supertrend Continuation",
            "ATR-based trend following using Supertrend direction and pullbacks.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.FourHourRangeReEntry,
            "4H Range Re-Entry Scalping",
            "Uses the first 4 hours of the New York trading day. Enters when price closes outside the range and then closes back inside.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.BbLiquiditySweepCisd,
            "BB Liquidity Sweep CISD",
            "3-minute Bollinger Band liquidity sweep with CISD confirmation. MOMO-native liquidity-line approximation inspired by #itsimpossible.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.BbLiquiditySweepCisdRsiPrimed,
            "BB Liquidity Sweep CISD + RSI Primed",
            "Adds MOMO port of RSI Primed [ChartPrime] filter: longs below 30, shorts above 70.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.VolatilityGatedSupertrendMomentum,
            "Volatility-Gated SuperTrend Momentum",
            "SuperTrend continuation strategy filtered by ATR volatility regime and momentum confirmation to reduce sideways-market whipsaws.",
            cancellationToken);

        await EnsureStrategyAsync(
            StrategyCode.PriceStructureLiquiditySweepReclaim,
            "Price Structure Liquidity Sweep + Reclaim",
            "Detects swing liquidity levels, sweeps through them, and reclaims the level using OHLC candles only.",
            cancellationToken,
            version: "1.0.0",
            isEnabled: false);
    }


    private async Task EnsureStrategyAsync(
        StrategyCode code,
        string name,
        string description,
        CancellationToken cancellationToken,
        string version = "2.0.0",
        bool isEnabled = false)
    {
        var exists = await _dbContext.Strategies
            .FirstOrDefaultAsync(strategy => strategy.Code == code, cancellationToken);

        if (exists is not null)
        {
            var isChanged = !string.Equals(exists.Name, name, StringComparison.Ordinal)
                            || !string.Equals(exists.Description, description, StringComparison.Ordinal)
                            || !string.Equals(exists.Version, version, StringComparison.Ordinal)
                            || (isEnabled && !exists.IsEnabled);
            if (!isChanged)
            {
                _logger.LogInformation("Strategy seed: {StrategyCode} already exists.", code.ToCode());
                return;
            }

            exists.Name = name;
            exists.Description = description;
            exists.Version = version;
            if (isEnabled)
            {
                exists.IsEnabled = true;
            }

            exists.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Strategy seed: {StrategyCode} metadata updated.", code.ToCode());
            return;
        }

        var now = DateTime.UtcNow;
        _dbContext.Strategies.Add(new Strategy
        {
            Code = code,
            Name = name,
            Description = description,
            IsEnabled = isEnabled,
            Version = version,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Strategy seed: {StrategyCode} inserted.", code.ToCode());
        }
        catch (DbUpdateException ex) when (IsDuplicateStrategyException(ex))
        {
            DetachAddedStrategies();
            _logger.LogInformation("Strategy seed: {StrategyCode} already exists.", code.ToCode());
        }
    }

    private async Task EnsureDefaultParametersForCodesAsync(
        IReadOnlyCollection<StrategyCode> codes,
        CancellationToken cancellationToken)
    {
        var strategies = await _dbContext.Strategies.AsNoTracking()
            .Where(strategy => codes.Contains(strategy.Code))
            .ToListAsync(cancellationToken);

        foreach (var strategy in strategies)
        {
            await EnsureParametersForStrategyAsync(strategy, cancellationToken);
        }
    }

    private async Task CleanupDuplicateStrategiesAsync(CancellationToken cancellationToken)
    {
        var strategies = await _dbContext.Strategies
            .OrderBy(strategy => strategy.Id)
            .ToListAsync(cancellationToken);
        var duplicateGroups = strategies
            .GroupBy(strategy => strategy.Code)
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
        {
            return;
        }

        foreach (var duplicateGroup in duplicateGroups)
        {
            var keep = duplicateGroup.First();
            var canonicalName = duplicateGroup.Key == StrategyCode.LiquiditySweep ? "Liquidity Sweep Reclaim" : keep.Name;
            if (!string.Equals(keep.Name, canonicalName, StringComparison.Ordinal))
            {
                keep.Name = canonicalName;
                keep.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            foreach (var duplicate in duplicateGroup.Skip(1))
            {
                duplicate.IsEnabled = false;
                if (!duplicate.Name.Contains("Legacy Duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    duplicate.Name = $"{duplicate.Name} (Legacy Duplicate)";
                }
                duplicate.Description = "Deprecated duplicate strategy row. Kept for historical references only.";
                duplicate.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        _logger.LogWarning("Disabled duplicate strategy rows detected during strategy seeding.");
    }

    private async Task EnsureDefaultParametersAsync(CancellationToken cancellationToken)
    {
        var strategies = await _dbContext.Strategies.AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var strategy in strategies)
        {
            await EnsureParametersForStrategyAsync(strategy, cancellationToken);
        }
    }

    private async Task EnsureParametersForStrategyAsync(Strategy strategy, CancellationToken cancellationToken)
    {
        var defaults = strategy.Code switch
        {
            StrategyCode.EmaPullback => EmaPullbackDefaults,
            StrategyCode.VwapMeanReversion => VwapDefaults,
            StrategyCode.LiquiditySweep => LiquiditySweepDefaults,
            StrategyCode.BollingerSqueezeBreakout => BollingerDefaults,
            StrategyCode.DonchianBreakout => DonchianDefaults,
            StrategyCode.RsiDivergenceReversal => RsiDivergenceDefaults,
            StrategyCode.MacdMomentumContinuation => MacdDefaults,
            StrategyCode.AtrVolatilityBreakout => AtrVolatilityDefaults,
            StrategyCode.SupportResistanceBreakoutRetest => SupportResistanceDefaults,
            StrategyCode.SupertrendContinuation => SupertrendDefaults,
            StrategyCode.FourHourRangeReEntry => FourHourRangeReEntryDefaults,
            StrategyCode.BbLiquiditySweepCisd => BbLiquiditySweepDefaults,
            StrategyCode.BbLiquiditySweepCisdRsiPrimed => BbLiquiditySweepRsiDefaults,
            StrategyCode.VolatilityGatedSupertrendMomentum => VgSupertrendDefaults,
            StrategyCode.PriceStructureBreakoutRetest => PriceStructureBreakoutRetestDefaults,
            StrategyCode.PriceStructureLiquiditySweepReclaim => PriceStructureLiquiditySweepDefaults,
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout => MomoAdaptiveMultiTimeframeTrendBreakoutDefaults,
            StrategyCode.MomoVolatilityRangeReversion => MomoVolatilityRangeReversionDefaults,
            _ => Array.Empty<(string Key, string Value, SettingValueType Type)>()
        };

        var defaultTimeframes = strategy.Code switch
        {
            StrategyCode.FourHourRangeReEntry => new[] { Timeframe.M3, Timeframe.M5, Timeframe.M15 },
            StrategyCode.BbLiquiditySweepCisd or StrategyCode.BbLiquiditySweepCisdRsiPrimed => new[] { Timeframe.M3 },
            StrategyCode.VolatilityGatedSupertrendMomentum => new[] { Timeframe.M3, Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H4 },
            StrategyCode.PriceStructureBreakoutRetest or StrategyCode.PriceStructureLiquiditySweepReclaim =>
                new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H4 },
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout => new[] { Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.H4 },
            StrategyCode.MomoVolatilityRangeReversion => new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1 },
            _ => new[] { Timeframe.M3, Timeframe.M5 }
        };

        foreach (var timeframe in defaultTimeframes)
        {
            foreach (var (key, value, valueType) in defaults)
            {
                var exists = await _dbContext.StrategyParameters.AsNoTracking().AnyAsync(
                    parameter =>
                        parameter.StrategyId == strategy.Id &&
                        parameter.ParameterKey == key &&
                        parameter.Timeframe == timeframe &&
                        parameter.SymbolId == null,
                    cancellationToken);

                if (exists)
                {
                    continue;
                }

                _dbContext.StrategyParameters.Add(new StrategyParameter
                {
                    StrategyId = strategy.Id,
                    ParameterKey = key,
                    ParameterValue = value,
                    ValueType = valueType,
                    Timeframe = timeframe,
                    SymbolId = null,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsDuplicateParameterException(ex))
                {
                    DetachAddedParameters();
                }
            }
        }
    }

    private async Task ReconcileCanonicalParameterContractsAsync(CancellationToken cancellationToken)
    {
        var strategies = await _dbContext.Strategies
            .Where(strategy =>
                strategy.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout
                || strategy.Code == StrategyCode.MomoVolatilityRangeReversion)
            .ToListAsync(cancellationToken);

        foreach (var strategy in strategies)
        {
            var obsoleteKeys = strategy.Code switch
            {
                StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout => MomoAdaptiveObsoleteParameterKeys,
                StrategyCode.MomoVolatilityRangeReversion => MomoRangeObsoleteParameterKeys,
                _ => Array.Empty<string>()
            };

            var defaults = strategy.Code switch
            {
                StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout => MomoAdaptiveMultiTimeframeTrendBreakoutDefaults,
                StrategyCode.MomoVolatilityRangeReversion => MomoVolatilityRangeReversionDefaults,
                _ => Array.Empty<(string Key, string Value, SettingValueType Type)>()
            };

            var defaultTimeframes = strategy.Code switch
            {
                StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout => new[] { Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.H4 },
                StrategyCode.MomoVolatilityRangeReversion => new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1 },
                _ => Array.Empty<Timeframe>()
            };

            var parameters = await _dbContext.StrategyParameters
                .Where(parameter => parameter.StrategyId == strategy.Id && parameter.SymbolId == null)
                .ToListAsync(cancellationToken);

            foreach (var parameter in parameters)
            {
                if (obsoleteKeys.Contains(parameter.ParameterKey, StringComparer.OrdinalIgnoreCase) && parameter.IsActive)
                {
                    parameter.IsActive = false;
                    parameter.UpdatedAtUtc = DateTime.UtcNow;
                }

                // Upgrade Adaptive fixedRewardRisk only when reliable seed provenance identifies the old 2.0 seed.
                // Value alone must never classify a row as seeded — preserve arbitrary user 2.0 rows.
                if (strategy.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout
                    && string.Equals(parameter.ParameterKey, "fixedRewardRisk", StringComparison.OrdinalIgnoreCase)
                    && parameter.IsActive
                    && parameter.SymbolId is null
                    && HasAdaptiveRewardRiskSeedProvenance(parameters, parameter.Timeframe, AdaptiveRewardRiskSeedProvenanceV0)
                    && IsExactDecimalValue(parameter.ParameterValue, 2.0m))
                {
                    parameter.ParameterValue = "2.50";
                    parameter.ValueType = SettingValueType.Decimal;
                    parameter.UpdatedAtUtc = DateTime.UtcNow;
                    UpsertAdaptiveSeedProvenance(parameters, strategy.Id, parameter.Timeframe, AdaptiveRewardRiskSeedProvenanceV1);
                }
            }

            var defaultLookup = defaults.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var timeframe in defaultTimeframes)
            {
                foreach (var (key, value, valueType) in defaults)
                {
                    var existing = parameters.FirstOrDefault(parameter =>
                        string.Equals(parameter.ParameterKey, key, StringComparison.OrdinalIgnoreCase)
                        && parameter.Timeframe == timeframe
                        && parameter.SymbolId is null);

                    if (existing is null)
                    {
                        var added = new StrategyParameter
                        {
                            StrategyId = strategy.Id,
                            ParameterKey = key,
                            ParameterValue = value,
                            ValueType = valueType,
                            Timeframe = timeframe,
                            SymbolId = null,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.StrategyParameters.Add(added);
                        parameters.Add(added);
                        continue;
                    }

                    // Normalize key casing to the contract key without creating a duplicate row.
                    if (!string.Equals(existing.ParameterKey, key, StringComparison.Ordinal))
                    {
                        existing.ParameterKey = key;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }

                    if (!existing.IsActive)
                    {
                        existing.IsActive = true;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }

                    // Backfill contract defaults only when the row still carries an empty/missing contract value.
                    if (string.IsNullOrWhiteSpace(existing.ParameterValue)
                        && defaultLookup.TryGetValue(key, out var contractDefault))
                    {
                        existing.ParameterValue = contractDefault.Value;
                        existing.ValueType = contractDefault.Type;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }

                if (strategy.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout)
                {
                    EnsureAdaptiveSeedProvenance(parameters, strategy.Id, timeframe, AdaptiveRewardRiskSeedProvenanceV1);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private const string SeedProvenanceParameterKey = "seedProvenance";
    private const string AdaptiveRewardRiskSeedProvenanceV0 = "231A1-adaptive-rr-2.0";
    private const string AdaptiveRewardRiskSeedProvenanceV1 = "231A1-adaptive-v1";

    private static bool HasAdaptiveRewardRiskSeedProvenance(
        IReadOnlyList<StrategyParameter> parameters,
        Timeframe timeframe,
        string expectedProvenance) =>
        parameters.Any(parameter =>
            string.Equals(parameter.ParameterKey, SeedProvenanceParameterKey, StringComparison.OrdinalIgnoreCase)
            && parameter.Timeframe == timeframe
            && parameter.SymbolId is null
            && string.Equals(parameter.ParameterValue, expectedProvenance, StringComparison.Ordinal));

    private void UpsertAdaptiveSeedProvenance(
        List<StrategyParameter> parameters,
        long strategyId,
        Timeframe timeframe,
        string provenanceValue)
    {
        var existing = parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.ParameterKey, SeedProvenanceParameterKey, StringComparison.OrdinalIgnoreCase)
            && parameter.Timeframe == timeframe
            && parameter.SymbolId is null);

        if (existing is null)
        {
            var added = new StrategyParameter
            {
                StrategyId = strategyId,
                ParameterKey = SeedProvenanceParameterKey,
                ParameterValue = provenanceValue,
                ValueType = SettingValueType.String,
                Timeframe = timeframe,
                SymbolId = null,
                IsActive = false,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.StrategyParameters.Add(added);
            parameters.Add(added);
            return;
        }

        existing.ParameterValue = provenanceValue;
        existing.ValueType = SettingValueType.String;
        existing.IsActive = false;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    private void EnsureAdaptiveSeedProvenance(
        List<StrategyParameter> parameters,
        long strategyId,
        Timeframe timeframe,
        string provenanceValue)
    {
        if (HasAdaptiveRewardRiskSeedProvenance(parameters, timeframe, provenanceValue)
            || HasAdaptiveRewardRiskSeedProvenance(parameters, timeframe, AdaptiveRewardRiskSeedProvenanceV0))
        {
            return;
        }

        UpsertAdaptiveSeedProvenance(parameters, strategyId, timeframe, provenanceValue);
    }

    private static bool IsExactDecimalValue(string? value, decimal expected)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed)
               && parsed == expected;
    }

    private void DetachAddedStrategies()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<Strategy>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }

    private void DetachAddedParameters()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<StrategyParameter>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }

    private static readonly (string Key, string Value, SettingValueType Type)[] EmaPullbackDefaults =
    [
        ("PullbackTolerancePercent", "0.25", SettingValueType.Decimal),
        ("RequireEma200", "false", SettingValueType.Bool),
        ("RequireVolumeConfirmation", "false", SettingValueType.Bool),
        ("RequireCandleConfirmation", "true", SettingValueType.Bool),
        ("MinStrength", "50", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.0", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] VwapDefaults =
    [
        ("VwapDeviationPercent", "0.15", SettingValueType.Decimal),
        ("RsiOversold", "35", SettingValueType.Decimal),
        ("RsiOverbought", "65", SettingValueType.Decimal),
        ("MaxAtrPercent", "3.0", SettingValueType.Decimal),
        ("RequireWickRejection", "false", SettingValueType.Bool),
        ("MinStrength", "50", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.2", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "1.5", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] LiquiditySweepDefaults =
    [
        ("SwingLookback", "2", SettingValueType.Int),
        ("SweepLookbackCandles", "3", SettingValueType.Int),
        ("MinWickPercent", "30", SettingValueType.Decimal),
        ("RequireVolumeSpike", "false", SettingValueType.Bool),
        ("VolumeSpikeMultiplier", "1.2", SettingValueType.Decimal),
        ("MinStrength", "50", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.2", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.0", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] BollingerDefaults =
    [
        ("SqueezeBandwidthPercent", "1.0", SettingValueType.Decimal),
        ("SqueezeLookback", "20", SettingValueType.Int),
        ("VolumeMultiplier", "1.1", SettingValueType.Decimal),
        ("RequireVolumeConfirmation", "true", SettingValueType.Bool),
        ("MaxAtrPercent", "4.0", SettingValueType.Decimal),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.5", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] DonchianDefaults =
    [
        ("DonchianPeriod", "20", SettingValueType.Int),
        ("RequireVolumeConfirmation", "false", SettingValueType.Bool),
        ("VolumeMultiplier", "1.0", SettingValueType.Decimal),
        ("MinBreakoutPercent", "0.05", SettingValueType.Decimal),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.5", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] RsiDivergenceDefaults =
    [
        ("DivergenceLookback", "20", SettingValueType.Int),
        ("ConfirmationCandles", "3", SettingValueType.Int),
        ("RsiOversoldZone", "40", SettingValueType.Decimal),
        ("RsiOverboughtZone", "60", SettingValueType.Decimal),
        ("RequireConfirmationClose", "true", SettingValueType.Bool),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.3", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.0", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] MacdDefaults =
    [
        ("RequireEmaTrend", "true", SettingValueType.Bool),
        ("RequireHistogramExpansion", "true", SettingValueType.Bool),
        ("MinHistogramChange", "0", SettingValueType.Decimal),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.0", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] AtrVolatilityDefaults =
    [
        ("RangeLookback", "20", SettingValueType.Int),
        ("CompressionAtrPercent", "1.0", SettingValueType.Decimal),
        ("BreakoutBufferPercent", "0.05", SettingValueType.Decimal),
        ("MaxAtrPercent", "4.0", SettingValueType.Decimal),
        ("RequireVolumeConfirmation", "false", SettingValueType.Bool),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.5", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] SupportResistanceDefaults =
    [
        ("LevelLookback", "50", SettingValueType.Int),
        ("RetestLookbackCandles", "10", SettingValueType.Int),
        ("RetestTolerancePercent", "0.15", SettingValueType.Decimal),
        ("RequireVolumeOnBreakout", "false", SettingValueType.Bool),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.3", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.0", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] SupertrendDefaults =
    [
        ("SupertrendPeriod", "10", SettingValueType.Int),
        ("SupertrendMultiplier", "3.0", SettingValueType.Decimal),
        ("PullbackTolerancePercent", "0.25", SettingValueType.Decimal),
        ("RequireVolumeConfirmation", "false", SettingValueType.Bool),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("StopLossAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("TakeProfitAtrMultiplier", "2.0", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] VgSupertrendDefaults =
    [
        ("atrPeriod", "10", SettingValueType.Int),
        ("superTrendMultiplier", "3.0", SettingValueType.Decimal),
        ("fastAtrPeriod", "14", SettingValueType.Int),
        ("slowAtrPeriod", "100", SettingValueType.Int),
        ("minVolatilityRatio", "1.05", SettingValueType.Decimal),
        ("macdFast", "12", SettingValueType.Int),
        ("macdSlow", "26", SettingValueType.Int),
        ("macdSignal", "9", SettingValueType.Int),
        ("minHistogramStrength", "0", SettingValueType.Decimal),
        ("retestAtrTolerance", "0.35", SettingValueType.Decimal),
        ("maxBarsAfterTrendFlip", "20", SettingValueType.Int),
        ("requireRetest", "true", SettingValueType.Bool),
        ("allowTrendContinuationEntry", "false", SettingValueType.Bool),
        ("stopMode", "SuperTrendLine", SettingValueType.String),
        ("stopAtrMultiplier", "1.5", SettingValueType.Decimal),
        ("stopBufferAtrMultiplier", "0.1", SettingValueType.Decimal),
        ("targetMode", "FixedR", SettingValueType.String),
        ("fixedRewardRisk", "2.0", SettingValueType.Decimal),
        ("target2RewardRisk", "3.0", SettingValueType.Decimal),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("MinRewardRisk", "1.2", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] BbLiquiditySweepDefaults =
    [
        ("BbStrategyStrictnessProfile", "BalancedResearch", SettingValueType.String),
        ("BbPeriod", "20", SettingValueType.Int),
        ("BbStdDev", "2.0", SettingValueType.Decimal),
        ("UseSessionFilter", "true", SettingValueType.Bool),
        ("StopAfterLossesPerSession", "2", SettingValueType.Int),
        ("RequireSweepOutsideBb", "true", SettingValueType.Bool),
        ("RequireCloseBackInsideBb", "false", SettingValueType.Bool),
        ("RequireCloseBackAcrossLiquidityLine", "false", SettingValueType.Bool),
        ("MaxBarsAfterSweep", "5", SettingValueType.Int),
        ("MinRewardRisk", "2.5", SettingValueType.Decimal),
        ("ResearchMinRewardRisk3R", "3.0", SettingValueType.Decimal),
        ("SwingLeft", "2", SettingValueType.Int),
        ("SwingRight", "2", SettingValueType.Int),
        ("EqualHighLowToleranceAtrMultiplier", "0.25", SettingValueType.Decimal),
        ("MinTouches", "1", SettingValueType.Int),
        ("IncludeSingleSwingLevels", "true", SettingValueType.Bool),
        ("MaxLevelAgeCandles", "300", SettingValueType.Int),
        ("LevelMergeToleranceAtrMultiplier", "0.15", SettingValueType.Decimal),
        ("MaxDistanceFromLiquidityAtrMultiplier", "0.35", SettingValueType.Decimal),
        ("AllowSweepOfAnyRecentSwing", "true", SettingValueType.Bool),
        ("MinStrength", "55", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] BbLiquiditySweepRsiDefaults =
        BbLiquiditySweepDefaults.Concat(
        [
            ("RsiLength", "24", SettingValueType.Int),
            ("RsiSmoothing", "3", SettingValueType.Int),
            ("RsiUseHeikinAshi", "true", SettingValueType.Bool),
            ("RsiOversoldLevel", "30", SettingValueType.Decimal),
            ("RsiOverboughtLevel", "70", SettingValueType.Decimal),
            ("RsiPrimedSignalValueMode", "HaClose", SettingValueType.String)
        ]).ToArray();

    private static readonly (string Key, string Value, SettingValueType Type)[] FourHourRangeReEntryDefaults =
    [
        ("AnchorTimezone", "America/New_York", SettingValueType.String),
        ("RangeStartHour", "0", SettingValueType.Int),
        ("RangeDurationHours", "4", SettingValueType.Int),
        ("RewardRiskRatio", "2.0", SettingValueType.Decimal),
        ("MaxTradesPerDay", "3", SettingValueType.Int),
        ("AllowMultipleTradesPerDay", "true", SettingValueType.Bool),
        ("RequireCloseOutsideRange", "true", SettingValueType.Bool),
        ("RequireCloseBackInsideRange", "true", SettingValueType.Bool),
        ("UseWicksForBreakout", "false", SettingValueType.Bool),
        ("EntryMode", "Close", SettingValueType.String),
        ("StopMode", "BreakoutExtreme", SettingValueType.String),
        ("StopLossBufferPercent", "0.02", SettingValueType.Decimal),
        ("StopLossBufferTicks", "0", SettingValueType.Decimal),
        ("StopLossBufferAtrMultiplier", "0", SettingValueType.Decimal),
        ("MaxStopDistancePercent", "1.5", SettingValueType.Decimal),
        ("AllowLargeBreakoutStructureStop", "false", SettingValueType.Bool),
        ("MinRangePercent", "0.10", SettingValueType.Decimal),
        ("MaxRangePercent", "4.00", SettingValueType.Decimal),
        ("MinStrength", "55", SettingValueType.Decimal),
        ("SupportedTimeframes", "3m,5m,15m", SettingValueType.String),
        ("PreferredTimeframe", "5m", SettingValueType.String),
        ("DisableAfterNewYorkDayEnd", "true", SettingValueType.Bool),
        ("AllowChoppy", "false", SettingValueType.Bool),
        ("AllowHighVolatility", "false", SettingValueType.Bool)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] PriceStructureBreakoutRetestDefaults =
    [
        ("swingLeftBars", "2", SettingValueType.Int),
        ("swingRightBars", "2", SettingValueType.Int),
        ("minSwingDistanceBars", "3", SettingValueType.Int),
        ("useWicksForSwing", "true", SettingValueType.Bool),
        ("minBreakoutClosePercent", "0", SettingValueType.Decimal),
        ("breakoutMustCloseBeyondLevel", "true", SettingValueType.Bool),
        ("maxRetestBars", "20", SettingValueType.Int),
        ("retestTolerancePercent", "0.15", SettingValueType.Decimal),
        ("retestToleranceMode", "Percent", SettingValueType.String),
        ("retestToleranceAtrMultiplier", "0.25", SettingValueType.Decimal),
        ("allowWickThroughLevel", "true", SettingValueType.Bool),
        ("maxRetestPenetrationPercent", "0.30", SettingValueType.Decimal),
        ("confirmationMode", "ReactionClose", SettingValueType.String),
        ("fixedRewardRisk", "2.0", SettingValueType.Decimal),
        ("stopBufferPercent", "0.05", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] PriceStructureLiquiditySweepDefaults =
    [
        ("swingLeftBars", "2", SettingValueType.Int),
        ("swingRightBars", "2", SettingValueType.Int),
        ("maxLiquidityLevelAgeBars", "200", SettingValueType.Int),
        ("includeSingleSwingLevels", "true", SettingValueType.Bool),
        ("includeEqualHighLowLevels", "true", SettingValueType.Bool),
        ("equalLevelTolerancePercent", "0.10", SettingValueType.Decimal),
        ("maxReclaimBars", "1", SettingValueType.Int),
        ("requireSameCandleReclaim", "true", SettingValueType.Bool),
        ("minimumSweepDistancePercent", "0", SettingValueType.Decimal),
        ("confirmationMode", "ReclaimCloseOnly", SettingValueType.String),
        ("fixedRewardRisk", "2.0", SettingValueType.Decimal),
        ("stopBufferPercent", "0.05", SettingValueType.Decimal)
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] MomoAdaptiveMultiTimeframeTrendBreakoutDefaults =
    [
        ("htfFastEmaPeriod", "50", SettingValueType.Int),
        ("htfSlowEmaPeriod", "200", SettingValueType.Int),
        ("htfSlopeLookback", "5", SettingValueType.Int),
        ("ltfFastEmaPeriod", "20", SettingValueType.Int),
        ("ltfSlowEmaPeriod", "50", SettingValueType.Int),
        ("breakoutLookback", "20", SettingValueType.Int),
        ("fastAtrPeriod", "14", SettingValueType.Int),
        ("slowAtrPeriod", "100", SettingValueType.Int),
        ("minVolatilityRatio", "1.00", SettingValueType.Decimal),
        ("maxVolatilityRatio", "2.25", SettingValueType.Decimal),
        ("baseBreakoutBufferAtr", "0.10", SettingValueType.Decimal),
        ("volatilitySensitivity", "0.15", SettingValueType.Decimal),
        ("minBreakoutBufferAtr", "0.05", SettingValueType.Decimal),
        ("maxBreakoutBufferAtr", "0.35", SettingValueType.Decimal),
        ("macdFast", "12", SettingValueType.Int),
        ("macdSlow", "26", SettingValueType.Int),
        ("macdSignal", "9", SettingValueType.Int),
        ("requireHistogramExpansion", "true", SettingValueType.Bool),
        ("maxRetestBars", "8", SettingValueType.Int),
        ("retestToleranceAtr", "0.35", SettingValueType.Decimal),
        ("maxBreakoutChaseAtr", "1.00", SettingValueType.Decimal),
        ("stopBufferAtr", "0.20", SettingValueType.Decimal),
        ("fixedRewardRisk", "2.50", SettingValueType.Decimal),
        ("minStrength", "70", SettingValueType.Decimal)
    ];

    private static readonly string[] MomoAdaptiveObsoleteParameterKeys =
    [
        "htfTrendTimeframe",
        "htfStructureTimeframe",
        "requireHtfTrendAlignment",
        "requireHtfStructureBreak",
        "minBreakoutStrength",
        "stopBufferPercent"
    ];

    private static readonly (string Key, string Value, SettingValueType Type)[] MomoVolatilityRangeReversionDefaults =
    [
        ("rangeLookback", "48", SettingValueType.Int),
        ("minRangeWidthAtr", "3.0", SettingValueType.Decimal),
        ("maxRangeWidthAtr", "12.0", SettingValueType.Decimal),
        ("fastEmaPeriod", "20", SettingValueType.Int),
        ("slowEmaPeriod", "50", SettingValueType.Int),
        ("maxEmaSeparationAtr", "0.50", SettingValueType.Decimal),
        ("slopeLookback", "5", SettingValueType.Int),
        ("maxSlowEmaSlopeAtr", "0.15", SettingValueType.Decimal),
        ("fastAtrPeriod", "14", SettingValueType.Int),
        ("slowAtrPeriod", "100", SettingValueType.Int),
        ("minVolatilityRatio", "0.65", SettingValueType.Decimal),
        ("maxVolatilityRatio", "1.25", SettingValueType.Decimal),
        ("rsiPeriod", "14", SettingValueType.Int),
        ("rsiOversold", "35", SettingValueType.Decimal),
        ("rsiOverbought", "65", SettingValueType.Decimal),
        ("boundaryToleranceAtr", "0.15", SettingValueType.Decimal),
        ("minimumWickPercent", "30", SettingValueType.Decimal),
        ("stopBufferAtr", "0.25", SettingValueType.Decimal),
        ("minimumRewardRisk", "1.25", SettingValueType.Decimal),
        ("targetMode", "RangeMidpoint", SettingValueType.String),
        ("minStrength", "65", SettingValueType.Decimal)
    ];

    private static readonly string[] MomoRangeObsoleteParameterKeys =
    [
        "rangeLookbackBars",
        "maxVolatilityAtrPercent",
        "meanReversionZonePercent",
        "requireRangeConfirmation",
        "fixedRewardRisk",
        "stopBufferPercent"
    ];

    private static bool IsDuplicateStrategyException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_Strategies_Code", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDuplicateParameterException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_StrategyParameters", StringComparison.OrdinalIgnoreCase) == true;
}
