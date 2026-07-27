namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Computes the highest contiguous payload-confirmed prefix beginning at sequence 1.
/// Never uses MAX(batch.LastSequence) — only contiguous evidence from sequence 1 counts.
/// </summary>
public static class ValidationAuditContiguousSequenceCalculator
{
    public sealed record ContiguousPrefixResult(
        long LastConfirmedSequence,
        int ConfirmedEventCount,
        long? FirstMissingSequence,
        bool HasGap);

    /// <summary>
    /// Given confirmed sequence numbers (each mapped to a payload-verified event),
    /// returns the longest contiguous prefix starting at 1.
    /// </summary>
    public static ContiguousPrefixResult ComputeFromConfirmedSequences(IEnumerable<long> confirmedSequences)
    {
        var set = confirmedSequences.OrderBy(s => s).ToHashSet();
        if (set.Count == 0)
        {
            return new ContiguousPrefixResult(0, 0, 1, false);
        }

        long last = 0;
        foreach (var seq in set.OrderBy(s => s))
        {
            if (seq != last + 1)
            {
                return new ContiguousPrefixResult(last, (int)last, last + 1, true);
            }

            last = seq;
        }

        return new ContiguousPrefixResult(last, (int)last, null, false);
    }
}
