using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MomoQuant.Application.Common;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkSystemPdfExportServiceTests
{
    // 1x1 transparent PNG.
    private const string SamplePng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private readonly Mock<ISkSystemAnalysisService> _analysisService = new();

    private SkSystemPdfExportService BuildService() =>
        new(_analysisService.Object, NullLogger<SkSystemPdfExportService>.Instance);

    private static SkSystemAnalysisResultDto SampleResult() => new()
    {
        AnalysisId = 42,
        ExchangeName = "Binance Futures",
        Symbol = "BTCUSDT",
        PrimaryTimeframe = "4h",
        HigherTimeframe = "1d",
        LookbackCandles = 300,
        SwingSensitivity = "Balanced",
        DirectionMode = "Auto",
        ExplanationMode = "Beginner",
        AnalysisTimeUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
        LatestCandleTimeUtc = new DateTime(2026, 7, 6, 8, 0, 0, DateTimeKind.Utc),
        CurrentPrice = 63450.12m,
        PriceDecimals = 2,
        MarketBias = "Bullish",
        ConfidenceLabel = "Medium",
        KeyAreaToWatch = "62,000 – 62,500",
        DangerLevelToWatch = "61,000",
        WhatThisMeans = "Price may pull back into a reaction zone before continuing higher.",
        BottomLine = "Watch how price reacts near the reaction zone. This is analysis only.",
        HigherTimeframeExplanation = "The higher timeframe looks broadly bullish.",
        BullishScenario = "If price holds the reaction zone it could move toward the targets.",
        BearishScenario = "If the danger level breaks, this idea is off.",
        ClarityReasons = new List<string> { "Clean swing structure detected." },
        ClarityWarnings = new List<string> { "Price has not entered the reaction zone yet." },
        BestBullishIdea = new SkIdeaDto
        {
            Direction = "Bullish",
            DirectionLabel = "Possible upward move",
            Status = "Potential",
            StatusLabel = "Possible setup",
            ClarityLabel = "Medium",
            ClarityScore = 62m,
            ReactionZoneText = "62,000 – 62,500",
            StrongReactionZoneText = "62,100 – 62,300",
            DangerLevelText = "61,000",
            Target1 = 65000m,
            Target2 = 67000m,
            PlainExplanation = "If price dips into the zone and turns up, it may aim for the targets.",
            CandidateId = "seq-1"
        },
        GlossaryTerms = new List<SkGlossaryTermDto>
        {
            new() { Term = "Reaction zone", Explanation = "An area where price may turn or pause." }
        },
        KeyLevels = new List<SkKeyLevelDto>
        {
            new() { Label = "Reaction zone low", Price = 62000m, Kind = "Support" }
        },
        SequenceCandidates = new List<SkSequenceCandidateDto>
        {
            new()
            {
                Id = "seq-2",
                Direction = "Bearish",
                Status = "Potential",
                CorrectionZoneMin = 64000m,
                CorrectionZoneMax = 64500m,
                InvalidationLevel = 66000m,
                Target1 = 61000m,
                Target2 = 60000m,
                CurrentPricePosition = "BeforeCorrectionZone",
                ConfidenceScore = 40m
            }
        }
    };

    private void SetupAnalysis(SkSystemAnalysisResultDto result) =>
        _analysisService
            .Setup(s => s.GetAnalysisAsync(result.AnalysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<SkSystemAnalysisResultDto>.Ok(result));

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    [Fact]
    public async Task ExportAsync_MissingAnalysis_Fails()
    {
        _analysisService
            .Setup(s => s.GetAnalysisAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<SkSystemAnalysisResultDto>.Fail("Analysis was not found.", "id"));

        var result = await BuildService().ExportAsync(999, new SkExportPdfRequest());

        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ExportAsync_WithoutImage_ReturnsPdf()
    {
        var analysis = SampleResult();
        SetupAnalysis(analysis);

        var result = await BuildService().ExportAsync(analysis.AnalysisId, new SkExportPdfRequest());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(IsPdf(result.Data!.Content));
    }

    [Fact]
    public async Task ExportAsync_WithImage_ReturnsPdf()
    {
        var analysis = SampleResult();
        SetupAnalysis(analysis);

        var result = await BuildService().ExportAsync(
            analysis.AnalysisId,
            new SkExportPdfRequest { ChartImageBase64 = SamplePng });

        Assert.True(result.Succeeded);
        Assert.True(IsPdf(result.Data!.Content));
    }

    [Fact]
    public async Task ExportAsync_InvalidImage_StillReturnsPdf()
    {
        var analysis = SampleResult();
        SetupAnalysis(analysis);

        var result = await BuildService().ExportAsync(
            analysis.AnalysisId,
            new SkExportPdfRequest { ChartImageBase64 = "data:image/png;base64,not-valid-base64!!!" });

        Assert.True(result.Succeeded);
        Assert.True(IsPdf(result.Data!.Content));
    }

    [Fact]
    public async Task ExportAsync_FileName_ContainsSymbolAndTimeframes()
    {
        var analysis = SampleResult();
        SetupAnalysis(analysis);

        var result = await BuildService().ExportAsync(analysis.AnalysisId, new SkExportPdfRequest());

        Assert.Contains("BTCUSDT", result.Data!.FileName);
        Assert.Contains("4h", result.Data.FileName);
        Assert.Contains("1d", result.Data.FileName);
        Assert.EndsWith(".pdf", result.Data.FileName);
    }

    [Fact]
    public async Task ExportAsync_WithRawDiagnostics_ReturnsPdf()
    {
        var analysis = SampleResult();
        SetupAnalysis(analysis);

        var result = await BuildService().ExportAsync(
            analysis.AnalysisId,
            new SkExportPdfRequest { IncludeRawDiagnostics = true });

        Assert.True(result.Succeeded);
        Assert.True(IsPdf(result.Data!.Content));
    }

    [Fact]
    public void BuildKeyLevelGroups_GroupsBySetup()
    {
        var groups = SkSystemPdfExportService.BuildKeyLevelGroups(SampleResult(), decimals: 2);

        Assert.Contains(groups, g => g.GroupName == "Best upward idea");
        // The sample has one non-best bearish candidate, so an "other structures" group appears.
        Assert.Contains(groups, g => g.GroupName == "Other possible structures");

        var upward = groups.Single(g => g.GroupName == "Best upward idea");
        Assert.Contains(upward.Rows, r => r.LevelName == "Reaction zone");
        Assert.Contains(upward.Rows, r => r.LevelName == "Danger level");
        Assert.Contains(upward.Rows, r => r.LevelName == "Target 1");
        Assert.Contains(upward.Rows, r => r.LevelName == "Target 2");
    }

    [Fact]
    public void BuildLegend_IncludesLineTypeLabels()
    {
        var legend = SkSystemPdfExportService.BuildLegend();

        Assert.Contains(legend, e => e.Label == "Current price");
        Assert.Contains(legend, e => e.Label == "Danger level");
        Assert.Contains(legend, e => e.Label.Contains("Fibonacci"));
        Assert.All(legend, e => Assert.False(string.IsNullOrWhiteSpace(e.Meaning)));
    }

    [Fact]
    public async Task ExportAsync_OnlyReadsAnalysis_NoSideEffects()
    {
        var analysis = SampleResult();
        SetupAnalysis(analysis);

        await BuildService().ExportAsync(analysis.AnalysisId, new SkExportPdfRequest());

        // The exporter must never analyze, delete, or import — it is read-only.
        _analysisService.Verify(
            s => s.GetAnalysisAsync(analysis.AnalysisId, It.IsAny<CancellationToken>()),
            Times.Once);
        _analysisService.Verify(
            s => s.AnalyzeAsync(It.IsAny<SkSystemAnalyzeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _analysisService.Verify(
            s => s.DeleteAnalysisAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _analysisService.Verify(
            s => s.ImportRequiredDataAsync(It.IsAny<SkImportRequiredDataRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
