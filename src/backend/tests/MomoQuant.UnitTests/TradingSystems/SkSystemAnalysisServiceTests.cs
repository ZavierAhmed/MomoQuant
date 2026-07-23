using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkSystemAnalysisServiceTests
{
    private readonly Mock<ICandleRepository> _candleRepository = new();
    private readonly Mock<IExchangeRepository> _exchangeRepository = new();
    private readonly Mock<ISymbolRepository> _symbolRepository = new();
    private readonly Mock<ITradingSystemAnalysisRepository> _analysisRepository = new();
    private readonly Mock<IMarketDataService> _marketDataService = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuditService> _auditService = new();

    private SkSystemAnalysisService BuildService()
    {
        var swing = new SwingStructureService();
        var sequence = new SkSequenceAnalyzer();
        var context = new SkMultiTimeframeContextService(swing);
        var ai = new SkSystemAiSummaryService(NullLogger<SkSystemAiSummaryService>.Instance);

        return new SkSystemAnalysisService(
            swing,
            sequence,
            context,
            ai,
            _candleRepository.Object,
            _exchangeRepository.Object,
            _symbolRepository.Object,
            _analysisRepository.Object,
            _marketDataService.Object,
            _currentUserService.Object,
            _auditService.Object,
            Options.Create(new SkSystemSettings()),
            NullLogger<SkSystemAnalysisService>.Instance);
    }

    private static Exchange Exchange() => new() { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES" };

    private static Symbol Symbol() => new() { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" };

    [Fact]
    public async Task AnalyzeAsync_UnsupportedTimeframe_Fails()
    {
        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "2h",
            HigherTimeframe = "4h"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("primaryTimeframe", result.ErrorField);
    }

    [Fact]
    public async Task AnalyzeAsync_MissingSymbol_Fails()
    {
        _exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Exchange());
        _symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Symbol?)null);

        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("symbolId", result.ErrorField);
    }

    [Fact]
    public async Task AnalyzeAsync_MissingCandles_ReturnsImportMessage()
    {
        _exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Exchange());
        _symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Symbol());
        _candleRepository
            .Setup(r => r.GetRecentCandlesAsync(1, It.IsAny<Timeframe>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Candle>());

        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            LookbackCandles = 300,
            AutoImportMissingCandles = false
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Required candles are missing. Import data first.", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_WithData_SavesAnalysisAndIsAnalysisOnly()
    {
        var primary = SkTestData.FromPrices(SkTestData.ZigZagPrices(), Timeframe.M15);
        var higher = SkTestData.FromPrices(SkTestData.ZigZagPrices(), Timeframe.H4);

        _exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Exchange());
        _symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Symbol());
        _candleRepository
            .Setup(r => r.GetRecentCandlesAsync(1, Timeframe.M15, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(primary);
        _candleRepository
            .Setup(r => r.GetRecentCandlesAsync(1, Timeframe.H4, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(higher);

        TradingSystemAnalysis? saved = null;
        _analysisRepository
            .Setup(r => r.AddAsync(It.IsAny<TradingSystemAnalysis>(), It.IsAny<CancellationToken>()))
            .Callback<TradingSystemAnalysis, CancellationToken>((entity, _) => saved = entity)
            .Returns(Task.CompletedTask);

        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            LookbackCandles = 300,
            UseAiSummary = true
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.AnalysisOnly);
        Assert.Equal("SK_SYSTEM", result.Data.SystemCode);
        Assert.NotEmpty(result.Data.Candles);

        // The analysis is persisted, and no execution/benchmark/order path is touched.
        _analysisRepository.Verify(r => r.AddAsync(It.IsAny<TradingSystemAnalysis>(), It.IsAny<CancellationToken>()), Times.Once);
        _analysisRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _marketDataService.Verify(
            r => r.ImportCandlesAsync(It.IsAny<MomoQuant.Application.MarketData.Dtos.ImportCandlesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.NotNull(saved);
        Assert.Equal("SK_SYSTEM", saved!.SystemCode);
    }

    [Fact]
    public async Task AnalyzeAsync_Beginner_ReturnsPlainNarrativeAndBestIdeas()
    {
        SetupDataForAnalysis();
        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            LookbackCandles = 300,
            ExplanationMode = "Beginner",
            UseAiSummary = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Beginner", result.Data!.ExplanationMode);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.PlainLanguageSummary));
        Assert.False(string.IsNullOrWhiteSpace(result.Data.BottomLine));
        Assert.NotEmpty(result.Data.GlossaryTerms);
        Assert.NotEmpty(result.Data.DisplayLabels);
        // The synthetic zig-zag contains both directions, so both ideas should be selected.
        Assert.NotNull(result.Data.BestBullishIdea);
        Assert.NotNull(result.Data.BestBearishIdea);

        var combined = string.Join(
            " ",
            result.Data.PlainLanguageSummary,
            result.Data.BottomLine,
            result.Data.BullishScenario,
            result.Data.BearishScenario);
        foreach (var forbidden in new[] { "buy", "sell", "enter now", "guaranteed" })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_Expert_ReturnsTechnicalDiagnostics()
    {
        SetupDataForAnalysis();
        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            LookbackCandles = 300,
            ExplanationMode = "Expert",
            UseAiSummary = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Expert", result.Data!.ExplanationMode);
        Assert.Contains("sequence", result.Data.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Data.Diagnostics.PrimaryCandleCount > 0);
    }

    [Fact]
    public async Task AnalyzeAsync_ChartOverlays_IncludeGroupingMetadata()
    {
        SetupDataForAnalysis();
        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "15m",
            HigherTimeframe = "4h",
            LookbackCandles = 300,
            UseAiSummary = true
        });

        Assert.True(result.Succeeded);
        var overlays = result.Data!.ChartOverlays;
        Assert.NotEmpty(overlays);

        // Every overlay is grouped and typed with a plain-language meaning.
        Assert.All(overlays, overlay =>
        {
            Assert.False(string.IsNullOrWhiteSpace(overlay.GroupName));
            Assert.False(string.IsNullOrWhiteSpace(overlay.LevelType));
            Assert.False(string.IsNullOrWhiteSpace(overlay.PlainLanguageMeaning));
        });

        // Best upward and downward ideas are grouped separately.
        Assert.Contains(overlays, o => o.GroupName == "Best upward idea");
        Assert.Contains(overlays, o => o.GroupName == "Best downward idea");

        // Current price is present, primary, and important.
        var current = Assert.Single(overlays, o => o.LevelType == "CurrentPrice");
        Assert.True(current.IsPrimary);
        Assert.Equal("High", current.Importance);

        // Fibonacci levels are advanced.
        Assert.All(overlays.Where(o => o.Category == "Fibonacci"), o => Assert.True(o.IsAdvanced));

        // Best-setup danger / target / reaction levels are marked important.
        Assert.All(
            overlays.Where(o => (o.IsBestBullish || o.IsBestBearish)
                && o.LevelType is "DangerLevel" or "Target1" or "Target2" or "ReactionZone"),
            o => Assert.Equal("High", o.Importance));
    }

    [Fact]
    public async Task AnalyzeAsync_HigherTimeframeMustBeLongerThanPrimary_Fails()
    {
        _exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Exchange());
        _symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Symbol());

        var service = BuildService();

        var result = await service.AnalyzeAsync(new SkSystemAnalyzeRequest
        {
            ExchangeId = 1,
            SymbolId = 1,
            PrimaryTimeframe = "4h",
            HigherTimeframe = "15m",
            LookbackCandles = 500
        });

        Assert.False(result.Succeeded);
        Assert.Equal("higherTimeframe", result.ErrorField);
    }

    private void SetupDataForAnalysis()
    {
        var primary = SkTestData.FromPrices(SkTestData.ZigZagPrices(), Timeframe.M15);
        var higher = SkTestData.FromPrices(SkTestData.ZigZagPrices(), Timeframe.H4);

        _exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Exchange());
        _symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Symbol());
        _candleRepository
            .Setup(r => r.GetRecentCandlesAsync(1, Timeframe.M15, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(primary);
        _candleRepository
            .Setup(r => r.GetRecentCandlesAsync(1, Timeframe.H4, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(higher);
        _analysisRepository
            .Setup(r => r.AddAsync(It.IsAny<TradingSystemAnalysis>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
