namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Maps internal SK terminology to beginner-friendly UI labels. Internal names remain
/// unchanged in code and diagnostics; these labels are only for display.
/// </summary>
public static class SkDisplayLabels
{
    public static IReadOnlyDictionary<string, string> Map { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SequenceCandidate"] = "Possible setup",
        ["Bullish"] = "Possible upward move",
        ["Bearish"] = "Possible downward move",
        ["CorrectionZone"] = "Reaction zone",
        ["GoldenPocket"] = "Strong reaction zone",
        ["Invalidation"] = "Danger level",
        ["Target1"] = "First possible target",
        ["Target2"] = "Second possible target",
        ["Extension1618"] = "Extended target area",
        ["PointZ"] = "Starting point",
        ["PointA"] = "First strong move",
        ["PointB"] = "Pullback point",
        ["PointC"] = "Target area",
        ["BtoC"] = "Move from pullback to target",
        ["BtoZ"] = "Move back toward starting point",
        ["Confidence"] = "Clarity score",
        ["Bias"] = "Market direction",
        ["Potential"] = "Still forming",
        ["Active"] = "Inside reaction zone",
        ["Completed"] = "Reached target area",
        ["Invalidated"] = "No longer valid",
        ["NearTarget"] = "Close to target",
        ["BeforeCorrectionZone"] = "Not yet in reaction zone",
        ["InsideCorrectionZone"] = "Inside reaction zone",
        ["LeftCorrectionZone"] = "Moved away from reaction zone",
        ["DirectionMismatch"] = "Direction mismatch",
        ["AlreadyReached"] = "Already reached",
        ["StructureInvalidated"] = "Structure invalidated",
        ["LowClarity"] = "Low clarity",
        ["MissingData"] = "Missing data",
        ["Valid"] = "Valid setup"
    };

    public static string Direction(string? direction) => direction switch
    {
        "Bullish" => "Possible upward move",
        "Bearish" => "Possible downward move",
        _ => "Possible setup"
    };

    public static string Status(string? status) =>
        status is not null && Map.TryGetValue(status, out var value) ? value : status ?? string.Empty;

    public static string Position(string? position) =>
        position is not null && Map.TryGetValue(position, out var value) ? value : position ?? string.Empty;
}
