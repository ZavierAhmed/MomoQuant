using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Admin.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Services;

namespace MomoQuant.UnitTests.Persistence;

public class CleanBaselineServiceTests
{
    private static (CleanBaselineService Service, MomoQuantDbContext Context) BuildService()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new MomoQuantDbContext(options);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns(1);

        var audit = new Mock<IAuditService>();

        var service = new CleanBaselineService(
            context,
            currentUser.Object,
            audit.Object,
            NullLogger<CleanBaselineService>.Instance);

        return (service, context);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfirmationInvalid_FailsWithoutDeleting()
    {
        var (service, context) = BuildService();
        await using var _ = context;

        context.Strategies.Add(new Strategy
        {
            Code = StrategyCode.EmaPullback,
            Name = "EMA Pullback",
            Description = "Test.",
            IsEnabled = true,
            Version = "1.0.0",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var result = await service.ExecuteAsync(new CleanBaselineRequest { Confirmation = "wrong" });

        Assert.False(result.Succeeded);
        Assert.Equal("confirmation", result.ErrorField);
        Assert.Equal(1, await context.Strategies.CountAsync());
    }

    [Fact]
    public async Task PreviewAsync_ReturnsCountsAndFlagsForConfiguredTargets()
    {
        var (service, context) = BuildService();
        await using var _ = context;

        context.Strategies.Add(new Strategy
        {
            Code = StrategyCode.EmaPullback,
            Name = "EMA Pullback",
            Description = "Test.",
            IsEnabled = true,
            Version = "1.0.0",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var result = await service.PreviewAsync(new CleanBaselineRequest
        {
            Confirmation = CleanBaselineRequest.RequiredConfirmation,
            RemoveStrategies = true,
            RemoveSymbols = false
        });

        Assert.True(result.Succeeded);
        var strategies = Assert.Single(result.Data!.Items, item => item.EntityName == "Strategies");
        Assert.Equal(1, strategies.Count);
        Assert.True(strategies.WillDelete);

        var symbols = Assert.Single(result.Data!.Items, item => item.EntityName == "Symbols");
        Assert.False(symbols.WillDelete);
    }
}
