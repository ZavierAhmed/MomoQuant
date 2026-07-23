using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public sealed class BbLiquiditySweepParameters
{
    public BbStrategyStrictnessProfile StrictnessProfile { get; init; } = BbStrategyStrictnessProfile.BalancedResearch;
    public int BbPeriod { get; init; } = 20;
    public decimal BbStdDev { get; init; } = 2.0m;
    public bool UseSessionFilter { get; init; } = true;
    public int StopAfterLossesPerSession { get; init; } = 2;
    public bool RequireSweepOutsideBb { get; init; } = true;
    public bool RequireCloseBackInsideBb { get; init; }
    public bool RequireCloseBackAcrossLiquidityLine { get; init; }
    public int CisdLookbackCandles { get; init; } = 5;
    public int MaxBarsAfterSweep { get; init; } = 5;
    public decimal MinRewardRisk { get; init; } = 2.5m;
    public decimal ResearchMinRewardRisk3R { get; init; } = 3m;
    public decimal StopLossAtrBufferMultiplier { get; init; } = 0.05m;
    public decimal DisplacementAtrMultiplier { get; init; } = 0.3m;
    public bool AllowTradeCreation { get; init; } = true;
    public bool UseRsiPrimedFilter { get; init; }
    public bool RequireRsiPrimedFilter { get; init; }
    public decimal RsiOversoldLevel { get; init; } = 30m;
    public decimal RsiOverboughtLevel { get; init; } = 70m;
    public RsiPrimedSignalValueMode RsiPrimedSignalValueMode { get; init; } = RsiPrimedSignalValueMode.HaClose;
    public int RsiLength { get; init; } = 24;
    public int RsiSmoothing { get; init; } = 3;
    public bool RsiUseHeikinAshi { get; init; } = true;
    public decimal MinStrength { get; init; } = 55m;
    public IReadOnlyList<string> AllowedSessions { get; init; } = [];

    public int SwingLeft { get; init; } = 2;
    public int SwingRight { get; init; } = 2;
    public decimal EqualHighLowToleranceAtrMultiplier { get; init; } = 0.25m;
    public decimal EqualHighLowTolerancePercent { get; init; }
    public int MinTouches { get; init; } = 1;
    public int MaxLevelAgeCandles { get; init; } = 300;
    public decimal LevelMergeToleranceAtrMultiplier { get; init; } = 0.15m;
    public bool RequireEqualHighLow { get; init; }
    public bool IncludeSingleSwingLevels { get; init; } = true;
    public bool UseWicksForLevels { get; init; } = true;
    public bool UseBodiesForLevels { get; init; }
    public decimal MaxDistanceFromLiquidityAtrMultiplier { get; init; } = 0.35m;
    public bool AllowSweepOfAnyRecentSwing { get; init; } = true;
    public decimal CloseBackToleranceTicks { get; init; }
    public int MaxSampleEvaluations { get; init; } = 500;

    public static BbLiquiditySweepParameters From(IReadOnlyDictionary<string, string> parameters, bool useRsiFilter)
    {
        var profile = ParseStrictnessProfile(StrategyParameterReader.GetString(parameters, "BbStrategyStrictnessProfile", "BalancedResearch"));
        var resolved = ApplyStrictnessProfile(profile);

        return new BbLiquiditySweepParameters
        {
            StrictnessProfile = profile,
            BbPeriod = StrategyParameterReader.GetInt(parameters, "BbPeriod", resolved.BbPeriod),
            BbStdDev = StrategyParameterReader.GetDecimal(parameters, "BbStdDev", resolved.BbStdDev),
            UseSessionFilter = StrategyParameterReader.GetBool(parameters, "UseSessionFilter", resolved.UseSessionFilter),
            StopAfterLossesPerSession = StrategyParameterReader.GetInt(parameters, "StopAfterLossesPerSession", resolved.StopAfterLossesPerSession),
            RequireSweepOutsideBb = StrategyParameterReader.GetBool(parameters, "RequireSweepOutsideBb", resolved.RequireSweepOutsideBb),
            RequireCloseBackInsideBb = StrategyParameterReader.GetBool(parameters, "RequireCloseBackInsideBb", resolved.RequireCloseBackInsideBb),
            RequireCloseBackAcrossLiquidityLine = StrategyParameterReader.GetBool(parameters, "RequireCloseBackAcrossLiquidityLine", resolved.RequireCloseBackAcrossLiquidityLine),
            CisdLookbackCandles = StrategyParameterReader.GetInt(parameters, "CisdLookbackCandles", resolved.CisdLookbackCandles),
            MaxBarsAfterSweep = StrategyParameterReader.GetInt(parameters, "MaxBarsAfterSweep", resolved.MaxBarsAfterSweep),
            MinRewardRisk = StrategyParameterReader.GetDecimal(parameters, "MinRewardRisk", resolved.MinRewardRisk),
            ResearchMinRewardRisk3R = StrategyParameterReader.GetDecimal(parameters, "ResearchMinRewardRisk3R", resolved.ResearchMinRewardRisk3R),
            StopLossAtrBufferMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossAtrBufferMultiplier", resolved.StopLossAtrBufferMultiplier),
            DisplacementAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "DisplacementAtrMultiplier", resolved.DisplacementAtrMultiplier),
            AllowTradeCreation = resolved.AllowTradeCreation,
            UseRsiPrimedFilter = useRsiFilter,
            RequireRsiPrimedFilter = useRsiFilter && StrategyParameterReader.GetBool(parameters, "RequireRsiPrimedFilter", resolved.RequireRsiPrimedFilter),
            RsiOversoldLevel = StrategyParameterReader.GetDecimal(parameters, "RsiOversoldLevel", resolved.RsiOversoldLevel),
            RsiOverboughtLevel = StrategyParameterReader.GetDecimal(parameters, "RsiOverboughtLevel", resolved.RsiOverboughtLevel),
            RsiPrimedSignalValueMode = ParseSignalMode(StrategyParameterReader.GetString(parameters, "RsiPrimedSignalValueMode", "HaClose")),
            RsiLength = StrategyParameterReader.GetInt(parameters, "RsiLength", resolved.RsiLength),
            RsiSmoothing = StrategyParameterReader.GetInt(parameters, "RsiSmoothing", resolved.RsiSmoothing),
            RsiUseHeikinAshi = StrategyParameterReader.GetBool(parameters, "RsiUseHeikinAshi", resolved.RsiUseHeikinAshi),
            MinStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", resolved.MinStrength),
            AllowedSessions = ParseSessions(StrategyParameterReader.GetString(parameters, "AllowedSessions", string.Empty)),
            SwingLeft = StrategyParameterReader.GetInt(parameters, "SwingLeft", resolved.SwingLeft),
            SwingRight = StrategyParameterReader.GetInt(parameters, "SwingRight", resolved.SwingRight),
            EqualHighLowToleranceAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "EqualHighLowToleranceAtrMultiplier", resolved.EqualHighLowToleranceAtrMultiplier),
            EqualHighLowTolerancePercent = StrategyParameterReader.GetDecimal(parameters, "EqualHighLowTolerancePercent", resolved.EqualHighLowTolerancePercent),
            MinTouches = StrategyParameterReader.GetInt(parameters, "MinTouches", resolved.MinTouches),
            MaxLevelAgeCandles = StrategyParameterReader.GetInt(parameters, "MaxLevelAgeCandles", resolved.MaxLevelAgeCandles),
            LevelMergeToleranceAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "LevelMergeToleranceAtrMultiplier", resolved.LevelMergeToleranceAtrMultiplier),
            RequireEqualHighLow = StrategyParameterReader.GetBool(parameters, "RequireEqualHighLow", resolved.RequireEqualHighLow),
            IncludeSingleSwingLevels = StrategyParameterReader.GetBool(parameters, "IncludeSingleSwingLevels", resolved.IncludeSingleSwingLevels),
            UseWicksForLevels = StrategyParameterReader.GetBool(parameters, "UseWicksForLevels", resolved.UseWicksForLevels),
            UseBodiesForLevels = StrategyParameterReader.GetBool(parameters, "UseBodiesForLevels", resolved.UseBodiesForLevels),
            MaxDistanceFromLiquidityAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "MaxDistanceFromLiquidityAtrMultiplier", resolved.MaxDistanceFromLiquidityAtrMultiplier),
            AllowSweepOfAnyRecentSwing = StrategyParameterReader.GetBool(parameters, "AllowSweepOfAnyRecentSwing", resolved.AllowSweepOfAnyRecentSwing),
            CloseBackToleranceTicks = StrategyParameterReader.GetDecimal(parameters, "CloseBackToleranceTicks", resolved.CloseBackToleranceTicks),
            MaxSampleEvaluations = StrategyParameterReader.GetInt(parameters, "MaxSampleEvaluations", resolved.MaxSampleEvaluations)
        };
    }

    public static BbLiquiditySweepParameters ApplyStrictnessProfile(BbStrategyStrictnessProfile profile) => profile switch
    {
        BbStrategyStrictnessProfile.OriginalStrict => new BbLiquiditySweepParameters
        {
            StrictnessProfile = profile,
            UseSessionFilter = true,
            RequireSweepOutsideBb = true,
            RequireCloseBackInsideBb = true,
            RequireCloseBackAcrossLiquidityLine = true,
            MinRewardRisk = 3m,
            ResearchMinRewardRisk3R = 3m,
            AllowTradeCreation = true,
            RequireRsiPrimedFilter = true,
            SwingLeft = 2,
            SwingRight = 2,
            EqualHighLowToleranceAtrMultiplier = 0.1m,
            MinTouches = 1,
            IncludeSingleSwingLevels = false,
            MaxLevelAgeCandles = 200,
            LevelMergeToleranceAtrMultiplier = 0.1m,
            MaxDistanceFromLiquidityAtrMultiplier = 0.25m
        },
        BbStrategyStrictnessProfile.DetectorCalibration => new BbLiquiditySweepParameters
        {
            StrictnessProfile = profile,
            UseSessionFilter = false,
            RequireSweepOutsideBb = false,
            RequireCloseBackInsideBb = false,
            RequireCloseBackAcrossLiquidityLine = false,
            MinRewardRisk = 0m,
            ResearchMinRewardRisk3R = 3m,
            AllowTradeCreation = false,
            RequireRsiPrimedFilter = false,
            SwingLeft = 2,
            SwingRight = 2,
            EqualHighLowToleranceAtrMultiplier = 0.25m,
            MinTouches = 1,
            IncludeSingleSwingLevels = true,
            MaxLevelAgeCandles = 300,
            LevelMergeToleranceAtrMultiplier = 0.15m,
            MaxDistanceFromLiquidityAtrMultiplier = 0.5m,
            AllowSweepOfAnyRecentSwing = true
        },
        _ => new BbLiquiditySweepParameters
        {
            StrictnessProfile = BbStrategyStrictnessProfile.BalancedResearch,
            UseSessionFilter = true,
            RequireSweepOutsideBb = true,
            RequireCloseBackInsideBb = false,
            RequireCloseBackAcrossLiquidityLine = false,
            MinRewardRisk = 2.5m,
            ResearchMinRewardRisk3R = 3m,
            AllowTradeCreation = true,
            RequireRsiPrimedFilter = false,
            SwingLeft = 2,
            SwingRight = 2,
            EqualHighLowToleranceAtrMultiplier = 0.25m,
            MinTouches = 1,
            IncludeSingleSwingLevels = true,
            MaxLevelAgeCandles = 300,
            LevelMergeToleranceAtrMultiplier = 0.15m,
            MaxDistanceFromLiquidityAtrMultiplier = 0.35m
        }
    };

    private static BbStrategyStrictnessProfile ParseStrictnessProfile(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "originalstrict" or "original_strict" or "1" => BbStrategyStrictnessProfile.OriginalStrict,
            "detectorcalibration" or "detector_calibration" or "3" => BbStrategyStrictnessProfile.DetectorCalibration,
            _ => BbStrategyStrictnessProfile.BalancedResearch
        };

    private static RsiPrimedSignalValueMode ParseSignalMode(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "halowhigh" => RsiPrimedSignalValueMode.HaLowHigh,
            "ohlc4" => RsiPrimedSignalValueMode.Ohlc4,
            _ => RsiPrimedSignalValueMode.HaClose
        };

    private static IReadOnlyList<string> ParseSessions(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
