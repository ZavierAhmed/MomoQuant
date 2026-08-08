using Microsoft.EntityFrameworkCore;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.TradingSystems;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

namespace MomoQuant.UnitTests.Persistence;

public sealed class Milestone231B1C6D1LimitRepositoryTests
{
    private static readonly DateTime Start = new(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SkSessions_RepositoryCapsAndFallsBackWithDescendingOrder()
    {
        await using var db = CreateContext();
        db.SkLivePaperSessions.AddRange(Enumerable.Range(1, 205).Select(i => new SkLivePaperSession
        {
            SessionName = $"session-{i}",
            Symbol = "BTCUSDT",
            CreatedAtUtc = Start.AddMinutes(i)
        }));
        await db.SaveChangesAsync();
        var repository = new SkLivePaperSessionRepository(db);

        var capped = await repository.GetRecentAsync(int.MaxValue);
        var fallback = await repository.GetRecentAsync(0);

        Assert.Equal(200, capped.Count);
        Assert.Equal(50, fallback.Count);
        AssertDescending(capped.Select(session => session.CreatedAtUtc));
    }

    [Fact]
    public async Task SkCandidates_RepositoryCapsFallsBackFiltersAndOrders()
    {
        await using var db = CreateContext();
        db.SkLivePaperCandidates.AddRange(Enumerable.Range(1, 505).Select(i => Candidate(7, i))
            .Append(Candidate(8, 9999)));
        await db.SaveChangesAsync();
        var repository = new SkLivePaperCandidateRepository(db);

        var capped = await repository.GetBySessionAsync(7, int.MaxValue);
        var fallback = await repository.GetBySessionAsync(7, -1);

        Assert.Equal(500, capped.Count);
        Assert.Equal(100, fallback.Count);
        Assert.All(capped, candidate => Assert.Equal(7, candidate.SessionId));
        AssertDescending(capped.Select(candidate => candidate.CreatedAtUtc));
    }

    [Fact]
    public async Task SkEvents_RepositoryCapsFallsBackFiltersAndOrders()
    {
        await using var db = CreateContext();
        db.SkLivePaperEvents.AddRange(Enumerable.Range(1, 1005).Select(i => Event(11, i))
            .Append(Event(12, 9999)));
        await db.SaveChangesAsync();
        var repository = new SkLivePaperEventRepository(db);

        var capped = await repository.GetBySessionAsync(11, int.MaxValue);
        var fallback = await repository.GetBySessionAsync(11, 0);

        Assert.Equal(1000, capped.Count);
        Assert.Equal(200, fallback.Count);
        Assert.All(capped, item => Assert.Equal(11, item.SessionId));
        AssertDescending(capped.Select(item => item.CreatedAtUtc));
    }

    [Fact]
    public async Task StrategyLabRecentRuns_RepositoryCapsFallsBackAndOrders()
    {
        await using var db = CreateContext();
        db.StrategyLabRuns.AddRange(Enumerable.Range(1, 205).Select(i => Run("MOMO_ADAPTIVE_MTF_TREND_BREAKOUT", i)));
        await db.SaveChangesAsync();
        var repository = new StrategyLabRunRepository(db);

        var capped = await repository.GetRecentAsync(int.MaxValue);
        var fallback = await repository.GetRecentAsync(-1);

        Assert.Equal(200, capped.Count);
        Assert.Equal(50, fallback.Count);
        AssertDescending(capped.Select(run => run.CreatedAtUtc));
    }

    [Fact]
    public async Task StrategyLabRunsByStrategy_RepositoryCapsFallsBackFiltersAndOrders()
    {
        const string target = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT";
        await using var db = CreateContext();
        db.StrategyLabRuns.AddRange(Enumerable.Range(1, 205).Select(i => Run(target, i))
            .Append(Run("MOMO_VOLATILITY_RANGE_REVERSION", 9999)));
        await db.SaveChangesAsync();
        var repository = new StrategyLabRunRepository(db);

        var capped = await repository.GetByStrategyCodeAsync(target, int.MaxValue);
        var fallback = await repository.GetByStrategyCodeAsync(target, 0);

        Assert.Equal(200, capped.Count);
        Assert.Equal(20, fallback.Count);
        Assert.All(capped, run => Assert.Equal(target, run.StrategyCode));
        AssertDescending(capped.Select(run => run.CreatedAtUtc));
    }

    private static MomoQuantDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase($"limit-contract-{Guid.NewGuid():N}")
            .Options);

    private static SkLivePaperCandidate Candidate(long sessionId, int number) => new()
    {
        SessionId = sessionId,
        CandidateKey = $"candidate-{sessionId}-{number}",
        Symbol = "BTCUSDT",
        HigherTimeframe = "4h",
        PrimaryTimeframe = "1h",
        Direction = "Long",
        SequenceStatus = "Complete",
        ValidityStatus = "Valid",
        UsefulnessStatus = "Useful",
        CreatedAtUtc = Start.AddMinutes(number)
    };

    private static SkLivePaperEvent Event(long sessionId, int number) => new()
    {
        SessionId = sessionId,
        EventType = "Test",
        Message = $"event-{number}",
        CreatedAtUtc = Start.AddMinutes(number)
    };

    private static StrategyLabRun Run(string strategyCode, int number) => new()
    {
        Name = $"run-{strategyCode}-{number}",
        StrategyCode = strategyCode,
        StrategyVersion = "1.0.0",
        Symbol = "BTCUSDT",
        Timeframe = "1h",
        FromUtc = Start,
        ToUtc = Start.AddDays(1),
        ExperimentFingerprint = $"experiment-{number}",
        AppVersion = "test",
        CandleDatasetFingerprint = $"dataset-{number}",
        StrategyCodeFingerprint = $"strategy-{number}",
        CreatedAtUtc = Start.AddMinutes(number)
    };

    private static void AssertDescending(IEnumerable<DateTime> timestamps) =>
        Assert.True(timestamps.SequenceEqual(timestamps.OrderByDescending(timestamp => timestamp)));
}
