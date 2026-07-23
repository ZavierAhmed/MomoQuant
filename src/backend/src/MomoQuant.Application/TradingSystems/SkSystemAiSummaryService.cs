using System.Text;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Builds an analysis-only, beginner-friendly summary. Uses a deterministic, rule-based
/// generator that references only calculated levels, never invents levels, never gives
/// buy/sell advice, and adapts its wording to the requested explanation mode.
/// </summary>
public sealed class SkSystemAiSummaryService : ISkSystemAiSummaryService
{
    private const string AnalysisOnlyTag = "Analysis only, not a trade signal.";

    private const string WhyNotTradeSignalDefault =
        "This analysis identifies possible chart areas and scenarios. It does not check your account risk, " +
        "does not create orders, and does not confirm execution.";

    private readonly ILogger<SkSystemAiSummaryService> _logger;

    public SkSystemAiSummaryService(ILogger<SkSystemAiSummaryService> logger)
    {
        _logger = logger;
    }

    public Task<SkAiSummaryDto> BuildSummaryAsync(SkSummaryInput input, CancellationToken cancellationToken = default)
    {
        var summary = BuildRuleBasedSummary(input);
        return Task.FromResult(summary);
    }

    private SkAiSummaryDto BuildRuleBasedSummary(SkSummaryInput input)
    {
        var mode = SkSystemConstants.NormalizeExplanationMode(input.ExplanationMode);
        var decimals = input.PriceDecimals;
        string Fmt(decimal value) => SkPriceFormatter.Format(value, decimals);

        var warnings = new List<string>(input.Warnings);
        var hasConflict = input.HigherTimeframeContext?.ConflictWarning is { Length: > 0 };
        if (hasConflict && !warnings.Contains(input.HigherTimeframeContext!.ConflictWarning!))
        {
            warnings.Add(input.HigherTimeframeContext!.ConflictWarning!);
        }

        var conflictExplanation = hasConflict
            ? "Warning: The short-term chart and higher-timeframe chart do not agree. This makes the setup less clear."
            : string.Empty;

        var higherExplanation = BuildHigherTimeframeExplanation(
            input.HigherTimeframeContext, input.PrimaryTimeframe, input.HigherTimeframe);

        var eligible = input.Candidates.Where(c => c.EligibleForBestIdea).ToList();
        var invalidCount = input.Candidates.Count(c => SkConceptScoring.IsHiddenFromBeginner(c));
        var alternativeNote = BuildAlternativeStructuresNote(input.Candidates, eligible, mode);

        if (eligible.Count == 0)
        {
            var unclearPlain =
                $"{input.Symbol} does not show a clear, confirmed pattern on the {input.PrimaryTimeframe} chart right now. " +
                $"The structure is unclear and the direction looks mixed, so there are no reliable levels to rely on. {AnalysisOnlyTag}";

            var unclearExpert =
                $"{input.Symbol}: no valid SK sequence (Z→A→B) could be built from the detected swings on {input.PrimaryTimeframe}. " +
                $"Structure is unclear, so no correction zone, golden pocket, invalidation, or extension targets are projected. {AnalysisOnlyTag}";

            return new SkAiSummaryDto
            {
                Summary = mode == "Expert" ? unclearExpert : unclearPlain,
                PlainLanguageSummary = unclearPlain,
                WhatThisMeans =
                    "Because no clean structure is visible, it is better to wait for the market to form a clearer pattern before drawing conclusions.",
                WhatWouldMakeWrong = "Without a valid structure, any level guess would be unreliable.",
                WhatToWatchNext = "Watch for a clearer swing sequence to form before drawing conclusions.",
                WhyNotTradeSignal = WhyNotTradeSignalDefault,
                UsefulnessExplanation = "No useful setup is available right now.",
                AlternativeStructuresNote = alternativeNote,
                BottomLine = "No clear SK structure is visible from the current candles.",
                HigherTimeframeExplanation = higherExplanation,
                ConflictExplanation = conflictExplanation,
                BullishScenario = "No clear upward setup detected.",
                BearishScenario = "No clear downward setup detected.",
                InvalidationExplanation = "No setup is defined, so there is no danger level yet.",
                Warnings = warnings,
                ConfidenceLabel = "Low",
                AnalysisOnly = true,
                UsedFallback = true,
                Source = "RuleBased"
            };
        }

        var bestBull = eligible
            .Where(c => c.Direction == "Bullish")
            .OrderByDescending(c => c.ConfidenceScore)
            .FirstOrDefault();
        var bestBear = eligible
            .Where(c => c.Direction == "Bearish")
            .OrderByDescending(c => c.ConfidenceScore)
            .FirstOrDefault();
        var top = eligible.OrderByDescending(c => c.ConfidenceScore).First();

        var primary = input.MarketBias switch
        {
            "Bullish" when bestBull is not null => bestBull,
            "Bearish" when bestBear is not null => bestBear,
            _ => top
        };

        var plain = BuildPlainSummary(input, primary, hasConflict, Fmt);
        var intermediate = BuildIntermediateSummary(input, primary, Fmt);
        var expert = BuildExpertSummary(input, primary, Fmt);

        var summary = mode switch
        {
            "Expert" => expert,
            "Intermediate" => intermediate,
            _ => plain
        };

        return new SkAiSummaryDto
        {
            Summary = summary,
            PlainLanguageSummary = plain,
            WhatThisMeans = BuildWhatThisMeans(primary, hasConflict, input.HigherTimeframe),
            WhatWouldMakeWrong = BuildWhatWouldMakeWrong(primary, Fmt),
            WhatToWatchNext = BuildWhatToWatchNext(primary, Fmt),
            WhyNotTradeSignal = WhyNotTradeSignalDefault,
            UsefulnessExplanation = BuildUsefulnessExplanation(primary),
            AlternativeStructuresNote = alternativeNote,
            BottomLine = BuildBottomLine(input.MarketBias, bestBull, bestBear, hasConflict),
            HigherTimeframeExplanation = higherExplanation,
            ConflictExplanation = conflictExplanation,
            BullishScenario = BuildScenario(bestBull, "Bullish", mode, Fmt),
            BearishScenario = BuildScenario(bestBear, "Bearish", mode, Fmt),
            InvalidationExplanation = BuildInvalidation(eligible, Fmt),
            Warnings = warnings,
            ConfidenceLabel = input.ConfidenceLabel,
            AnalysisOnly = true,
            UsedFallback = true,
            Source = "RuleBased"
        };
    }

    private static string BuildAlternativeStructuresNote(
        IReadOnlyList<SkSequenceCandidateDto> all,
        IReadOnlyList<SkSequenceCandidateDto> eligible,
        string mode)
    {
        var hidden = all.Count - eligible.Count;
        if (hidden <= 0)
        {
            return string.Empty;
        }

        if (mode == "Beginner")
        {
            return "Lower-quality or invalid structures exist, but they are hidden in Beginner view.";
        }

        var labels = all
            .Where(c => !c.EligibleForBestIdea)
            .Select(c => SkConceptScoring.StructureCategoryLabel(c))
            .Distinct()
            .ToList();

        return $"Advanced diagnostics: {hidden} hidden structure(s) — {string.Join(", ", labels)}.";
    }

    private static string BuildPlainSummary(
        SkSummaryInput input,
        SkSequenceCandidateDto primary,
        bool hasConflict,
        Func<decimal, string> fmt)
    {
        var upward = primary.Direction == "Bullish";
        var patternWord = upward ? "upward" : "downward";
        var crossWord = upward ? "falls below" : "rises above";
        var sideWord = upward ? "above" : "below";
        var conflictSuffix = hasConflict
            ? $" The bigger {input.HigherTimeframe} chart does not fully agree, so treat this carefully."
            : string.Empty;

        return
            $"{input.Symbol} may be forming a {patternWord} pattern on the {input.PrimaryTimeframe} chart, but it is not confirmed yet. " +
            $"The important reaction area is between {fmt(primary.CorrectionZoneMin)} and {fmt(primary.CorrectionZoneMax)}. " +
            $"If price {crossWord} {fmt(primary.InvalidationLevel)}, this idea is no longer valid. " +
            $"If it holds, the areas to watch {sideWord} are {fmt(primary.Target1)} and {fmt(primary.Target2)}.{conflictSuffix} {AnalysisOnlyTag}";
    }

    private static string BuildIntermediateSummary(
        SkSummaryInput input,
        SkSequenceCandidateDto primary,
        Func<decimal, string> fmt)
    {
        var dirWord = primary.Direction == "Bullish" ? "upward" : "downward";
        var position = SkDisplayLabels.Position(primary.CurrentPricePosition).ToLowerInvariant();
        var higherText = input.HigherTimeframeContext is null
            ? string.Empty
            : $" The higher timeframe ({input.HigherTimeframe}) is {input.HigherTimeframeContext.HigherTimeframeBias.ToLowerInvariant()}.";

        return
            $"{input.Symbol} shows a possible {dirWord} setup on {input.PrimaryTimeframe}. " +
            $"Price is currently {position}. The reaction zone (the area where price may pause or turn) is between " +
            $"{fmt(primary.CorrectionZoneMin)} and {fmt(primary.CorrectionZoneMax)}. " +
            $"The danger level (where the idea becomes invalid) is {fmt(primary.InvalidationLevel)}. " +
            $"Targets are {fmt(primary.Target1)} and {fmt(primary.Target2)}.{higherText} {AnalysisOnlyTag}";
    }

    private static string BuildExpertSummary(
        SkSummaryInput input,
        SkSequenceCandidateDto primary,
        Func<decimal, string> fmt)
    {
        var dirWord = primary.Direction.ToLowerInvariant();
        var higherText = input.HigherTimeframeContext is null
            ? string.Empty
            : $" Higher timeframe ({input.HigherTimeframe}) bias is {input.HigherTimeframeContext.HigherTimeframeBias.ToLowerInvariant()}.";

        return
            $"{input.Symbol}: potential {dirWord} SK sequence on {input.PrimaryTimeframe} (Z→A→B). " +
            $"Price {fmt(input.CurrentPrice)} is {SkDisplayLabels.Position(primary.CurrentPricePosition).ToLowerInvariant()}. " +
            $"Correction zone {fmt(primary.CorrectionZoneMin)}–{fmt(primary.CorrectionZoneMax)} " +
            $"(golden pocket {fmt(primary.GoldenPocketMin)}–{fmt(primary.GoldenPocketMax)}). " +
            $"Invalidation at {fmt(primary.InvalidationLevel)}; extension targets {fmt(primary.Target1)} and {fmt(primary.Target2)} " +
            $"(1.618 → {fmt(primary.Extension1618)}).{higherText} {AnalysisOnlyTag}";
    }

    private static string BuildWhatThisMeans(SkSequenceCandidateDto primary, bool hasConflict, string higherTimeframe)
    {
        var upward = primary.Direction == "Bullish";
        var builder = new StringBuilder();

        builder.Append(upward
            ? "Price first moved upward from the sequence start to the impulse high. It then pulled back into the reaction zone. " +
              "This is why the analyzer is watching for a possible upward reaction."
            : "Price first moved downward from the sequence start to the impulse low. It then bounced into the reaction zone. " +
              "This is why the analyzer is watching for a possible downward rejection.");

        builder.Append(' ');
        builder.Append(primary.CurrentPricePosition switch
        {
            "InsideCorrectionZone" => "Price is inside the reaction zone now — the area this setup cares about most.",
            "BeforeCorrectionZone" => "Price has not returned to the reaction zone yet.",
            "LeftCorrectionZone" => "Price has moved away from the reaction zone, which weakens the setup.",
            "NearTarget" => "Price is already close to the target area, so much of the expected move may have happened.",
            _ => "Price is currently around the calculated levels for this setup."
        });

        if (hasConflict)
        {
            builder.Append($" Because the {higherTimeframe} chart does not agree, this setup should be treated carefully.");
        }

        return builder.ToString();
    }

    private static string BuildWhatWouldMakeWrong(SkSequenceCandidateDto primary, Func<decimal, string> fmt)
    {
        var upward = primary.Direction == "Bullish";
        return upward
            ? $"This upward idea would be wrong if price closes below the danger level at {fmt(primary.InvalidationLevel)}, " +
              "or if price fails to react from the reaction zone."
            : $"This downward idea would be wrong if price closes above the danger level at {fmt(primary.InvalidationLevel)}, " +
              "or if price fails to reject from the reaction zone.";
    }

    private static string BuildWhatToWatchNext(SkSequenceCandidateDto primary, Func<decimal, string> fmt)
    {
        var upward = primary.Direction == "Bullish";
        return upward
            ? $"Watch whether price holds above {fmt(primary.InvalidationLevel)} and reacts from " +
              $"{fmt(primary.CorrectionZoneMin)}–{fmt(primary.CorrectionZoneMax)}. Confirmation is still needed."
            : $"Watch whether price stays below {fmt(primary.InvalidationLevel)} and rejects from " +
              $"{fmt(primary.CorrectionZoneMin)}–{fmt(primary.CorrectionZoneMax)}. Confirmation is still needed.";
    }

    private static string BuildUsefulnessExplanation(SkSequenceCandidateDto primary) =>
        primary.CurrentPricePosition switch
        {
            "InsideCorrectionZone" =>
                "Usefulness: price is inside the reaction zone, so the idea is relevant right now.",
            "NearTarget" or _ when primary.ValidationStatus == SkScenarioValidator.AlreadyReached =>
                "Usefulness: price may have already reached the target area, so the idea may be late.",
            "BeforeCorrectionZone" =>
                "Usefulness: price has not reached the reaction zone yet — the idea is forming but not active.",
            _ => "Usefulness: monitor whether price returns to the reaction zone."
        };

    private static string BuildBottomLine(
        string bias,
        SkSequenceCandidateDto? bestBull,
        SkSequenceCandidateDto? bestBear,
        bool hasConflict)
    {
        if (!hasConflict && bias == "Bullish" && bestBull is not null)
        {
            return "The clearest idea is a possible upward move. Watch for price to react from the reaction zone. " +
                   "If price breaks below the danger level, the idea is no longer valid.";
        }

        if (!hasConflict && bias == "Bearish" && bestBear is not null)
        {
            return "The clearest idea is a possible downward move. Watch for price to reject from the reaction zone. " +
                   "If price breaks above the danger level, the idea is no longer valid.";
        }

        if (bestBull is not null && bestBear is not null || hasConflict || bias == "Mixed")
        {
            return "The chart is mixed. There are possible upward and downward structures, but the higher timeframe does not fully agree. " +
                   "Treat this as observation only.";
        }

        if (bestBull is not null)
        {
            return "There is a possible upward structure, but confirmation is still needed. Watch how price reacts around the reaction zone.";
        }

        if (bestBear is not null)
        {
            return "There is a possible downward structure, but confirmation is still needed. Watch how price reacts around the reaction zone.";
        }

        return "No clear SK structure is visible from the current candles.";
    }

    private static string BuildScenario(
        SkSequenceCandidateDto? candidate,
        string direction,
        string mode,
        Func<decimal, string> fmt)
    {
        if (candidate is null)
        {
            return direction == "Bullish"
                ? "No clear upward setup detected."
                : "No clear downward setup detected.";
        }

        var label = mode != "Beginner"
            ? $" [{SkConceptScoring.StructureCategoryLabel(candidate)}]"
            : string.Empty;

        return direction == "Bullish"
            ? $"Possible upward idea: If price stays above {fmt(candidate.InvalidationLevel)} and reacts from the " +
              $"{fmt(candidate.CorrectionZoneMin)}–{fmt(candidate.CorrectionZoneMax)} area, the next areas to watch above are " +
              $"{fmt(candidate.Target1)} and {fmt(candidate.Target2)}.{label}"
            : $"Possible downward idea: If price stays below {fmt(candidate.InvalidationLevel)} and rejects from the " +
              $"{fmt(candidate.CorrectionZoneMin)}–{fmt(candidate.CorrectionZoneMax)} area, the next areas to watch below are " +
              $"{fmt(candidate.Target1)} and {fmt(candidate.Target2)}.{label}";
    }

    private static string BuildInvalidation(IReadOnlyList<SkSequenceCandidateDto> candidates, Func<decimal, string> fmt)
    {
        var builder = new StringBuilder();
        foreach (var candidate in candidates)
        {
            var upward = candidate.Direction == "Bullish";
            var side = upward ? "below" : "above";
            var moveWord = upward ? "upward" : "downward";
            builder.Append(
                $"The {moveWord} idea is no longer valid if price closes {side} {fmt(candidate.InvalidationLevel)}. ");
        }

        return builder.ToString().Trim();
    }

    private static string BuildHigherTimeframeExplanation(
        SkMultiTimeframeContextDto? context,
        string primaryTimeframe,
        string higherTimeframe)
    {
        if (context is null)
        {
            return string.Empty;
        }

        return context.HigherTimeframeBias switch
        {
            "Bearish" =>
                $"Higher timeframe view: the {higherTimeframe} chart is bearish because price is making lower highs and lower lows. " +
                $"This means upward setups on the {primaryTimeframe} chart are weaker and should be treated carefully.",
            "Bullish" =>
                $"Higher timeframe view: the {higherTimeframe} chart is bullish because price is making higher highs and higher lows. " +
                $"This supports upward setups on the {primaryTimeframe} chart, but confirmation is still needed.",
            "Mixed" =>
                $"Higher timeframe view: the {higherTimeframe} chart is mixed, with no clean trend. " +
                $"This makes {primaryTimeframe} setups less reliable.",
            _ =>
                $"Higher timeframe view: the {higherTimeframe} chart is neutral, with no strong trend yet."
        };
    }
}
