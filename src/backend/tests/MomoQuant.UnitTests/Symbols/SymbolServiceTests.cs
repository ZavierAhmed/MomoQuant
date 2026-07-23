using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Symbols;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.UnitTests.Symbols;

public class SymbolServiceTests
{
    [Fact]
    public async Task SyncSymbolsAsync_CreatesTopFiveSymbols()
    {
        var exchange = new Exchange
        {
            Id = 1,
            Code = "BINANCE_FUTURES",
            Name = "Binance Futures",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByExchangeAndNameAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Symbol?)null);

        var symbolProvider = new Mock<IExchangeSymbolProvider>();
        symbolProvider.Setup(provider => provider.GetSymbolsAsync(exchange.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExchangeSymbolDefinition>
            {
                CreateDefinition("BTCUSDT"),
                CreateDefinition("ETHUSDT"),
                CreateDefinition("SOLUSDT"),
                CreateDefinition("BNBUSDT"),
                CreateDefinition("XRPUSDT")
            });

        var auditService = new Mock<IAuditService>();

        var service = new SymbolService(
            symbolRepository.Object,
            exchangeRepository.Object,
            symbolProvider.Object,
            Mock.Of<ICurrentUserService>(),
            auditService.Object);

        var result = await service.SyncSymbolsAsync(new SyncSymbolsRequest { ExchangeId = 1 });

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Data?.CreatedCount);
        Assert.Equal(0, result.Data?.UpdatedCount);
        Assert.Equal(5, result.Data?.TotalCount);

        symbolRepository.Verify(repo => repo.AddAsync(It.IsAny<Symbol>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
        symbolRepository.Verify(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        auditService.Verify(
            audit => audit.LogAsync(
                "SYMBOLS_SYNCED",
                nameof(Symbol),
                1,
                null,
                null,
                It.IsAny<string?>(),
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncSymbolsAsync_UpdatesExistingSymbolsInsteadOfDuplicating()
    {
        var exchange = new Exchange
        {
            Id = 1,
            Code = "BINANCE_FUTURES",
            Name = "Binance Futures",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };

        var existing = new Symbol
        {
            Id = 10,
            ExchangeId = 1,
            SymbolName = "BTCUSDT",
            BaseAsset = "BTC",
            QuoteAsset = "USDT",
            ContractType = ContractType.Perpetual,
            PricePrecision = 2,
            QuantityPrecision = 3,
            MinQty = 0.001m,
            MinNotional = 5m,
            TickSize = 0.10m,
            StepSize = 0.001m,
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0004m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchange);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByExchangeAndNameAsync(1, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        symbolRepository.Setup(repo => repo.GetByExchangeAndNameAsync(
                1,
                It.Is<string>(name => name != "BTCUSDT"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Symbol?)null);

        var symbolProvider = new Mock<IExchangeSymbolProvider>();
        symbolProvider.Setup(provider => provider.GetSymbolsAsync(exchange.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExchangeSymbolDefinition> { CreateDefinition("BTCUSDT") });

        var service = new SymbolService(
            symbolRepository.Object,
            exchangeRepository.Object,
            symbolProvider.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.SyncSymbolsAsync(new SyncSymbolsRequest { ExchangeId = 1 });

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data?.CreatedCount);
        Assert.Equal(1, result.Data?.UpdatedCount);

        symbolRepository.Verify(repo => repo.AddAsync(It.IsAny<Symbol>(), It.IsAny<CancellationToken>()), Times.Never);
        symbolRepository.Verify(repo => repo.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ExchangeSymbolDefinition CreateDefinition(string symbol) => new()
    {
        Symbol = symbol,
        BaseAsset = symbol[..^4],
        QuoteAsset = "USDT",
        ContractType = ContractType.Perpetual,
        PricePrecision = 2,
        QuantityPrecision = 3,
        MinQty = 0.001m,
        MinNotional = 5m,
        TickSize = 0.01m,
        StepSize = 0.001m,
        MakerFeeRate = 0.0002m,
        TakerFeeRate = 0.0004m
    };
}
