using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkSystemAiSummaryServiceTests
{
    private static SkSequenceCandidateDto BullishCandidate() => new()
    {
        Id = "B-1",
        Direction = "Bullish",
        Status = "Active",
        CorrectionZoneMin = 106m,
        CorrectionZoneMax = 112m,
        GoldenPocketMin = 106m,
        GoldenPocketMax = 108m,
        Target1 = 130m,
        Target2 = 135m,
        Extension1618 = 142m,
        InvalidationLevel = 100m,
        CurrentPricePosition = "InsideCorrectionZone",
        ConfidenceScore = 65m
    };

    [Fact]
    public async Task BuildSummary_WithCandidate_UsesRuleBasedFallbackAndReferencesLevels()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "BTCUSDT",
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            CurrentPrice = 110m,
            MarketBias = "Bullish",
            ConfidenceLabel = "Medium",
            Candidates = [BullishCandidate()],
            UseAiSummary = true
        });

        Assert.True(summary.UsedFallback);
        Assert.Equal("RuleBased", summary.Source);
        Assert.True(summary.AnalysisOnly);
        Assert.Contains("BTCUSDT", summary.Summary);
        Assert.Contains("analysis only", summary.Summary, StringComparison.OrdinalIgnoreCase);
        // Analysis-only summaries never contain direct trade instructions.
        Assert.DoesNotContain("buy now", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sell now", summary.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildSummary_NoCandidates_StatesStructureIsUnclear()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "ETHUSDT",
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            CurrentPrice = 3000m,
            MarketBias = "Unknown",
            ConfidenceLabel = "Low",
            Candidates = [],
            UseAiSummary = true
        });

        Assert.True(summary.UsedFallback);
        Assert.Contains("unclear", summary.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildSummary_Beginner_ReturnsPlainLanguageSummary()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "BTCUSDT",
            PrimaryTimeframe = "4h",
            HigherTimeframe = "1d",
            CurrentPrice = 62000m,
            MarketBias = "Bullish",
            ConfidenceLabel = "Medium",
            Candidates = [BullishCandidate()],
            ExplanationMode = "Beginner",
            PriceDecimals = 2,
            UseAiSummary = true
        });

        Assert.False(string.IsNullOrWhiteSpace(summary.PlainLanguageSummary));
        Assert.Equal(summary.PlainLanguageSummary, summary.Summary);
        Assert.False(string.IsNullOrWhiteSpace(summary.BottomLine));
    }

    [Fact]
    public async Task BuildSummary_Expert_ReturnsTechnicalTerms()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "BTCUSDT",
            PrimaryTimeframe = "4h",
            HigherTimeframe = "1d",
            CurrentPrice = 108m,
            MarketBias = "Bullish",
            ConfidenceLabel = "Medium",
            Candidates = [BullishCandidate()],
            ExplanationMode = "Expert",
            PriceDecimals = 2,
            UseAiSummary = true
        });

        Assert.Contains("sequence", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("golden pocket", summary.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildSummary_HigherTimeframeConflict_ReturnsConflictExplanation()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "BTCUSDT",
            PrimaryTimeframe = "4h",
            HigherTimeframe = "1d",
            CurrentPrice = 108m,
            MarketBias = "Mixed",
            ConfidenceLabel = "Low",
            Candidates = [BullishCandidate()],
            HigherTimeframeContext = new SkMultiTimeframeContextDto
            {
                HigherTimeframeBias = "Bearish",
                HigherTimeframeTrendDescription = "1d shows lower highs/lows.",
                ConflictWarning = "Primary timeframe sequence conflicts with higher timeframe context."
            },
            ExplanationMode = "Beginner",
            PriceDecimals = 2,
            UseAiSummary = true
        });

        Assert.False(string.IsNullOrWhiteSpace(summary.ConflictExplanation));
        Assert.False(string.IsNullOrWhiteSpace(summary.HigherTimeframeExplanation));
    }

    [Fact]
    public async Task BuildSummary_Fallback_DoesNotUseTradeCommandLanguage()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "BTCUSDT",
            PrimaryTimeframe = "4h",
            HigherTimeframe = "1d",
            CurrentPrice = 108m,
            MarketBias = "Bullish",
            ConfidenceLabel = "Medium",
            Candidates = [BullishCandidate()],
            ExplanationMode = "Beginner",
            PriceDecimals = 2,
            UseAiSummary = true
        });

        var combined = string.Join(
            " ",
            summary.Summary,
            summary.PlainLanguageSummary,
            summary.WhatThisMeans,
            summary.BottomLine,
            summary.BullishScenario,
            summary.BearishScenario,
            summary.InvalidationExplanation);

        foreach (var forbidden in new[] { "buy", "sell", "enter now", "guaranteed", "high probability" })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task BuildSummary_InvalidBearishHidden_DoesNotShowDownwardScenario()
    {
        var service = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        var invalidBear = new SkSequenceCandidateDto
        {
            Id = "bear-bad",
            Direction = "Bearish",
            Status = "Potential",
            CorrectionZoneMin = 62000m,
            CorrectionZoneMax = 62500m,
            GoldenPocketMin = 62000m,
            GoldenPocketMax = 62500m,
            Target1 = 64000m,
            Target2 = 65000m,
            InvalidationLevel = 66000m,
            CurrentPricePosition = "BeforeCorrectionZone",
            ConfidenceScore = 80m,
            ValidationStatus = SkScenarioValidator.DirectionMismatch,
            EligibleForBestIdea = false,
            ImpulseStartTimeUtc = DateTime.UtcNow.AddDays(-2),
            ImpulseEndTimeUtc = DateTime.UtcNow.AddDays(-1)
        };

        var summary = await service.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = "BTCUSDT",
            PrimaryTimeframe = "1h",
            HigherTimeframe = "4h",
            CurrentPrice = 63000m,
            MarketBias = "Mixed",
            ConfidenceLabel = "Low",
            Candidates = [BullishCandidate(), invalidBear],
            ExplanationMode = "Beginner",
            PriceDecimals = 2,
            UseAiSummary = true
        });

        Assert.Contains("No clear downward setup detected", summary.BearishScenario);
        Assert.Contains("hidden in Beginner view", summary.AlternativeStructuresNote, StringComparison.OrdinalIgnoreCase);
    }
}
