using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Domain.Constants;

namespace MomoQuant.Application.Strategies.Optimization;

public sealed class StrategyParameterDefinitionDto
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Type { get; init; }
    public required string DefaultValue { get; init; }
    public string? MinValue { get; init; }
    public string? MaxValue { get; init; }
    public string? Step { get; init; }
    public IReadOnlyList<string>? AllowedValues { get; init; }
    public bool IsOptimizable { get; init; } = true;
    public string? OptimizationGroup { get; init; }
    public string? Description { get; init; }
}

public interface IStrategyParameterDefinitionProvider
{
    IReadOnlyList<StrategyParameterDefinitionDto> GetDefinitions(string strategyCode);
    int EstimateGridCombinations(string strategyCode, IReadOnlyDictionary<string, string>? rangeOverrides = null);
    IReadOnlyList<IReadOnlyDictionary<string, string>> GenerateGridCombinations(
        string strategyCode,
        int maxCombinations,
        IReadOnlyDictionary<string, string>? rangeOverrides = null);
}

public sealed class StrategyParameterDefinitionProvider : IStrategyParameterDefinitionProvider
{
    private static readonly Dictionary<string, IReadOnlyList<StrategyParameterDefinitionDto>> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Domain.Constants.StrategyCodes.VolatilityGatedSupertrendMomentum] = BuildVgSupertrendDefinitions(),
            [StrategyCodes.PriceStructureBreakoutRetest] = BuildPriceStructureBreakoutDefinitions(),
            [StrategyCodes.PriceStructureLiquiditySweepReclaim] = BuildPriceStructureLiquiditySweepDefinitions()
        };

    public IReadOnlyList<StrategyParameterDefinitionDto> GetDefinitions(string strategyCode) =>
        Definitions.TryGetValue(strategyCode, out var defs) ? defs : [];

    public int EstimateGridCombinations(string strategyCode, IReadOnlyDictionary<string, string>? rangeOverrides = null) =>
        GenerateGridCombinations(strategyCode, int.MaxValue, rangeOverrides).Count;

    public IReadOnlyList<IReadOnlyDictionary<string, string>> GenerateGridCombinations(
        string strategyCode,
        int maxCombinations,
        IReadOnlyDictionary<string, string>? rangeOverrides = null)
    {
        if (!Definitions.TryGetValue(strategyCode, out var defs))
        {
            return [];
        }

        var optimizable = defs.Where(d => d.IsOptimizable).ToList();
        if (optimizable.Count == 0)
        {
            if (string.Equals(
                    strategyCode,
                    Domain.Constants.StrategyCodes.VolatilityGatedSupertrendMomentum,
                    StringComparison.OrdinalIgnoreCase))
            {
                return [VolatilityGatedSuperTrendParameters.From(new Dictionary<string, string>()).ToDictionary()];
            }

            var defaults = defs.ToDictionary(d => d.Key, d => d.DefaultValue, StringComparer.OrdinalIgnoreCase);
            return [defaults];
        }

        var axes = optimizable.Select(def => BuildAxis(def, rangeOverrides)).ToList();
        var results = new List<IReadOnlyDictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Recurse(int depth)
        {
            if (results.Count >= maxCombinations) return;
            if (depth >= axes.Count)
            {
                Dictionary<string, string> merged;
                if (string.Equals(
                        strategyCode,
                        Domain.Constants.StrategyCodes.VolatilityGatedSupertrendMomentum,
                        StringComparison.OrdinalIgnoreCase))
                {
                    merged = new Dictionary<string, string>(
                        VolatilityGatedSuperTrendParameters.From(current).ToDictionary(),
                        StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    merged = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
                    foreach (var def in defs.Where(d => !d.IsOptimizable))
                    {
                        if (!merged.ContainsKey(def.Key))
                        {
                            merged[def.Key] = def.DefaultValue;
                        }
                    }
                }

                results.Add(merged);
                return;
            }

            foreach (var value in axes[depth])
            {
                current[optimizable[depth].Key] = value;
                Recurse(depth + 1);
                if (results.Count >= maxCombinations) return;
            }
        }

        Recurse(0);
        return results;
    }

    private static List<string> BuildAxis(StrategyParameterDefinitionDto def, IReadOnlyDictionary<string, string>? overrides)
    {
        if (def.Type == "bool")
        {
            return ["true", "false"];
        }

        if (def.AllowedValues is { Count: > 0 })
        {
            return def.AllowedValues.ToList();
        }

        var minStr = overrides?.GetValueOrDefault($"{def.Key}Min") ?? def.MinValue;
        var maxStr = overrides?.GetValueOrDefault($"{def.Key}Max") ?? def.MaxValue;
        var stepStr = overrides?.GetValueOrDefault($"{def.Key}Step") ?? def.Step;

        if (def.Type == "int" &&
            int.TryParse(minStr, out var minInt) &&
            int.TryParse(maxStr, out var maxInt) &&
            int.TryParse(stepStr, out var stepInt) &&
            stepInt > 0)
        {
            var values = new List<string>();
            for (var v = minInt; v <= maxInt; v += stepInt)
            {
                values.Add(v.ToString());
            }

            return values;
        }

        if (def.Type == "decimal" &&
            decimal.TryParse(minStr, out var minDec) &&
            decimal.TryParse(maxStr, out var maxDec) &&
            decimal.TryParse(stepStr, out var stepDec) &&
            stepDec > 0m)
        {
            var values = new List<string>();
            for (var v = minDec; v <= maxDec + (stepDec / 2m); v += stepDec)
            {
                values.Add(v.ToString("G"));
            }

            return values;
        }

        return [def.DefaultValue];
    }

    private static IReadOnlyList<StrategyParameterDefinitionDto> BuildVgSupertrendDefinitions() =>
    [
        IntDef("atrPeriod", "ATR Period", 10, 7, 21, 1, "SuperTrend"),
        DecDef("superTrendMultiplier", "SuperTrend Multiplier", 3.0m, 1.5m, 4.0m, 0.25m, "SuperTrend"),
        IntDef("fastAtrPeriod", "Fast ATR Period", 14, 7, 30, 1, "Volatility"),
        IntDef("slowAtrPeriod", "Slow ATR Period", 100, 50, 200, 10, "Volatility"),
        DecDef("minVolatilityRatio", "Min Volatility Ratio", 1.05m, 0.85m, 1.20m, 0.05m, "Volatility"),
        IntDef("macdFast", "MACD Fast", 12, 8, 16, 1, "Momentum"),
        IntDef("macdSlow", "MACD Slow", 26, 20, 35, 1, "Momentum"),
        IntDef("macdSignal", "MACD Signal", 9, 5, 12, 1, "Momentum"),
        DecDef("retestAtrTolerance", "Retest ATR Tolerance", 0.35m, 0.20m, 0.80m, 0.05m, "Retest"),
        DecDef("fixedRewardRisk", "Fixed Reward/Risk", 2.0m, 1.2m, 2.5m, 0.2m, "Targets"),
        BoolDef("requireRetest", "Require Retest", true, optimizable: true)
    ];

    private static IReadOnlyList<StrategyParameterDefinitionDto> BuildPriceStructureBreakoutDefinitions() =>
    [
        IntDef("swingLeftBars", "Swing Left Bars", 2, 1, 5, 1, "Structure"),
        IntDef("swingRightBars", "Swing Right Bars", 2, 1, 5, 1, "Structure"),
        IntDef("maxRetestBars", "Max Retest Bars", 20, 5, 40, 5, "Retest"),
        DecDef("retestTolerancePercent", "Retest Tolerance %", 0.15m, 0.05m, 0.50m, 0.05m, "Retest"),
        DecDef("fixedRewardRisk", "Fixed Reward/Risk", 2.0m, 1.0m, 3.0m, 0.5m, "Targets"),
        DecDef("stopBufferPercent", "Stop Buffer %", 0.05m, 0m, 0.20m, 0.05m, "Risk"),
        BoolDef("useWicksForSwing", "Use Wicks For Swing", true, optimizable: false),
        BoolDef("breakoutMustCloseBeyondLevel", "Breakout Must Close Beyond Level", true, optimizable: false)
    ];

    private static IReadOnlyList<StrategyParameterDefinitionDto> BuildPriceStructureLiquiditySweepDefinitions() =>
    [
        IntDef("swingLeftBars", "Swing Left Bars", 2, 1, 5, 1, "Structure"),
        IntDef("swingRightBars", "Swing Right Bars", 2, 1, 5, 1, "Structure"),
        IntDef("maxLiquidityLevelAgeBars", "Max Liquidity Level Age Bars", 200, 50, 400, 50, "Structure"),
        DecDef("fixedRewardRisk", "Fixed Reward/Risk", 2.0m, 1.0m, 3.0m, 0.5m, "Targets"),
        DecDef("stopBufferPercent", "Stop Buffer %", 0.05m, 0m, 0.20m, 0.05m, "Risk"),
        BoolDef("requireSameCandleReclaim", "Require Same Candle Reclaim", true, optimizable: false)
    ];

    private static StrategyParameterDefinitionDto IntDef(string key, string label, int def, int min, int max, int step, string group) => new()
    {
        Key = key, Label = label, Type = "int", DefaultValue = def.ToString(),
        MinValue = min.ToString(), MaxValue = max.ToString(), Step = step.ToString(),
        OptimizationGroup = group, IsOptimizable = true
    };

    private static StrategyParameterDefinitionDto DecDef(string key, string label, decimal def, decimal min, decimal max, decimal step, string group) => new()
    {
        Key = key, Label = label, Type = "decimal", DefaultValue = def.ToString("G"),
        MinValue = min.ToString("G"), MaxValue = max.ToString("G"), Step = step.ToString("G"),
        OptimizationGroup = group, IsOptimizable = true
    };

    private static StrategyParameterDefinitionDto BoolDef(string key, string label, bool def, bool optimizable) => new()
    {
        Key = key, Label = label, Type = "bool", DefaultValue = def.ToString(),
        IsOptimizable = optimizable
    };
}
