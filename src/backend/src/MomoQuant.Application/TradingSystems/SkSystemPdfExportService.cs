using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MomoQuant.Application.Common;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Generates a beginner-friendly, analysis-only PDF report for a saved SK analysis.
/// Reads the stored analysis via <see cref="ISkSystemAnalysisService"/> and never touches
/// any execution, benchmark, order, or trade path.
/// </summary>
public sealed class SkSystemPdfExportService : ISkSystemPdfExportService
{
    private const string Disclaimer =
        "This report is chart analysis only. It is not financial advice, not a trade signal, " +
        "and no automated trading action was created.";

    private readonly ISkSystemAnalysisService _analysisService;
    private readonly ILogger<SkSystemPdfExportService> _logger;

    static SkSystemPdfExportService()
    {
        // QuestPDF Community license (free for individuals and small companies).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public SkSystemPdfExportService(
        ISkSystemAnalysisService analysisService,
        ILogger<SkSystemPdfExportService> logger)
    {
        _analysisService = analysisService;
        _logger = logger;
    }

    public async Task<ServiceResult<SkPdfDocumentDto>> ExportAsync(
        long analysisId,
        SkExportPdfRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new SkExportPdfRequest();
        var exportStartedAt = DateTime.UtcNow;

        var analysis = await _analysisService.GetAnalysisAsync(analysisId, cancellationToken);
        if (!analysis.Succeeded || analysis.Data is null)
        {
            return ServiceResult<SkPdfDocumentDto>.Fail(
                analysis.ErrorMessage ?? "Analysis was not found.",
                analysis.ErrorField ?? "id");
        }

        var result = analysis.Data;
        var (imageBytes, chartReason) = DecodeChartImage(request);

        _logger.LogInformation(
            "SK PDF export analysisId={AnalysisId} symbol={Symbol} primaryTimeframe={PrimaryTimeframe} " +
            "higherTimeframe={HigherTimeframe} chartReady={ChartReady} overlaysReady={OverlaysReady} " +
            "overlayCount={OverlayCount} exportStartedAt={ExportStartedAt} exportError={ExportError}",
            analysisId,
            result.Symbol,
            result.PrimaryTimeframe,
            result.HigherTimeframe,
            request.ChartReady,
            request.OverlaysReady,
            request.OverlayCount > 0 ? request.OverlayCount : result.ChartOverlays.Count,
            exportStartedAt,
            chartReason ?? request.ExportError);

        var pdf = BuildPdf(result, request, imageBytes, chartReason);
        var fileName = BuildFileName(result);
        var exportCompletedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "SK PDF export completed analysisId={AnalysisId} exportCompletedAt={ExportCompletedAt}",
            analysisId,
            exportCompletedAt);

        return ServiceResult<SkPdfDocumentDto>.Ok(new SkPdfDocumentDto
        {
            Content = pdf,
            FileName = fileName
        });
    }

    private static (byte[]? Bytes, string? ErrorReason) DecodeChartImage(SkExportPdfRequest request)
    {
        if (!request.IncludeChart)
        {
            return (null, "Chart excluded by export options.");
        }

        if (string.IsNullOrWhiteSpace(request.ChartImageBase64))
        {
            return (null, "No chart image was supplied by the client.");
        }

        try
        {
            var raw = request.ChartImageBase64.Trim();
            var commaIndex = raw.IndexOf(',');
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            {
                raw = raw[(commaIndex + 1)..];
            }

            var bytes = Convert.FromBase64String(raw);
            return bytes.Length == 0
                ? (null, "Chart image payload was empty after decoding.")
                : (bytes, null);
        }
        catch (FormatException)
        {
            return (null, "Chart image base64 payload was invalid.");
        }
    }

    private static string BuildFileName(SkSystemAnalysisResultDto result)
    {
        var date = result.AnalysisTimeUtc == default ? DateTime.UtcNow : result.AnalysisTimeUtc;
        return $"SK-System-{Sanitize(result.Symbol)}-{Sanitize(result.PrimaryTimeframe)}-{Sanitize(result.HigherTimeframe)}-{date:yyyy-MM-dd}.pdf";
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NA";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Where(c => !invalid.Contains(c) && c != ' ').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "NA" : cleaned;
    }

    private static byte[] BuildPdf(
        SkSystemAnalysisResultDto result,
        SkExportPdfRequest request,
        byte[]? imageBytes,
        string? chartUnavailableReason)
    {
        var decimals = result.PriceDecimals <= 0 ? 2 : result.PriceDecimals;

        string Price(decimal value) => SkPriceFormatter.Format(value, decimals);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(ComposeHeader);
                page.Footer().Element(ComposeFooter);

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(4);

                    // ---- Page 1: summary ----
                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text(Disclaimer)
                        .Italic().FontSize(9).FontColor(Colors.Grey.Darken3);

                    Heading(col, "Analysis details");
                    LabeledLine(col, "Symbol", result.Symbol);
                    LabeledLine(col, "Exchange", result.ExchangeName);
                    LabeledLine(col, "Primary timeframe", result.PrimaryTimeframe);
                    LabeledLine(col, "Higher timeframe", result.HigherTimeframe);
                    LabeledLine(col, "Lookback candles", result.LookbackCandles.ToString());
                    LabeledLine(col, "Swing sensitivity", result.SwingSensitivity);
                    LabeledLine(col, "Sequence direction mode", result.DirectionMode);
                    LabeledLine(col, "Explanation level", result.ExplanationMode);
                    LabeledLine(col, "Latest candle (UTC)", result.LatestCandleTimeUtc?.ToString("yyyy-MM-dd HH:mm") ?? "n/a");
                    LabeledLine(col, "Current price", Price(result.CurrentPrice));

                    Heading(col, "Summary");
                    LabeledLine(col, "Market direction", MarketDirectionText(result.MarketBias));
                    LabeledLine(col, "Best current idea", BestIdeaSummary(result));
                    LabeledLine(col, "Key area to watch", result.KeyAreaToWatch);
                    LabeledLine(col, "Danger level", result.DangerLevelToWatch);

                    if (!string.IsNullOrWhiteSpace(result.WhatThisMeans))
                    {
                        col.Item().PaddingTop(4).Text("What this means").SemiBold();
                        col.Item().Text(result.WhatThisMeans);
                    }

                    if (result.ClarityReasons.Count > 0 || result.ClarityWarnings.Count > 0)
                    {
                        col.Item().PaddingTop(4).Text($"Why clarity is {result.ConfidenceLabel.ToLowerInvariant()}").SemiBold();
                        foreach (var reason in result.ClarityReasons)
                        {
                            BulletLine(col, reason, Colors.Grey.Darken3);
                        }

                        foreach (var warning in result.ClarityWarnings)
                        {
                            BulletLine(col, warning, Colors.Orange.Darken2);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(result.BottomLine))
                    {
                        col.Item().PaddingTop(6).Background(Colors.Blue.Lighten5).Padding(8).Column(box =>
                        {
                            box.Item().Text("Bottom line").SemiBold().FontColor(Colors.Blue.Darken3);
                            box.Item().Text(result.BottomLine);
                        });
                    }

                    if (result.HtfContext is not null || result.LtfContext is not null)
                    {
                        Heading(col, "Higher timeframe context (HTF)");
                        if (result.HtfContext is not null)
                        {
                            LabeledLine(col, "Timeframe", result.HtfContext.Timeframe);
                            LabeledLine(col, "Direction", result.HtfContext.Direction);
                            LabeledLine(col, "Summary", result.HtfContext.Summary);
                            LabeledLine(col, "Major zones", result.HtfContext.ReactionZoneText);
                            LabeledLine(col, "Agrees with LTF", result.HtfContext.AgreesWithPrimary ? "Yes" : "No");
                        }

                        Heading(col, "Lower timeframe context (LTF)");
                        if (result.LtfContext is not null)
                        {
                            LabeledLine(col, "Timeframe", result.LtfContext.Timeframe);
                            LabeledLine(col, "Current setup", result.LtfContext.Direction);
                            LabeledLine(col, "Summary", result.LtfContext.Summary);
                            LabeledLine(col, "Reaction zone", result.LtfContext.ReactionZoneText);
                            LabeledLine(col, "Danger level", result.LtfContext.DangerLevelText);
                            LabeledLine(col, "Targets", result.LtfContext.TargetsText);
                            LabeledLine(col, "Clarity", result.LtfContext.ClarityLabel);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(result.AiSummary?.WhatWouldMakeWrong))
                    {
                        col.Item().PaddingTop(4).Text("What would make this wrong").SemiBold();
                        col.Item().Text(result.AiSummary.WhatWouldMakeWrong);
                    }

                    if (!string.IsNullOrWhiteSpace(result.AiSummary?.WhatToWatchNext))
                    {
                        col.Item().PaddingTop(4).Text("What to watch next").SemiBold();
                        col.Item().Text(result.AiSummary.WhatToWatchNext);
                    }

                    if (result.ConceptAudit is not null)
                    {
                        Heading(col, "SK concept audit");
                        LabeledLine(col, "HTF direction", result.ConceptAudit.HtfDirection);
                        LabeledLine(col, "LTF direction", result.ConceptAudit.LtfDirection);
                        LabeledLine(col, "HTF/LTF agreement", result.ConceptAudit.HtfLtfAgreement ? "Yes" : "No");
                        LabeledLine(col, "Selected direction", result.ConceptAudit.SelectedSequenceDirection);
                        LabeledLine(col, "Sequence status", result.ConceptAudit.SequenceStatus);
                        LabeledLine(col, "Validity", result.ConceptAudit.ValidityStatus);
                        LabeledLine(col, "Usefulness", result.ConceptAudit.UsefulnessStatus);
                        LabeledLine(col, "Target validation", result.ConceptAudit.TargetValidation);
                        LabeledLine(col, "Hidden structures", result.ConceptAudit.HiddenStructuresCount.ToString());
                        LabeledLine(col, "Direction mismatch structures", result.ConceptAudit.DirectionMismatchStructuresCount.ToString());
                    }

                    if (result.Sequences.Count > 0)
                    {
                        Heading(col, "Sequence anatomy");
                        foreach (var sequence in result.Sequences.Where(s => s.SelectedAsBest || !s.HiddenFromBeginner).Take(3))
                        {
                            col.Item().PaddingBottom(4).Text(text =>
                            {
                                text.Span($"{sequence.Direction} ({sequence.StructureCategory}): ").SemiBold();
                                text.Span(sequence.BeginnerExplanation);
                            });
                        }
                    }

                    col.Item().PaddingTop(6).Background(Colors.Grey.Lighten3).Padding(8).Column(box =>
                    {
                        box.Item().Text("Why this is not a trade signal").SemiBold();
                        box.Item().Text(result.AiSummary?.WhyNotTradeSignal ??
                            "This analysis identifies possible chart areas and scenarios. It does not check your account risk, " +
                            "does not create orders, and does not confirm execution.");
                    });

                    // ---- Page 2: chart + key levels ----
                    col.Item().PageBreak();
                    Heading(col, "Chart");
                    if (imageBytes is not null)
                    {
                        col.Item().Image(imageBytes).FitWidth();
                        col.Item().PaddingTop(2).Text("Overlays show calculated levels only. Not a trade signal.")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                    }
                    else
                    {
                        var reason = string.IsNullOrWhiteSpace(chartUnavailableReason)
                            ? "unknown error"
                            : chartUnavailableReason;
                        col.Item().Text($"Chart image unavailable because: {reason}")
                            .Italic().FontColor(Colors.Grey.Darken2);
                    }

                    Heading(col, "Chart legend");
                    foreach (var entry in BuildLegend())
                    {
                        col.Item().PaddingBottom(1).Text(text =>
                        {
                            text.Span($"{entry.Label}: ").SemiBold();
                            text.Span(entry.Meaning);
                        });
                    }

                    Heading(col, "Key levels by setup");
                    var groups = BuildKeyLevelGroups(result, decimals);
                    if (groups.Count == 0)
                    {
                        col.Item().Text("No key levels were calculated.").Italic();
                    }
                    else
                    {
                        foreach (var levelGroup in groups)
                        {
                            col.Item().PaddingTop(4).Text(levelGroup.GroupName).SemiBold().FontColor(Colors.Blue.Darken1);
                            foreach (var row in levelGroup.Rows)
                            {
                                col.Item().PaddingLeft(8).PaddingBottom(1).Text(text =>
                                {
                                    text.Span($"{row.LevelName}: ").SemiBold();
                                    text.Span($"{row.PriceText} — {row.Meaning}");
                                });
                            }
                        }
                    }

                    // ---- Page 3: ideas + scenarios ----
                    col.Item().PageBreak();
                    ComposeIdea(col, "Possible upward move", result.BestBullishIdea, Price);
                    ComposeIdea(col, "Possible downward move", result.BestBearishIdea, Price);

                    Heading(col, "Scenarios");
                    ParagraphLabeled(col, "Upward scenario", result.BullishScenario);
                    ParagraphLabeled(col, "Downward scenario", result.BearishScenario);
                    ParagraphLabeled(col, "Higher timeframe view", result.HigherTimeframeExplanation);
                    if (!string.IsNullOrWhiteSpace(result.ConflictExplanation))
                    {
                        col.Item().PaddingTop(2).Background(Colors.Orange.Lighten4).Padding(6)
                            .Text(result.ConflictExplanation).FontColor(Colors.Orange.Darken3);
                    }

                    var bestIds = new HashSet<string>();
                    if (result.BestBullishIdea is not null)
                    {
                        bestIds.Add(result.BestBullishIdea.CandidateId);
                    }

                    if (result.BestBearishIdea is not null)
                    {
                        bestIds.Add(result.BestBearishIdea.CandidateId);
                    }

                    var others = result.SequenceCandidates
                        .Where(c => !bestIds.Contains(c.Id) && c.EligibleForBestIdea)
                        .Take(3)
                        .ToList();
                    if (others.Count > 0)
                    {
                        Heading(col, "Alternative structures");
                        foreach (var candidate in others)
                        {
                            var label = candidate.Direction == "Bullish" ? "Possible upward move" : "Possible downward move";
                            col.Item().PaddingBottom(3).Text(text =>
                            {
                                text.Span($"{label} — clarity {candidate.ConfidenceScore}/100 [{SkConceptScoring.StructureCategoryLabel(candidate)}]. ").SemiBold();
                                text.Span(
                                    $"Reaction {Price(candidate.CorrectionZoneMin)} – {Price(candidate.CorrectionZoneMax)}, " +
                                    $"danger {Price(candidate.InvalidationLevel)}, targets {Price(candidate.Target1)} / {Price(candidate.Target2)}.");
                            });
                        }
                    }

                    var invalid = result.SequenceCandidates
                        .Where(c => SkConceptScoring.IsHiddenFromBeginner(c))
                        .Take(3)
                        .ToList();
                    if (invalid.Count > 0 && request.IncludeRawDiagnostics)
                    {
                        Heading(col, "Invalid structures (diagnostics)");
                        foreach (var candidate in invalid)
                        {
                            col.Item().PaddingBottom(2).Text(
                                $"{SkConceptScoring.StructureCategoryLabel(candidate)}: {candidate.ValidationMessage}");
                        }
                    }

                    // ---- Page 4: glossary + optional diagnostics ----
                    if (request.IncludeGlossary && result.GlossaryTerms.Count > 0)
                    {
                        col.Item().PageBreak();
                        Heading(col, "Explain these terms");
                        foreach (var term in result.GlossaryTerms)
                        {
                            col.Item().PaddingBottom(3).Text(text =>
                            {
                                text.Span($"{term.Term}: ").SemiBold();
                                text.Span(term.Explanation);
                            });
                        }
                    }

                    if (request.IncludeRawDiagnostics)
                    {
                        Heading(col, "Raw diagnostics");
                        LabeledLine(col, "Primary candles", result.Diagnostics.PrimaryCandleCount.ToString());
                        LabeledLine(col, "Higher candles", result.Diagnostics.HigherCandleCount.ToString());
                        LabeledLine(col, "Swing highs", result.Diagnostics.SwingHighCount.ToString());
                        LabeledLine(col, "Swing lows", result.Diagnostics.SwingLowCount.ToString());
                        LabeledLine(col, "Sequence candidates", result.Diagnostics.SequenceCandidateCount.ToString());
                        LabeledLine(col, "Sensitivity", result.Diagnostics.ResolvedSensitivity);
                        LabeledLine(col, "Correction fib", string.Join(", ", result.Diagnostics.FibonacciCorrectionLevels));
                        LabeledLine(col, "Extension fib", string.Join(", ", result.Diagnostics.FibonacciExtensionLevels));
                        col.Item().PaddingTop(2).Text(result.Diagnostics.Note).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                    }
                });
            });
        });

        return document.GeneratePdf();

        static void ComposeHeader(IContainer container)
        {
            container.Column(header =>
            {
                header.Item().Text("MOMO Quant").FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                header.Item().Text("SK System Analyzer Report").FontSize(13).SemiBold();
                header.Item().Text("Analysis only — not a trade signal").FontSize(9).FontColor(Colors.Red.Medium);
                header.Item().Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                header.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });
        }

        static void ComposeFooter(IContainer container)
        {
            container.Column(footer =>
            {
                footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                footer.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(Disclaimer).FontSize(7).FontColor(Colors.Grey.Darken1);
                    row.ConstantItem(70).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            });
        }
    }

    private static void Heading(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(10).PaddingBottom(3).Text(title).FontSize(14).Bold().FontColor(Colors.Blue.Darken2);

    private static void LabeledLine(ColumnDescriptor col, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        col.Item().PaddingBottom(1).Text(text =>
        {
            text.Span($"{label}: ").SemiBold();
            text.Span(value);
        });
    }

    private static void ParagraphLabeled(ColumnDescriptor col, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        col.Item().PaddingTop(3).Text(label).SemiBold();
        col.Item().Text(value);
    }

    private static void BulletLine(ColumnDescriptor col, string value, string color)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(10).Text("•").FontColor(color);
            row.RelativeItem().Text(value).FontColor(color);
        });
    }

    private static void ComposeIdea(
        ColumnDescriptor col,
        string title,
        SkIdeaDto? idea,
        Func<decimal, string> price)
    {
        Heading(col, title);
        if (idea is null)
        {
            col.Item().Text("No clear setup detected for this direction.").Italic().FontColor(Colors.Grey.Darken2);
            return;
        }

        LabeledLine(col, "Status", idea.StatusLabel);
        LabeledLine(col, "Clarity", $"{idea.ClarityLabel} ({idea.ClarityScore}/100)");
        LabeledLine(col, "Reaction zone", idea.ReactionZoneText);
        LabeledLine(col, "Strong reaction zone", idea.StrongReactionZoneText);
        LabeledLine(col, "Danger level", idea.DangerLevelText);
        LabeledLine(col, "First target", price(idea.Target1));
        LabeledLine(col, "Second target", price(idea.Target2));
        col.Item().PaddingTop(2).Text(idea.PlainExplanation);
    }

    private static string MarketDirectionText(string bias) => bias switch
    {
        "Bullish" => "Bullish — possible upward bias",
        "Bearish" => "Bearish — possible downward bias",
        "Mixed" => "Mixed — no clean direction",
        "Neutral" => "Neutral — no strong trend",
        _ => "Unclear — no confirmed setup"
    };

    private static string BestIdeaSummary(SkSystemAnalysisResultDto result)
    {
        var idea = PickKeyIdea(result);
        if (idea is null)
        {
            return "No clear setup yet.";
        }

        var text = $"{idea.DirectionLabel} ({idea.ClarityLabel.ToLowerInvariant()} clarity)";
        if (!string.IsNullOrWhiteSpace(result.ConflictExplanation))
        {
            text += ", but the higher timeframe does not agree";
        }

        return text + ".";
    }

    /// <summary>A single explained key level row for the grouped PDF section.</summary>
    public sealed record SkPdfKeyLevelRow(string LevelName, string PriceText, string Meaning);

    /// <summary>A grouped set of key levels (best upward, best downward, other).</summary>
    public sealed record SkPdfKeyLevelGroup(string GroupName, IReadOnlyList<SkPdfKeyLevelRow> Rows);

    /// <summary>A single chart legend entry (label + plain meaning).</summary>
    public sealed record SkPdfLegendEntry(string Label, string Meaning);

    /// <summary>Static legend explaining each chart line type. Shared by the PDF and tests.</summary>
    public static IReadOnlyList<SkPdfLegendEntry> BuildLegend() =>
    [
        new("Current price", "The latest market price."),
        new("Upward reaction zone", "Area where price may pull back and possibly react upward."),
        new("Downward reaction zone", "Area where price may bounce and possibly react downward."),
        new("Strong reaction zone", "A tighter area within the reaction zone where a turn is more likely."),
        new("Danger level", "If price crosses this level, the idea is no longer valid."),
        new("Target 1 / Target 2", "Areas price may move toward if the idea works."),
        new("Fibonacci retracement / extension", "Calculated Fibonacci levels used by the analyzer. They are not trade signals."),
        new("Higher timeframe level", "An important level taken from the higher timeframe chart."),
        new("Swing high / swing low", "Recent local peaks and dips used to build structure."),
        new("Sequence start / pullback point", "Structure points the analyzer used to build an idea.")
    ];

    /// <summary>
    /// Builds grouped key levels (best upward idea, best downward idea, and up to three
    /// other structures) instead of one long ungrouped list. Shared by the PDF and tests.
    /// </summary>
    public static IReadOnlyList<SkPdfKeyLevelGroup> BuildKeyLevelGroups(SkSystemAnalysisResultDto result, int decimals)
    {
        var groups = new List<SkPdfKeyLevelGroup>();

        SkPdfKeyLevelGroup? IdeaGroup(string name, SkIdeaDto? idea, bool bullish)
        {
            if (idea is null)
            {
                return null;
            }

            var rows = new List<SkPdfKeyLevelRow>
            {
                new("Reaction zone", idea.ReactionZoneText,
                    bullish ? "Area where price may turn upward." : "Area where price may reject downward."),
                new("Strong reaction zone", idea.StrongReactionZoneText,
                    "Tighter area where a turn is more likely."),
                new("Danger level", idea.DangerLevelText,
                    bullish ? "Below this, the upward idea is invalid." : "Above this, the downward idea is invalid."),
                new("Target 1", SkPriceFormatter.Format(idea.Target1, decimals), "First area to watch."),
                new("Target 2", SkPriceFormatter.Format(idea.Target2, decimals), "Second area to watch.")
            };
            return new SkPdfKeyLevelGroup(name, rows);
        }

        var bull = IdeaGroup("Best upward idea", result.BestBullishIdea, bullish: true);
        if (bull is not null)
        {
            groups.Add(bull);
        }

        var bear = IdeaGroup("Best downward idea", result.BestBearishIdea, bullish: false);
        if (bear is not null)
        {
            groups.Add(bear);
        }

        var bestIds = new HashSet<string>(StringComparer.Ordinal);
        if (result.BestBullishIdea is not null)
        {
            bestIds.Add(result.BestBullishIdea.CandidateId);
        }

        if (result.BestBearishIdea is not null)
        {
            bestIds.Add(result.BestBearishIdea.CandidateId);
        }

        var others = result.SequenceCandidates
            .Where(candidate => !bestIds.Contains(candidate.Id))
            .Take(3)
            .Select((candidate, index) =>
            {
                var direction = candidate.Direction == "Bullish" ? "upward" : "downward";
                var priceText =
                    $"Reaction {SkPriceFormatter.Format(candidate.CorrectionZoneMin, decimals)} – {SkPriceFormatter.Format(candidate.CorrectionZoneMax, decimals)}, " +
                    $"danger {SkPriceFormatter.Format(candidate.InvalidationLevel, decimals)}, " +
                    $"targets {SkPriceFormatter.Format(candidate.Target1, decimals)} / {SkPriceFormatter.Format(candidate.Target2, decimals)}";
                return new SkPdfKeyLevelRow($"Structure #{index + 1}", priceText, $"A possible {direction} structure.");
            })
            .ToList();

        if (others.Count > 0)
        {
            groups.Add(new SkPdfKeyLevelGroup("Other possible structures", others));
        }

        return groups;
    }

    private static SkIdeaDto? PickKeyIdea(SkSystemAnalysisResultDto result)
    {
        var bull = result.BestBullishIdea;
        var bear = result.BestBearishIdea;
        if (result.MarketBias == "Bullish" && bull is not null)
        {
            return bull;
        }

        if (result.MarketBias == "Bearish" && bear is not null)
        {
            return bear;
        }

        if (bull is null)
        {
            return bear;
        }

        if (bear is null)
        {
            return bull;
        }

        return bull.ClarityScore >= bear.ClarityScore ? bull : bear;
    }
}
