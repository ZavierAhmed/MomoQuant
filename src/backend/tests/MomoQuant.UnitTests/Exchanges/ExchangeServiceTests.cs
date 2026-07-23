using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Exchanges;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.UnitTests.Exchanges;

public class ExchangeServiceTests
{
    [Fact]
    public async Task CreateExchangeAsync_RejectsDuplicateCode()
    {
        var repository = new Mock<IExchangeRepository>();
        repository.Setup(repo => repo.CodeExistsAsync("BINANCE_FUTURES", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new ExchangeService(
            repository.Object,
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IExchangeConnectivityProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.CreateExchangeAsync(new CreateExchangeRequest
        {
            Name = "Binance Futures",
            Code = "BINANCE_FUTURES",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("code", result.ErrorField);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSimulatedResult()
    {
        var exchange = new Exchange
        {
            Id = 1,
            Name = "Binance Futures",
            Code = "BINANCE_FUTURES",
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com"
        };

        var repository = new Mock<IExchangeRepository>();
        repository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchange);

        var connectivityProvider = new Mock<IExchangeConnectivityProvider>();
        connectivityProvider.Setup(provider => provider.TestConnectionAsync(
                exchange.Code,
                exchange.BaseUrl,
                exchange.WebSocketUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeConnectivityResult
            {
                Success = true,
                RestLatencyMs = 25,
                WebSocketAvailable = true
            });

        var auditService = new Mock<IAuditService>();

        var service = new ExchangeService(
            repository.Object,
            Mock.Of<ISymbolRepository>(),
            connectivityProvider.Object,
            Mock.Of<ICurrentUserService>(),
            auditService.Object);

        var result = await service.TestConnectionAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(25, result.Data?.RestLatencyMs);
        Assert.True(result.Data?.WebSocketAvailable);

        auditService.Verify(
            audit => audit.LogAsync(
                "EXCHANGE_CONNECTIVITY_TESTED",
                nameof(Exchange),
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
    public async Task DeleteExchangeAsync_RejectsWhenDependenciesExist()
    {
        var exchange = new Exchange
        {
            Id = 3,
            Name = "Test Exchange",
            Code = "TEST_EXCHANGE",
            BaseUrl = "https://example.com",
            WebSocketUrl = "wss://example.com"
        };

        var repository = new Mock<IExchangeRepository>();
        repository.Setup(repo => repo.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);
        repository.Setup(repo => repo.HasBlockingDependenciesAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = new ExchangeService(
            repository.Object,
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IExchangeConnectivityProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.DeleteExchangeAsync(3);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DeleteExchangeAsync_DeletesSymbolsThenExchange()
    {
        var exchange = new Exchange
        {
            Id = 4,
            Name = "Disposable Exchange",
            Code = "DISPOSABLE",
            BaseUrl = "https://example.com",
            WebSocketUrl = "wss://example.com"
        };

        var repository = new Mock<IExchangeRepository>();
        repository.Setup(repo => repo.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(exchange);
        repository.Setup(repo => repo.HasBlockingDependenciesAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(repo => repo.DeleteAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.DeleteByExchangeIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var auditService = new Mock<IAuditService>();

        var service = new ExchangeService(
            repository.Object,
            symbolRepository.Object,
            Mock.Of<IExchangeConnectivityProvider>(),
            Mock.Of<ICurrentUserService>(),
            auditService.Object);

        var result = await service.DeleteExchangeAsync(4);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data?.SymbolsDeleted);
        symbolRepository.Verify(repo => repo.DeleteByExchangeIdAsync(4, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(repo => repo.DeleteAsync(4, It.IsAny<CancellationToken>()), Times.Once);
    }
}
