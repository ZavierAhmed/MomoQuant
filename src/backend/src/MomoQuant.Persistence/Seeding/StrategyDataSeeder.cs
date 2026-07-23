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
        // Strategy Laboratory strategies always seed idempotently, even when full catalog seeding is disabled.
        await SeedStrategyLabStrategiesAsync(cancellationToken);

        if (!_settings.SeedDefaultStrategies)
        {
            _logger.LogInformation(
                "Default strategy seeding is disabled (StrategyCatalog:SeedDefaultStrategies=false). Skipping remaining strategy catalog seed.");
            return;
        }

        await CleanupDuplicateStrategiesAsync(cancellationToken);

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

        await EnsureDefaultParametersAsync(cancellationToken);
    }

    private async Task SeedStrategyLabStrategiesAsync(CancellationToken cancellationToken)
    {
        await EnsureStrategyAsync(
            StrategyCode.PriceStructureBreakoutRetest,
            "Price Structure Breakout + Retest",
            "Detects confirmed swing structure levels, breakout closes, retests, and confirmation using OHLC candles only.",
            cancellationToken,
            version: "1.0.0",
            isEnabled: true);

        await EnsureStrategyAsync(
            StrategyCode.PriceStructureLiquiditySweepReclaim,
            "Price Structure Liquidity Sweep + Reclaim",
            "Detects swing liquidity levels, sweeps through them, and reclaims the level using OHLC candles only.",
            cancellationToken,
            version: "1.0.0",
            isEnabled: true);

        // Ensure parameters exist even when full catalog seed is disabled.
        await EnsureDefaultParametersForCodesAsync(
            [
                StrategyCode.PriceStructureBreakoutRetest,
                StrategyCode.PriceStructureLiquiditySweepReclaim
            ],
            cancellationToken);
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
            _ => Array.Empty<(string Key, string Value, SettingValueType Type)>()
        };

        var defaultTimeframes = strategy.Code switch
        {
            StrategyCode.FourHourRangeReEntry => new[] { Timeframe.M3, Timeframe.M5, Timeframe.M15 },
            StrategyCode.BbLiquiditySweepCisd or StrategyCode.BbLiquiditySweepCisdRsiPrimed => new[] { Timeframe.M3 },
            StrategyCode.VolatilityGatedSupertrendMomentum => new[] { Timeframe.M3, Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H4 },
            StrategyCode.PriceStructureBreakoutRetest or StrategyCode.PriceStructureLiquiditySweepReclaim =>
                new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H4 },
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
        ("allowWickThroughLevel", "true", SettingValueType.Bool),
        ("maxRetestPenetrationPercent", "0.30", SettingValueType.Decimal),
        ("confirmationMode", "BullishReactionClose", SettingValueType.String),
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

    private static bool IsDuplicateStrategyException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_Strategies_Code", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDuplicateParameterException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_StrategyParameters", StringComparison.OrdinalIgnoreCase) == true;
}
