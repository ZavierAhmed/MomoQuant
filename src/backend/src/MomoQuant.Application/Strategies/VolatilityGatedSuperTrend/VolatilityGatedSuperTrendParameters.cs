using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public sealed class VolatilityGatedSuperTrendParameters
{
    public int AtrPeriod { get; init; } = 10;
    public decimal SuperTrendMultiplier { get; init; } = 3.0m;
    public int FastAtrPeriod { get; init; } = 14;
    public int SlowAtrPeriod { get; init; } = 100;
    public decimal MinVolatilityRatio { get; init; } = 1.05m;
    public int MacdFast { get; init; } = 12;
    public int MacdSlow { get; init; } = 26;
    public int MacdSignal { get; init; } = 9;
    public decimal MinHistogramStrength { get; init; } = 0m;
    public decimal RetestAtrTolerance { get; init; } = 0.35m;
    public int MaxBarsAfterTrendFlip { get; init; } = 20;
    public bool RequireRetest { get; init; } = true;
    public bool AllowTrendContinuationEntry { get; init; }
    public string StopMode { get; init; } = "SuperTrendLine";
    public decimal StopAtrMultiplier { get; init; } = 1.5m;
    public decimal StopBufferAtrMultiplier { get; init; } = 0.1m;
    public string TargetMode { get; init; } = "FixedR";
    public decimal FixedRewardRisk { get; init; } = 2.0m;
    public decimal? Target2RewardRisk { get; init; } = 3.0m;
    public decimal MinStrength { get; init; } = 55m;
    public decimal MinRewardRisk { get; init; } = 1.2m;

    public static VolatilityGatedSuperTrendParameters From(IReadOnlyDictionary<string, string> parameters) => new()
    {
        AtrPeriod = StrategyParameterReader.GetInt(parameters, "atrPeriod", 10),
        SuperTrendMultiplier = StrategyParameterReader.GetDecimal(parameters, "superTrendMultiplier", 3.0m),
        FastAtrPeriod = StrategyParameterReader.GetInt(parameters, "fastAtrPeriod", 14),
        SlowAtrPeriod = StrategyParameterReader.GetInt(parameters, "slowAtrPeriod", 100),
        MinVolatilityRatio = StrategyParameterReader.GetDecimal(parameters, "minVolatilityRatio", 1.05m),
        MacdFast = StrategyParameterReader.GetInt(parameters, "macdFast", 12),
        MacdSlow = StrategyParameterReader.GetInt(parameters, "macdSlow", 26),
        MacdSignal = StrategyParameterReader.GetInt(parameters, "macdSignal", 9),
        MinHistogramStrength = StrategyParameterReader.GetDecimal(parameters, "minHistogramStrength", 0m),
        RetestAtrTolerance = StrategyParameterReader.GetDecimal(parameters, "retestAtrTolerance", 0.35m),
        MaxBarsAfterTrendFlip = StrategyParameterReader.GetInt(parameters, "maxBarsAfterTrendFlip", 20),
        RequireRetest = StrategyParameterReader.GetBool(parameters, "requireRetest", true),
        AllowTrendContinuationEntry = StrategyParameterReader.GetBool(parameters, "allowTrendContinuationEntry", false),
        StopMode = StrategyParameterReader.GetString(parameters, "stopMode", "SuperTrendLine"),
        StopAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "stopAtrMultiplier", 1.5m),
        StopBufferAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "stopBufferAtrMultiplier", 0.1m),
        TargetMode = StrategyParameterReader.GetString(parameters, "targetMode", "FixedR"),
        FixedRewardRisk = StrategyParameterReader.GetDecimal(parameters, "fixedRewardRisk", 2.0m),
        Target2RewardRisk = StrategyParameterReader.GetDecimal(parameters, "target2RewardRisk", 3.0m),
        MinStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m),
        MinRewardRisk = StrategyParameterReader.GetDecimal(parameters, "MinRewardRisk", 1.2m)
    };

    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["atrPeriod"] = AtrPeriod.ToString(),
            ["superTrendMultiplier"] = SuperTrendMultiplier.ToString("G"),
            ["fastAtrPeriod"] = FastAtrPeriod.ToString(),
            ["slowAtrPeriod"] = SlowAtrPeriod.ToString(),
            ["minVolatilityRatio"] = MinVolatilityRatio.ToString("G"),
            ["macdFast"] = MacdFast.ToString(),
            ["macdSlow"] = MacdSlow.ToString(),
            ["macdSignal"] = MacdSignal.ToString(),
            ["minHistogramStrength"] = MinHistogramStrength.ToString("G"),
            ["retestAtrTolerance"] = RetestAtrTolerance.ToString("G"),
            ["maxBarsAfterTrendFlip"] = MaxBarsAfterTrendFlip.ToString(),
            ["requireRetest"] = RequireRetest.ToString(),
            ["allowTrendContinuationEntry"] = AllowTrendContinuationEntry.ToString(),
            ["stopMode"] = StopMode,
            ["stopAtrMultiplier"] = StopAtrMultiplier.ToString("G"),
            ["stopBufferAtrMultiplier"] = StopBufferAtrMultiplier.ToString("G"),
            ["targetMode"] = TargetMode,
            ["fixedRewardRisk"] = FixedRewardRisk.ToString("G"),
            ["target2RewardRisk"] = Target2RewardRisk?.ToString("G") ?? "3.0",
            ["MinStrength"] = MinStrength.ToString("G"),
            ["MinRewardRisk"] = MinRewardRisk.ToString("G")
        };
        return dict;
    }
}
