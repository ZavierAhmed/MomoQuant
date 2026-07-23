using MomoQuant.Application.TradingSystems;

namespace MomoQuant.Application.TradingSystems.Dtos;

/// <summary>
/// Request to run an SK System analysis. This is analysis only — it never places
/// trades, creates backtests, paper sessions, benchmarks, or orders.
/// </summary>
public sealed class SkSystemAnalyzeRequest
{
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string PrimaryTimeframe { get; set; } = "15m";
    public string HigherTimeframe { get; set; } = "4h";
    public int LookbackCandles { get; set; } = 500;
    public string SwingSensitivity { get; set; } = "Balanced";
    public string DirectionMode { get; set; } = "Auto";
    public bool UseAiSummary { get; set; } = true;

    /// <summary>Beginner, Intermediate, or Expert. Controls how technical the summaries are.</summary>
    public string ExplanationMode { get; set; } = "Beginner";

    /// <summary>Alias for <see cref="ExplanationMode"/> (SK-1 request field).</summary>
    public string ExplanationLevel
    {
        get => ExplanationMode;
        set => ExplanationMode = value;
    }

    public IReadOnlyList<string> AdditionalTimeframes { get; set; } = [];

    public string QuickViewMode { get; set; } = "Beginner";

    public bool IncludeAllPossibleSetups { get; set; }
    public bool IncludeFibonacciDetailLevels { get; set; }
    public bool IncludeTargetLevels { get; set; } = true;
    public bool IncludeDangerLevels { get; set; } = true;
    public bool IncludeHigherTimeframeZones { get; set; } = true;
    public bool IncludeLiquidityContext { get; set; } = true;
    public bool IncludeBreakoutRetestContext { get; set; } = true;

    /// <summary>When true, missing candles trigger a public Binance import before analysis.</summary>
    public bool AutoImportMissingCandles { get; set; } = true;
}

/// <summary>Default SK analyzer settings returned by GET /sk/defaults.</summary>
public sealed class SkAnalysisDefaultsDto
{
    public string PrimaryTimeframe { get; init; } = SkSystemConstants.DefaultPrimaryTimeframe;
    public string HigherTimeframe { get; init; } = SkSystemConstants.DefaultHigherTimeframe;
    public int LookbackCandles { get; init; } = SkSystemConstants.DefaultLookbackCandles;
    public string SwingSensitivity { get; init; } = "Balanced";
    public string SequenceDirectionMode { get; init; } = "Auto";
    public string ExplanationLevel { get; init; } = SkSystemConstants.DefaultExplanationMode;
    public string QuickViewMode { get; init; } = SkSystemConstants.DefaultQuickViewMode;
    public bool IncludeAllPossibleSetups { get; init; }
    public bool IncludeFibonacciDetailLevels { get; init; }
    public bool IncludeTargetLevels { get; init; } = true;
    public bool IncludeDangerLevels { get; init; } = true;
    public bool IncludeHigherTimeframeZones { get; init; } = true;
    public bool IncludeLiquidityContext { get; init; } = true;
    public bool IncludeBreakoutRetestContext { get; init; } = true;
    public IReadOnlyList<string> SupportedPrimaryTimeframes { get; init; } = SkSystemConstants.SupportedPrimaryTimeframes;
    public IReadOnlyList<string> SupportedHigherTimeframes { get; init; } = SkSystemConstants.SupportedHigherTimeframes;
    public IReadOnlyList<string> SupportedAnalysisTimeframes { get; init; } = SkSystemConstants.SupportedAnalysisTimeframes;
    public string AnalysisOnlyDisclaimer { get; init; } = SkSystemConstants.AnalysisOnlyDisclaimer;
}

public sealed class SkImportRequiredDataRequest
{
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string PrimaryTimeframe { get; set; } = "15m";
    public string HigherTimeframe { get; set; } = "4h";
    public int LookbackCandles { get; set; } = 300;
}

/// <summary>
/// Request to export a saved SK analysis as a PDF. Analysis only — exporting never
/// creates trades, orders, backtests, or paper sessions.
/// </summary>
public sealed class SkExportPdfRequest
{
    /// <summary>Optional captured chart image, e.g. "data:image/png;base64,...".</summary>
    public string? ChartImageBase64 { get; set; }

    public bool IncludeChart { get; set; } = true;
    public bool IncludeGlossary { get; set; } = true;
    public bool IncludeRawDiagnostics { get; set; }

    /// <summary>Client-side chart render readiness flags for export diagnostics.</summary>
    public bool ChartReady { get; set; }
    public bool OverlaysReady { get; set; }
    public int OverlayCount { get; set; }
    public string? ExportError { get; set; }
}

/// <summary>A generated PDF document ready to stream to the client.</summary>
public sealed class SkPdfDocumentDto
{
    public required byte[] Content { get; init; }
    public required string FileName { get; init; }
}

public sealed class SwingPointDto
{
    public required string Id { get; init; }
    public long CandleId { get; init; }
    public DateTime TimeUtc { get; init; }
    public decimal Price { get; init; }

    /// <summary>High or Low.</summary>
    public required string Type { get; init; }

    /// <summary>Relative strength 0-100.</summary>
    public decimal Strength { get; init; }

    public int LeftBars { get; init; }
    public int RightBars { get; init; }

    /// <summary>Wick or Close.</summary>
    public required string Source { get; init; }
}

public sealed class SkSequencePointDto
{
    /// <summary>Start, ImpulseEnd, Correction, Current, SwingHigh, SwingLow.</summary>
    public required string Label { get; init; }

    public long CandleId { get; init; }
    public DateTime TimeUtc { get; init; }
    public decimal Price { get; init; }
    public int CandleIndex { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>Full SK sequence anatomy for audit and beginner explanations (SK-2).</summary>
public sealed class SkSequenceDto
{
    public required string Id { get; init; }

    /// <summary>Upward or Downward.</summary>
    public required string Direction { get; init; }

    public required string Timeframe { get; init; }
    public required string Symbol { get; init; }

    public SkSequencePointDto? StartPoint { get; init; }
    public SkSequencePointDto? ImpulseEndPoint { get; init; }
    public SkSequencePointDto? CorrectionPoint { get; init; }
    public SkSequencePointDto? CurrentPoint { get; init; }

    public decimal SequenceHigh { get; init; }
    public decimal SequenceLow { get; init; }
    public decimal CorrectionZoneLow { get; init; }
    public decimal CorrectionZoneHigh { get; init; }
    public decimal StrongCorrectionZoneLow { get; init; }
    public decimal StrongCorrectionZoneHigh { get; init; }
    public decimal InvalidationLevel { get; init; }
    public decimal Target1 { get; init; }
    public decimal Target2 { get; init; }
    public decimal ExtensionTarget { get; init; }

    public string SequenceStatus { get; init; } = "Building";
    public string ValidityStatus { get; init; } = "Valid";
    public decimal ClarityScore { get; init; }
    public string ClarityLabel { get; init; } = "Low";
    public decimal UsefulnessScore { get; init; }
    public string UsefulnessStatus { get; init; } = "Fresh";
    public bool SelectedAsBest { get; init; }
    public string ReasonSelected { get; init; } = string.Empty;
    public string InvalidationReason { get; init; } = string.Empty;
    public string StructureCategory { get; init; } = "Alternative only";
    public bool HiddenFromBeginner { get; init; }
    public string BeginnerExplanation { get; init; } = string.Empty;
    public string AdvancedExplanation { get; init; } = string.Empty;
    public IReadOnlyList<string> WarningMessages { get; init; } = [];
    public string CalculationNotes { get; init; } = string.Empty;
    public string ValidationStatus { get; init; } = "Valid";
    public string ValidationMessage { get; init; } = string.Empty;
    public bool EligibleForBestIdea { get; init; } = true;
}

/// <summary>Internal audit panel summarizing SK concept validation (SK-2).</summary>
public sealed class SkConceptAuditDto
{
    public string HtfDirection { get; init; } = "Unknown";
    public string LtfDirection { get; init; } = "Unknown";
    public bool HtfLtfAgreement { get; init; }
    public string SelectedSequenceDirection { get; init; } = "Unknown";
    public string SequenceStatus { get; init; } = "Building";
    public string ValidityStatus { get; init; } = "Valid";
    public string UsefulnessStatus { get; init; } = "Fresh";
    public decimal ClarityScore { get; init; }
    public string ClarityLabel { get; init; } = "Low";
    public decimal UsefulnessScore { get; init; }
    public string ReasonSelected { get; init; } = string.Empty;
    public IReadOnlyList<string> SequencePoints { get; init; } = [];
    public string ReactionZoneText { get; init; } = string.Empty;
    public string StrongReactionZoneText { get; init; } = string.Empty;
    public string InvalidationLevelText { get; init; } = string.Empty;
    public string TargetValidation { get; init; } = string.Empty;
    public string AlreadyReachedCheck { get; init; } = string.Empty;
    public string InvalidationCheck { get; init; } = string.Empty;
    public int HiddenStructuresCount { get; init; }
    public int DirectionMismatchStructuresCount { get; init; }
    public string? PrimaryUpwardId { get; init; }
    public string? PrimaryDownwardId { get; init; }
    public IReadOnlyList<string> HiddenStructureIds { get; init; } = [];
    public IReadOnlyList<string> InvalidStructureIds { get; init; } = [];
}

public sealed class SkExportStatusDto
{
    public bool ChartIncluded { get; init; }
    public string? ChartUnavailableReason { get; init; }
    public DateTime? ExportStartedAtUtc { get; init; }
    public DateTime? ExportCompletedAtUtc { get; init; }
    public int OverlayCount { get; init; }
}

public sealed class SkSequenceCandidateDto
{
    public required string Id { get; init; }

    /// <summary>Bullish or Bearish.</summary>
    public required string Direction { get; init; }

    /// <summary>Potential, Active, Invalidated, Completed.</summary>
    public required string Status { get; init; }

    public SkSequencePointDto? PointZ { get; init; }
    public SkSequencePointDto? PointA { get; init; }
    public SkSequencePointDto? PointB { get; init; }
    public SkSequencePointDto? PointC { get; init; }

    public DateTime ImpulseStartTimeUtc { get; init; }
    public DateTime ImpulseEndTimeUtc { get; init; }

    public decimal CorrectionZoneMin { get; init; }
    public decimal CorrectionZoneMax { get; init; }
    public decimal GoldenPocketMin { get; init; }
    public decimal GoldenPocketMax { get; init; }

    public decimal Target1 { get; init; }
    public decimal Target2 { get; init; }
    public decimal Extension1618 { get; init; }
    public decimal InvalidationLevel { get; init; }

    /// <summary>
    /// BeforeCorrectionZone, InsideCorrectionZone, LeftCorrectionZone, NearTarget, Invalidated.
    /// </summary>
    public required string CurrentPricePosition { get; init; }

    /// <summary>0-100 heuristic confidence in the structure (not trade confidence).</summary>
    public decimal ConfidenceScore { get; init; }

    public string Notes { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Valid, DirectionMismatch, AlreadyReached, StructureInvalidated, LowClarity, MissingData.
    /// </summary>
    public string ValidationStatus { get; init; } = "Valid";

    public string ValidationMessage { get; init; } = string.Empty;

    /// <summary>False when directionally inconsistent or invalidated — hidden from best-idea selection.</summary>
    public bool EligibleForBestIdea { get; init; } = true;
}

public sealed class SkFibonacciZoneDto
{
    public required string SequenceId { get; init; }

    /// <summary>Retracement or Extension.</summary>
    public required string Kind { get; init; }

    public decimal Ratio { get; init; }
    public decimal Price { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsGoldenPocket { get; init; }
}

public sealed class SkKeyLevelDto
{
    public required string Label { get; init; }
    public decimal Price { get; init; }

    /// <summary>Support, Resistance, Invalidation, Target, SwingHigh, SwingLow.</summary>
    public required string Kind { get; init; }
    public string? SequenceId { get; init; }
}

public sealed class SkMultiTimeframeContextDto
{
    /// <summary>Bullish, Bearish, Neutral, Mixed.</summary>
    public required string HigherTimeframeBias { get; init; }
    public string HigherTimeframeTrendDescription { get; init; } = string.Empty;
    public IReadOnlyList<SkKeyLevelDto> ImportantHigherTimeframeLevels { get; init; } = [];
    public string? ConflictWarning { get; init; }
}

public sealed class ChartOverlayDto
{
    /// <summary>
    /// HorizontalLine, Zone, FibonacciRetracement, FibonacciExtension, Marker, ScenarioArrow, Label.
    /// </summary>
    public required string Type { get; init; }

    public string Label { get; init; } = string.Empty;
    public string Color { get; init; } = "#38bdf8";

    /// <summary>Single price for HorizontalLine / Marker / Label.</summary>
    public decimal? Price { get; init; }

    /// <summary>Zone bounds for Zone / Fibonacci overlays.</summary>
    public decimal? PriceLow { get; init; }
    public decimal? PriceHigh { get; init; }

    public DateTime? TimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }

    /// <summary>Bullish, Bearish, Neutral where relevant.</summary>
    public string? Direction { get; init; }

    /// <summary>
    /// Grouping used by the chart's overlay toggles:
    /// SwingPoint, ReactionZone, StrongReactionZone, Danger, Target, Scenario,
    /// Fibonacci, HigherTimeframe, SetupPoint, Current.
    /// </summary>
    public string Category { get; init; } = "Other";

    /// <summary>Short, clutter-free label such as "Upward danger".</summary>
    public string ShortLabel { get; init; } = string.Empty;

    public string? SequenceId { get; init; }
    public bool IsBestBullish { get; init; }
    public bool IsBestBearish { get; init; }
    public decimal? Ratio { get; init; }

    // ----- Grouping and level metadata (Milestone 19.8.3) -----

    /// <summary>
    /// Beginner-friendly group: "Best upward idea", "Best downward idea",
    /// "Other possible structures", "Higher timeframe context", "Swing points",
    /// "Fibonacci detail levels", "Current price".
    /// </summary>
    public string GroupName { get; init; } = "Other";

    /// <summary>Sequence id this overlay belongs to (same as <see cref="SequenceId"/> when applicable).</summary>
    public string? SetupId { get; init; }

    /// <summary>0 for the best setup / current price, 1..n for other structures.</summary>
    public int SetupRank { get; init; }

    /// <summary>Bullish, Bearish, or null.</summary>
    public string? SetupDirection { get; init; }

    /// <summary>
    /// Machine-friendly level type: CurrentPrice, ReactionZone, StrongReactionZone,
    /// DangerLevel, Target1, Target2, FibonacciRetracement, FibonacciExtension,
    /// HigherTimeframeLevel, SwingHigh, SwingLow, SequenceStart, FirstStrongMove,
    /// PullbackPoint, TargetArea, ScenarioArrow.
    /// </summary>
    public string LevelType { get; init; } = "Other";

    /// <summary>Human-friendly name, e.g. "Upward danger level".</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Plain-language explanation of what this level means.</summary>
    public string PlainLanguageMeaning { get; init; } = string.Empty;

    public string TooltipTitle { get; init; } = string.Empty;
    public string TooltipBody { get; init; } = string.Empty;

    /// <summary>Whether this overlay is shown in the default beginner chart view.</summary>
    public bool VisibleByDefault { get; init; }

    /// <summary>High, Medium, or Low. Drives chart line weight / visual hierarchy.</summary>
    public string Importance { get; init; } = "Low";

    /// <summary>Advanced/detail level (Fibonacci, swings, higher timeframe, other structures).</summary>
    public bool IsAdvanced { get; init; }

    /// <summary>Part of the best upward/downward idea (or the current price).</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Stable overlay key for legend toggles (SK-2).</summary>
    public string LayerKey { get; init; } = string.Empty;

    /// <summary>Beginner, Intermediate, or Advanced visibility tier.</summary>
    public string VisibilityTier { get; init; } = "Beginner";

    public string BeginnerLabel { get; init; } = string.Empty;
    public string AdvancedLabel { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>A single plain-language term explanation for the glossary section.</summary>
public sealed class SkGlossaryTermDto
{
    public required string Term { get; init; }
    public required string Explanation { get; init; }
}

/// <summary>
/// A ranked, beginner-friendly view of the best bullish or bearish structure. All
/// price fields are also provided pre-formatted for direct display.
/// </summary>
public sealed class SkIdeaDto
{
    /// <summary>Internal direction (Bullish/Bearish).</summary>
    public required string Direction { get; init; }

    /// <summary>Friendly label such as "Possible upward move".</summary>
    public required string DirectionLabel { get; init; }

    /// <summary>Internal status (Potential/Active/Invalidated/Completed).</summary>
    public required string Status { get; init; }
    public required string StatusLabel { get; init; }

    /// <summary>Low, Medium, High.</summary>
    public required string ClarityLabel { get; init; }
    public decimal ClarityScore { get; init; }

    public decimal ReactionZoneMin { get; init; }
    public decimal ReactionZoneMax { get; init; }
    public string ReactionZoneText { get; init; } = string.Empty;

    public decimal StrongReactionZoneMin { get; init; }
    public decimal StrongReactionZoneMax { get; init; }
    public string StrongReactionZoneText { get; init; } = string.Empty;

    public decimal DangerLevel { get; init; }
    public string DangerLevelText { get; init; } = string.Empty;

    public decimal Target1 { get; init; }
    public decimal Target2 { get; init; }
    public string TargetsText { get; init; } = string.Empty;

    public string CurrentPricePositionLabel { get; init; } = string.Empty;
    public string WhyItMatters { get; init; } = string.Empty;
    public string PlainExplanation { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public string ValidationStatus { get; init; } = "Valid";
    public string ValidationMessage { get; init; } = string.Empty;
}

/// <summary>HTF or LTF context block for beginner-friendly separation in UI and PDF.</summary>
public sealed class SkTimeframeContextDto
{
    public required string Timeframe { get; init; }
    public required string Role { get; init; }
    public string Direction { get; init; } = "Unknown";
    public string Summary { get; init; } = string.Empty;
    public string ReactionZoneText { get; init; } = string.Empty;
    public string DangerLevelText { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public string ClarityLabel { get; init; } = "Low";
    public bool AgreesWithPrimary { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Beginner narrative built by the orchestrator and persisted so saved analyses can be
/// re-displayed without recomputation.
/// </summary>
public sealed class SkNarrativeDto
{
    public string ExplanationMode { get; init; } = "Beginner";
    public int PriceDecimals { get; init; } = 2;
    public string BottomLine { get; init; } = string.Empty;
    public string KeyAreaToWatch { get; init; } = string.Empty;
    public string DangerLevelToWatch { get; init; } = string.Empty;
    public SkIdeaDto? BestBullishIdea { get; init; }
    public SkIdeaDto? BestBearishIdea { get; init; }
    public IReadOnlyList<string> ClarityReasons { get; init; } = [];
    public IReadOnlyList<string> ClarityWarnings { get; init; } = [];
}

public sealed class SkAiSummaryDto
{
    public string Summary { get; init; } = string.Empty;
    public string BullishScenario { get; init; } = string.Empty;
    public string BearishScenario { get; init; } = string.Empty;
    public string InvalidationExplanation { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Simple explanation with no unexplained jargon (used in Beginner mode).</summary>
    public string PlainLanguageSummary { get; init; } = string.Empty;

    /// <summary>One-paragraph "What this means" explanation.</summary>
    public string WhatThisMeans { get; init; } = string.Empty;

    public string WhatWouldMakeWrong { get; init; } = string.Empty;
    public string WhatToWatchNext { get; init; } = string.Empty;
    public string WhyNotTradeSignal { get; init; } = string.Empty;
    public string UsefulnessExplanation { get; init; } = string.Empty;
    public string AlternativeStructuresNote { get; init; } = string.Empty;

    /// <summary>Short conclusion shown in the "Bottom line" box.</summary>
    public string BottomLine { get; init; } = string.Empty;

    /// <summary>Plain-English higher timeframe explanation.</summary>
    public string HigherTimeframeExplanation { get; init; } = string.Empty;

    /// <summary>Plain-English explanation shown when primary/higher timeframes disagree.</summary>
    public string ConflictExplanation { get; init; } = string.Empty;

    /// <summary>Low, Medium, High.</summary>
    public string ConfidenceLabel { get; init; } = "Low";

    public bool AnalysisOnly { get; init; } = true;
    public bool UsedFallback { get; init; } = true;
    public string Source { get; init; } = "RuleBased";
}

/// <summary>
/// Full result of an SK System analysis. Analysis only — never used for execution.
/// </summary>
public sealed record SkSystemAnalysisResultDto
{
    public long AnalysisId { get; init; }
    public string SystemCode { get; init; } = "SK_SYSTEM";
    public string SystemName { get; init; } = "SK System Analyzer";

    public long ExchangeId { get; init; }
    public string ExchangeName { get; init; } = string.Empty;
    public long SymbolId { get; init; }
    public string Symbol { get; init; } = string.Empty;

    public string PrimaryTimeframe { get; init; } = string.Empty;
    public string HigherTimeframe { get; init; } = string.Empty;
    public int LookbackCandles { get; init; }
    public string SwingSensitivity { get; init; } = "Balanced";
    public string DirectionMode { get; init; } = "Auto";

    public string Status { get; init; } = "Completed";
    public DateTime AnalysisTimeUtc { get; init; }
    public DateTime? LatestCandleTimeUtc { get; init; }
    public decimal CurrentPrice { get; init; }

    /// <summary>Bullish, Bearish, Neutral, Mixed, Unknown.</summary>
    public string MarketBias { get; init; } = "Unknown";

    /// <summary>Low, Medium, High.</summary>
    public string ConfidenceLabel { get; init; } = "Low";

    public string Summary { get; init; } = string.Empty;
    public string BullishScenario { get; init; } = string.Empty;
    public string BearishScenario { get; init; } = string.Empty;
    public IReadOnlyList<string> InvalidationLevels { get; init; } = [];

    // ----- Beginner-friendly narrative (Milestone 19.8.1) -----

    /// <summary>Beginner, Intermediate, Expert.</summary>
    public string ExplanationMode { get; init; } = "Beginner";

    /// <summary>Number of decimals the UI should use for this symbol's prices.</summary>
    public int PriceDecimals { get; init; } = 2;

    public string PlainLanguageSummary { get; init; } = string.Empty;
    public string BottomLine { get; init; } = string.Empty;
    public string WhatThisMeans { get; init; } = string.Empty;
    public string KeyAreaToWatch { get; init; } = string.Empty;
    public string DangerLevelToWatch { get; init; } = string.Empty;
    public string HigherTimeframeExplanation { get; init; } = string.Empty;
    public string ConflictExplanation { get; init; } = string.Empty;

    public SkIdeaDto? BestBullishIdea { get; init; }
    public SkIdeaDto? BestBearishIdea { get; init; }

    public IReadOnlyList<string> ClarityReasons { get; init; } = [];
    public IReadOnlyList<string> ClarityWarnings { get; init; } = [];

    public IReadOnlyList<SkGlossaryTermDto> GlossaryTerms { get; init; } = [];
    public IReadOnlyDictionary<string, string> DisplayLabels { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<SwingPointDto> SwingPoints { get; init; } = [];
    public IReadOnlyList<SkSequenceCandidateDto> SequenceCandidates { get; init; } = [];
    public IReadOnlyList<SkFibonacciZoneDto> FibonacciZones { get; init; } = [];
    public IReadOnlyList<SkKeyLevelDto> KeyLevels { get; init; } = [];
    public IReadOnlyList<ChartOverlayDto> ChartOverlays { get; init; } = [];

    public SkMultiTimeframeContextDto? HigherTimeframeContext { get; init; }
    public SkTimeframeContextDto? HtfContext { get; init; }
    public SkTimeframeContextDto? LtfContext { get; init; }
    public SkAiSummaryDto? AiSummary { get; init; }

    public IReadOnlyList<SkSequenceDto> Sequences { get; init; } = [];
    public SkConceptAuditDto? ConceptAudit { get; init; }
    public SkExportStatusDto? LastExportStatus { get; init; }

    public string AnalysisOnlyDisclaimer { get; init; } = SkSystemConstants.AnalysisOnlyDisclaimer;
    public string QuickViewMode { get; init; } = SkSystemConstants.DefaultQuickViewMode;

    public IReadOnlyList<SkChartCandleDto> Candles { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public SkAnalysisDiagnosticsDto Diagnostics { get; init; } = new();

    /// <summary>Always true. Trading Systems never execute trades.</summary>
    public bool AnalysisOnly { get; init; } = true;
}

public sealed class SkChartCandleDto
{
    public DateTime TimeUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

public sealed class SkAnalysisDiagnosticsDto
{
    public int PrimaryCandleCount { get; init; }
    public int HigherCandleCount { get; init; }
    public int SwingHighCount { get; init; }
    public int SwingLowCount { get; init; }
    public int SequenceCandidateCount { get; init; }
    public string ResolvedSensitivity { get; init; } = "Balanced";
    public decimal MinSwingDistancePercent { get; init; }
    public int MinSwingCandles { get; init; }
    public IReadOnlyList<decimal> FibonacciCorrectionLevels { get; init; } = [];
    public IReadOnlyList<decimal> FibonacciExtensionLevels { get; init; } = [];
    public string Note { get; init; } =
        "SK System is an approximate, configurable analyzer for research only. Not certified. Not a trade signal.";
}

/// <summary>Compact row for the saved analyses table.</summary>
public sealed class TradingSystemAnalysisSummaryDto
{
    public long Id { get; init; }
    public string SystemCode { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string PrimaryTimeframe { get; init; } = string.Empty;
    public string HigherTimeframe { get; init; } = string.Empty;
    public string MarketBias { get; init; } = "Unknown";
    public string ConfidenceLabel { get; init; } = "Low";
    public string Status { get; init; } = "Completed";

    /// <summary>Short plain-language conclusion (the "Bottom line") for the saved list.</summary>
    public string Conclusion { get; init; } = string.Empty;

    public string ClarityLabel { get; init; } = "Low";
    public string UsefulnessStatus { get; init; } = "Fresh";
    public string SequenceStatus { get; init; } = "Building";
    public string ValidityStatus { get; init; } = "Valid";
    public string ChartExportStatus { get; init; } = "Not exported";

    public DateTime AnalysisTimeUtc { get; init; }
    public DateTime? LatestCandleTimeUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
