using Microsoft.EntityFrameworkCore;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

namespace MomoQuant.UnitTests.Persistence;

public class PositionRepositoryTests
{
    [Fact]
    public async Task UpdateAsync_WhenDetachedCopyMatchesTrackedEntity_UpdatesWithoutTrackingConflict()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new MomoQuantDbContext(options);
        var repository = new PositionRepository(context);

        var tracked = new Position
        {
            TradingSessionId = 10,
            SymbolId = 1,
            Direction = TradeDirection.Long,
            Quantity = 1m,
            AverageEntryPrice = 100m,
            MarkPrice = 100m,
            UnrealizedPnl = 0m,
            RealizedPnl = 0m,
            Leverage = 1m,
            MarginUsed = 100m,
            Status = PositionStatus.Open,
            OpenedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await repository.AddAsync(tracked);
        await repository.SaveChangesAsync();

        var detached = new Position
        {
            Id = tracked.Id,
            TradingSessionId = tracked.TradingSessionId,
            SymbolId = tracked.SymbolId,
            Direction = tracked.Direction,
            Quantity = 1.5m,
            AverageEntryPrice = tracked.AverageEntryPrice,
            MarkPrice = 105m,
            UnrealizedPnl = 7.5m,
            RealizedPnl = tracked.RealizedPnl,
            Leverage = tracked.Leverage,
            MarginUsed = tracked.MarginUsed,
            Status = PositionStatus.Open,
            OpenedAtUtc = tracked.OpenedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await repository.UpdateAsync(detached);
        await repository.SaveChangesAsync();

        var reloaded = await context.Positions.AsNoTracking().SingleAsync();
        Assert.Equal(105m, reloaded.MarkPrice);
        Assert.Equal(1.5m, reloaded.Quantity);
        Assert.Equal(7.5m, reloaded.UnrealizedPnl);
    }
}
