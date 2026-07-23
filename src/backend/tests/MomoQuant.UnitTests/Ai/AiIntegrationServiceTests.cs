using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Options;

namespace MomoQuant.UnitTests.Ai;

public class AiIntegrationServiceTests
{
    [Fact]
    public async Task DetectRegimeAsync_ReturnsFallback_WhenServiceUnavailableAndFallbackEnabled()
    {
        var aiClient = new Mock<IAiServiceClient>();
        aiClient.Setup(client => client.DetectRegimeAsync(It.IsAny<DetectRegimeRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiClientResult<DetectRegimeResponseDto>.Fail("AI service is unavailable."));

        var service = CreateService(aiClient.Object, enableFallback: true);
        var result = await service.DetectRegimeAsync(new DetectRegimeRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Unknown", result.Data.Regime);
        Assert.Equal(0, result.Data.Confidence);
        Assert.True(result.Data.UsedFallback);
    }

    [Fact]
    public async Task DetectRegimeAsync_ReturnsFailure_WhenServiceUnavailableAndFallbackDisabled()
    {
        var aiClient = new Mock<IAiServiceClient>();
        aiClient.Setup(client => client.DetectRegimeAsync(It.IsAny<DetectRegimeRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiClientResult<DetectRegimeResponseDto>.Fail("AI service is unavailable."));

        var service = CreateService(aiClient.Object, enableFallback: false);
        var result = await service.DetectRegimeAsync(new DetectRegimeRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("AI service is unavailable.", result.ErrorMessage);
    }

    private static AiIntegrationService CreateService(IAiServiceClient aiClient, bool enableFallback)
    {
        var aiDecisionService = new Mock<IAiDecisionService>();
        var strategySignalRepository = new Mock<IStrategySignalRepository>();
        var strategyRepository = new Mock<IStrategyRepository>();
        var symbolRepository = new Mock<ISymbolRepository>();
        var candleRepository = new Mock<ICandleRepository>();
        var indicatorRepository = new Mock<IIndicatorSnapshotRepository>();
        var riskProfileRepository = new Mock<IRiskProfileRepository>();
        var currentUserService = new Mock<ICurrentUserService>();
        var auditService = new Mock<IAuditService>();

        return new AiIntegrationService(
            aiClient,
            aiDecisionService.Object,
            strategySignalRepository.Object,
            strategyRepository.Object,
            symbolRepository.Object,
            candleRepository.Object,
            indicatorRepository.Object,
            riskProfileRepository.Object,
            currentUserService.Object,
            auditService.Object,
            Options.Create(new AiIntegrationOptions
            {
                EnableFallback = enableFallback
            }),
            NullLogger<AiIntegrationService>.Instance);
    }
}
