using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.TradingSystems;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C6D1LimitContractIntegrationTests
    : IClassFixture<DisposableIntegrationDatabaseFixture>
{
    private static readonly DateTime Start = new(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
    private readonly DisposableIntegrationDatabaseFixture _fixture;

    public Milestone231B1C6D1LimitContractIntegrationTests(DisposableIntegrationDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task MySqlRepositories_EnforceCapsFallbacksOrderingAndFilters()
    {
        const long candidateSessionId = 9_231_601;
        const long eventSessionId = 9_231_602;
        const string strategyCode = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT";
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        db.SkLivePaperSessions.AddRange(Enumerable.Range(1, 205).Select(i => Session(i)));
        db.SkLivePaperCandidates.AddRange(Enumerable.Range(1, 505).Select(i => Candidate(candidateSessionId, i))
            .Append(Candidate(candidateSessionId + 1, 9999)));
        db.SkLivePaperEvents.AddRange(Enumerable.Range(1, 1005).Select(i => Event(eventSessionId, i))
            .Append(Event(eventSessionId + 1, 9999)));
        db.StrategyLabRuns.AddRange(Enumerable.Range(1, 205).Select(i => Run(strategyCode, i))
            .Append(Run("MOMO_VOLATILITY_RANGE_REVERSION", 9999)));
        await db.SaveChangesAsync();

        var sessions = new SkLivePaperSessionRepository(db);
        var candidates = new SkLivePaperCandidateRepository(db);
        var events = new SkLivePaperEventRepository(db);
        var runs = new StrategyLabRunRepository(db);

        var cappedSessions = await sessions.GetRecentAsync(int.MaxValue);
        var fallbackSessions = await sessions.GetRecentAsync(0);
        Assert.Equal(200, cappedSessions.Count);
        Assert.Equal(50, fallbackSessions.Count);
        AssertDescending(cappedSessions.Select(item => item.CreatedAtUtc));

        var cappedCandidates = await candidates.GetBySessionAsync(candidateSessionId, int.MaxValue);
        var fallbackCandidates = await candidates.GetBySessionAsync(candidateSessionId, -1);
        Assert.Equal(500, cappedCandidates.Count);
        Assert.Equal(100, fallbackCandidates.Count);
        Assert.All(cappedCandidates, item => Assert.Equal(candidateSessionId, item.SessionId));
        AssertDescending(cappedCandidates.Select(item => item.CreatedAtUtc));

        var cappedEvents = await events.GetBySessionAsync(eventSessionId, int.MaxValue);
        var fallbackEvents = await events.GetBySessionAsync(eventSessionId, 0);
        Assert.Equal(1000, cappedEvents.Count);
        Assert.Equal(200, fallbackEvents.Count);
        Assert.All(cappedEvents, item => Assert.Equal(eventSessionId, item.SessionId));
        AssertDescending(cappedEvents.Select(item => item.CreatedAtUtc));

        var cappedRecentRuns = await runs.GetRecentAsync(int.MaxValue);
        var fallbackRecentRuns = await runs.GetRecentAsync(-1);
        Assert.Equal(200, cappedRecentRuns.Count);
        Assert.Equal(50, fallbackRecentRuns.Count);
        AssertDescending(cappedRecentRuns.Select(item => item.CreatedAtUtc));

        var cappedStrategyRuns = await runs.GetByStrategyCodeAsync(strategyCode, int.MaxValue);
        var fallbackStrategyRuns = await runs.GetByStrategyCodeAsync(strategyCode, 0);
        Assert.Equal(200, cappedStrategyRuns.Count);
        Assert.Equal(20, fallbackStrategyRuns.Count);
        Assert.All(cappedStrategyRuns, item => Assert.Equal(strategyCode, item.StrategyCode));
        AssertDescending(cappedStrategyRuns.Select(item => item.CreatedAtUtc));
    }

    private static SkLivePaperSession Session(int number) => new()
    {
        SessionName = $"limit-session-{number}",
        Symbol = "BTCUSDT",
        CreatedAtUtc = Start.AddMinutes(number)
    };

    private static SkLivePaperCandidate Candidate(long sessionId, int number) => new()
    {
        SessionId = sessionId,
        CandidateKey = $"limit-candidate-{sessionId}-{number}",
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
        EventType = "LimitContract",
        Message = $"event-{number}",
        CreatedAtUtc = Start.AddMinutes(number)
    };

    private static StrategyLabRun Run(string strategyCode, int number) => new()
    {
        Name = $"limit-run-{strategyCode}-{number}",
        StrategyCode = strategyCode,
        StrategyVersion = "1.0.0",
        Symbol = "BTCUSDT",
        Timeframe = "1h",
        FromUtc = Start,
        ToUtc = Start.AddDays(1),
        ExperimentFingerprint = $"limit-experiment-{strategyCode}-{number}",
        AppVersion = "test",
        CandleDatasetFingerprint = $"limit-dataset-{strategyCode}-{number}",
        StrategyCodeFingerprint = $"limit-strategy-{strategyCode}-{number}",
        CreatedAtUtc = Start.AddMinutes(number)
    };

    private static void AssertDescending(IEnumerable<DateTime> timestamps) =>
        Assert.True(timestamps.SequenceEqual(timestamps.OrderByDescending(timestamp => timestamp)));
}
