using MomoQuant.Application.StrategyBenchmarks;

namespace MomoQuant.UnitTests.StrategyBenchmarks;

public class BenchmarkImportRangeChunkerTests
{
    private readonly BenchmarkImportRangeChunker _chunker = new();

    [Fact]
    public void CreateChunks_SplitsRangeLongerThanMaxDays()
    {
        var fromUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc);

        var chunks = _chunker.CreateChunks(fromUtc, toUtc, maxDaysPerChunk: 30);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(fromUtc, chunks[0].FromUtc);
        Assert.True((chunks[0].ToUtc - chunks[0].FromUtc).TotalDays <= 30);
        Assert.True((chunks[1].ToUtc - chunks[1].FromUtc).TotalDays <= 30);
        Assert.Equal(toUtc, chunks[^1].ToUtc);
    }

    [Fact]
    public void CreateChunks_UsesSevenDayChunks_WithoutGapsOrOverlaps()
    {
        var fromUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc);

        var chunks = _chunker.CreateChunks(fromUtc, toUtc, maxDaysPerChunk: 7);

        Assert.True(chunks.Count >= 5);
        Assert.Equal(fromUtc, chunks[0].FromUtc);
        Assert.Equal(toUtc, chunks[^1].ToUtc);

        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.True((chunks[i].ToUtc - chunks[i].FromUtc).TotalDays <= 7);
            Assert.True(chunks[i].FromUtc < chunks[i].ToUtc);
            Assert.Equal(DateTimeKind.Utc, chunks[i].FromUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, chunks[i].ToUtc.Kind);

            if (i == 0)
            {
                continue;
            }

            Assert.Equal(chunks[i - 1].ToUtc.AddTicks(1), chunks[i].FromUtc);
        }
    }

    [Fact]
    public void CreateChunks_EachChunkIsWithinMaxDaysPerImport()
    {
        var fromUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc);
        const int maxDays = 30;

        var chunks = _chunker.CreateChunks(fromUtc, toUtc, maxDays);

        Assert.All(chunks, chunk => Assert.True((chunk.ToUtc - chunk.FromUtc).TotalDays <= maxDays));
    }

    [Fact]
    public void CreateChunks_ReturnsEmpty_WhenFromIsNotBeforeTo()
    {
        var instant = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Empty(_chunker.CreateChunks(instant, instant, 7));
        Assert.Empty(_chunker.CreateChunks(instant.AddDays(1), instant, 7));
    }

    [Theory]
    [InlineData(0, 30, 7)]
    [InlineData(-1, 30, 7)]
    [InlineData(45, 30, 7)]
    [InlineData(7, 30, 7)]
    [InlineData(14, 10, 7)]
    [InlineData(5, 3, 3)]
    public void ResolveChunkDays_FallsBackSafely(int configured, int maxImport, int expected)
    {
        Assert.Equal(expected, BenchmarkImportRangeChunker.ResolveChunkDays(configured, maxImport));
    }
}
