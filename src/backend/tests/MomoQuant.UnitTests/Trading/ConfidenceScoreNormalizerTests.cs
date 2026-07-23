using MomoQuant.Application.Common;

namespace MomoQuant.UnitTests.Trading;

public class ConfidenceScoreNormalizerTests
{
    [Theory]
    [MemberData(nameof(NormalizeCases))]
    public void Normalize_ConvertsExpectedValues(decimal? input, decimal expected) =>
        Assert.Equal(expected, ConfidenceScoreNormalizer.Normalize(input));

    public static IEnumerable<object?[]> NormalizeCases() =>
    [
        [0.75m, 75m],
        [1m, 100m],
        [75m, 75m],
        [120m, 100m],
        [-5m, 0m],
        [null, 0m]
    ];

    [Fact]
    public void Format_UsesTwoDecimalPlaces() =>
        Assert.Equal("72.20", ConfidenceScoreNormalizer.Format(72.2m));
}
