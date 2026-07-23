namespace MomoQuant.Application.StrategyBenchmarks;

public interface IBenchmarkImportRangeChunker
{
    IReadOnlyList<BenchmarkImportChunk> CreateChunks(DateTime fromUtc, DateTime toUtc, int maxDaysPerChunk);
}

public sealed class BenchmarkImportChunk
{
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
}

public sealed class BenchmarkImportRangeChunker : IBenchmarkImportRangeChunker
{
    public IReadOnlyList<BenchmarkImportChunk> CreateChunks(DateTime fromUtc, DateTime toUtc, int maxDaysPerChunk)
    {
        fromUtc = NormalizeUtc(fromUtc);
        toUtc = NormalizeUtc(toUtc);

        if (fromUtc >= toUtc)
        {
            return [];
        }

        var chunkDays = Math.Max(maxDaysPerChunk, 1);
        var chunks = new List<BenchmarkImportChunk>();
        var cursor = fromUtc;

        while (cursor < toUtc)
        {
            var remainingDays = (toUtc - cursor).TotalDays;
            DateTime chunkToUtc;
            if (remainingDays <= chunkDays)
            {
                chunkToUtc = toUtc;
            }
            else
            {
                // Inclusive end just before the next chunk start (no gaps, no overlaps).
                chunkToUtc = cursor.AddDays(chunkDays).AddTicks(-1);
                if (chunkToUtc >= toUtc)
                {
                    chunkToUtc = toUtc;
                }
            }

            // Safety: never exceed maxDaysPerChunk even with tick rounding.
            if ((chunkToUtc - cursor).TotalDays > chunkDays)
            {
                chunkToUtc = cursor.AddDays(chunkDays);
                if (chunkToUtc > toUtc)
                {
                    chunkToUtc = toUtc;
                }
            }

            if (chunkToUtc <= cursor)
            {
                chunkToUtc = toUtc;
            }

            chunks.Add(new BenchmarkImportChunk
            {
                FromUtc = cursor,
                ToUtc = chunkToUtc
            });

            if (chunkToUtc >= toUtc)
            {
                break;
            }

            cursor = chunkToUtc.AddTicks(1);
        }

        return chunks;
    }

    public static int ResolveChunkDays(int configuredChunkDays, int maxDaysPerImport)
    {
        var maxAllowed = Math.Max(maxDaysPerImport, 1);
        if (configuredChunkDays <= 0 || configuredChunkDays > maxAllowed)
        {
            return Math.Min(7, maxAllowed);
        }

        return configuredChunkDays;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
