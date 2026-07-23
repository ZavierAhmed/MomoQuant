using Microsoft.Extensions.Options;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.MarketData;

public class MarketDataServiceTests
{
    private static IOptions<MarketDataSettings> CreateSettings(string provider = "Fake") =>
        Options.Create(new MarketDataSettings { HistoricalProvider = provider });

    [Fact]
    public async Task ImportCandlesAsync_SkipsDuplicateCandles()
    {
        var exchange = new Exchange
        {
            Id = 1,
            Code = "BINANCE_FUTURES",
            Name = "Binance Futures",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };

        var symbol = new Symbol
        {
            Id = 2,
            ExchangeId = 1,
            SymbolName = "BTCUSDT"
        };

        var openTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var definitions = new List<HistoricalCandleDefinition>
        {
            new()
            {
                OpenTimeUtc = openTime,
                CloseTimeUtc = openTime.AddMinutes(3),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100.5m,
                Volume = 10m,
                QuoteVolume = 1005m,
                TradeCount = 20
            }
        };

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(symbol);

        var provider = new Mock<IHistoricalCandleProvider>();
        provider.Setup(p => p.GetCandlesAsync(
                exchange.Code,
                symbol.SymbolName,
                Timeframe.M3,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(definitions);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetExistingOpenTimesAsync(
                exchange.Id,
                symbol.Id,
                Timeframe.M3,
                It.IsAny<IReadOnlyCollection<DateTime>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<DateTime> { openTime });

        var importRepository = new Mock<IMarketDataImportRepository>();
        importRepository.Setup(repo => repo.AddAsync(It.IsAny<MarketDataImport>(), It.IsAny<CancellationToken>()))
            .Callback<MarketDataImport, CancellationToken>((import, _) => import.Id = 99);

        var service = new MarketDataService(
            candleRepository.Object,
            importRepository.Object,
            exchangeRepository.Object,
            symbolRepository.Object,
            provider.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings());

        var result = await service.ImportCandlesAsync(new ImportCandlesRequest
        {
            ExchangeId = 1,
            SymbolId = 2,
            Timeframe = "3m",
            FromUtc = openTime,
            ToUtc = openTime.AddHours(1)
        });

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data?.TotalReceived);
        Assert.Equal(0, result.Data?.InsertedCount);
        Assert.Equal(1, result.Data?.SkippedDuplicateCount);

        candleRepository.Verify(repo => repo.AddRangeAsync(It.IsAny<IReadOnlyCollection<Candle>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMarketSnapshotAsync_ReturnsLatestCandleAndCount()
    {
        var symbol = new Symbol
        {
            Id = 2,
            ExchangeId = 1,
            SymbolName = "BTCUSDT"
        };

        var latest = new Candle
        {
            Id = 10,
            ExchangeId = 1,
            SymbolId = 2,
            Timeframe = Timeframe.M3,
            OpenTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CloseTimeUtc = new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Utc),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 10m,
            QuoteVolume = 1005m,
            TradeCount = 20,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetLatestCandleAsync(2, Timeframe.M3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);
        candleRepository.Setup(repo => repo.CountCandlesAsync(2, Timeframe.M3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        var service = new MarketDataService(
            candleRepository.Object,
            Mock.Of<IMarketDataImportRepository>(),
            Mock.Of<IExchangeRepository>(),
            symbolRepository.Object,
            Mock.Of<IHistoricalCandleProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings());

        var result = await service.GetMarketSnapshotAsync(2, "3m");

        Assert.True(result.Succeeded);
        Assert.Equal("BTCUSDT", result.Data?.Symbol);
        Assert.Equal(42, result.Data?.CandleCountAvailable);
        Assert.Equal(100.5m, result.Data?.LatestPrice);
        Assert.False(result.Data?.IndicatorsAvailable);
    }

    [Fact]
    public async Task ImportCandlesAsync_WithBinanceProvider_RejectsUnsupportedSymbol()
    {
        var exchange = new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" };
        var symbol = new Symbol { Id = 2, ExchangeId = 1, SymbolName = "DOGEUSDT" };

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var service = new MarketDataService(
            Mock.Of<ICandleRepository>(),
            Mock.Of<IMarketDataImportRepository>(),
            exchangeRepository.Object,
            symbolRepository.Object,
            Mock.Of<IHistoricalCandleProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings("Binance"));

        var result = await service.ImportCandlesAsync(new ImportCandlesRequest
        {
            ExchangeId = 1,
            SymbolId = 2,
            Timeframe = "3m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.False(result.Succeeded);
        Assert.Equal("symbolId", result.ErrorField);
    }

    [Fact]
    public async Task GetDataQualityAsync_CalculatesCoverageAndMissingCounts()
    {
        var exchange = new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" };
        var symbol = new Symbol { Id = 2, ExchangeId = 1, SymbolName = "BTCUSDT" };
        var fromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc);

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetOpenTimesInRangeAsync(1, 2, Timeframe.M3, fromUtc, toUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateTime> { fromUtc, fromUtc.AddMinutes(6) });
        candleRepository.Setup(repo => repo.CountDuplicateKeysInRangeAsync(1, 2, Timeframe.M3, fromUtc, toUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = new MarketDataService(
            candleRepository.Object,
            Mock.Of<IMarketDataImportRepository>(),
            exchangeRepository.Object,
            symbolRepository.Object,
            Mock.Of<IHistoricalCandleProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings());

        var result = await service.GetDataQualityAsync(1, 2, "3m", fromUtc, toUtc);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Data?.ExpectedCandles);
        Assert.Equal(2, result.Data?.TotalCandles);
        Assert.Equal(3, result.Data?.MissingCandles);
        Assert.Equal(40m, result.Data?.CoveragePercent);
        Assert.NotEmpty(result.Data?.Gaps ?? []);
    }

    [Fact]
    public async Task GetDataQualityAsync_Supports4hIntervalSpacing()
    {
        var exchange = new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" };
        var symbol = new Symbol { Id = 2, ExchangeId = 1, SymbolName = "BNBUSDT" };
        var fromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetOpenTimesInRangeAsync(1, 2, Timeframe.H4, fromUtc, toUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateTime> { fromUtc, fromUtc.AddHours(4) });
        candleRepository.Setup(repo => repo.CountDuplicateKeysInRangeAsync(1, 2, Timeframe.H4, fromUtc, toUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = new MarketDataService(
            candleRepository.Object,
            Mock.Of<IMarketDataImportRepository>(),
            exchangeRepository.Object,
            symbolRepository.Object,
            Mock.Of<IHistoricalCandleProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings());

        var result = await service.GetDataQualityAsync(1, 2, "4h", fromUtc, toUtc);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data?.ExpectedCandles);
        Assert.Equal(2, result.Data?.TotalCandles);
        Assert.Equal(0, result.Data?.MissingCandles);
        Assert.Equal(100m, result.Data?.CoveragePercent);
    }

    [Fact]
    public async Task ImportCandlesAsync_RejectsUnsupportedIntervalToken_2m()
    {
        var service = new MarketDataService(
            Mock.Of<ICandleRepository>(),
            Mock.Of<IMarketDataImportRepository>(),
            Mock.Of<IExchangeRepository>(),
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IHistoricalCandleProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings("Binance"));

        var result = await service.ImportCandlesAsync(new ImportCandlesRequest
        {
            ExchangeId = 1,
            SymbolId = 2,
            Timeframe = "2m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.False(result.Succeeded);
        Assert.Equal("timeframe", result.ErrorField);
        Assert.Contains("invalid", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportCandlesAsync_For4h_UsesDuplicateUniquenessChecks()
    {
        var exchange = new Exchange
        {
            Id = 1,
            Code = "BINANCE_FUTURES",
            Name = "Binance Futures",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };

        var symbol = new Symbol
        {
            Id = 2,
            ExchangeId = 1,
            SymbolName = "BNBUSDT"
        };

        var openTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var definitions = new List<HistoricalCandleDefinition>
        {
            new()
            {
                OpenTimeUtc = openTime,
                CloseTimeUtc = openTime.AddHours(4),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100.5m,
                Volume = 10m,
                QuoteVolume = 1005m,
                TradeCount = 20
            }
        };

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(symbol);

        var provider = new Mock<IHistoricalCandleProvider>();
        provider.Setup(p => p.GetCandlesAsync(
                exchange.Code,
                symbol.SymbolName,
                Timeframe.H4,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(definitions);

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetExistingOpenTimesAsync(
                exchange.Id,
                symbol.Id,
                Timeframe.H4,
                It.IsAny<IReadOnlyCollection<DateTime>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<DateTime> { openTime });

        var importRepository = new Mock<IMarketDataImportRepository>();
        importRepository.Setup(repo => repo.AddAsync(It.IsAny<MarketDataImport>(), It.IsAny<CancellationToken>()))
            .Callback<MarketDataImport, CancellationToken>((import, _) => import.Id = 100);

        var service = new MarketDataService(
            candleRepository.Object,
            importRepository.Object,
            exchangeRepository.Object,
            symbolRepository.Object,
            provider.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            CreateSettings("Binance"));

        var result = await service.ImportCandlesAsync(new ImportCandlesRequest
        {
            ExchangeId = 1,
            SymbolId = 2,
            Timeframe = "4h",
            FromUtc = openTime,
            ToUtc = openTime.AddDays(1)
        });

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data?.InsertedCount);
        Assert.Equal(1, result.Data?.SkippedDuplicateCount);
        candleRepository.Verify(repo => repo.GetExistingOpenTimesAsync(
            exchange.Id,
            symbol.Id,
            Timeframe.H4,
            It.IsAny<IReadOnlyCollection<DateTime>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
