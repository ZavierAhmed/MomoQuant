using MomoQuant.Application.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>Milestone 23.0E2C1B — contiguous prefix recovery calculator coverage.</summary>
public sealed class Milestone230E2C1BContiguousRecoveryTests
{
    [Fact]
    public void ContiguousSequence_FromSequences1To3_Returns3()
    {
        var result = ValidationAuditContiguousSequenceCalculator.ComputeFromConfirmedSequences([1, 2, 3]);

        Assert.Equal(3, result.LastConfirmedSequence);
        Assert.Equal(3, result.ConfirmedEventCount);
        Assert.False(result.HasGap);
        Assert.Null(result.FirstMissingSequence);
    }

    [Fact]
    public void ContiguousSequence_WithGapAt4_Returns3AndMissing4()
    {
        var result = ValidationAuditContiguousSequenceCalculator.ComputeFromConfirmedSequences([1, 2, 3, 5]);

        Assert.Equal(3, result.LastConfirmedSequence);
        Assert.Equal(3, result.ConfirmedEventCount);
        Assert.True(result.HasGap);
        Assert.Equal(4, result.FirstMissingSequence);
    }

    [Fact]
    public void ConfirmedEventCount_EqualsContiguousCount_NotMaxSequence()
    {
        // MAX(sequence) would be 100; contiguous prefix from 1 is only 2 events.
        var result = ValidationAuditContiguousSequenceCalculator.ComputeFromConfirmedSequences([1, 2, 100]);

        Assert.Equal(2, result.LastConfirmedSequence);
        Assert.Equal(2, result.ConfirmedEventCount);
        Assert.NotEqual(100, result.ConfirmedEventCount);
        Assert.True(result.HasGap);
        Assert.Equal(3, result.FirstMissingSequence);
    }
}
