using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Creates a visible candle window over a source series for Strategy Lab evaluation.
/// Production uses a zero-copy prefix view; tests may use an allocating copy for equivalence.
/// </summary>
public interface IStrategyLabCandleWindowFactory
{
    IReadOnlyList<Candle> CreateVisibleWindow(IReadOnlyList<Candle> source, int visibleCount);
}

/// <summary>
/// Production window factory — returns <see cref="CandlePrefixView"/> (no candle list copy).
/// </summary>
public sealed class CandlePrefixViewStrategyLabCandleWindowFactory : IStrategyLabCandleWindowFactory
{
    public IReadOnlyList<Candle> CreateVisibleWindow(IReadOnlyList<Candle> source, int visibleCount) =>
        new CandlePrefixView(source, visibleCount);
}

/// <summary>
/// Reference/test window factory — allocates a new list via Take().ToList() each call.
/// </summary>
public sealed class CopiedListStrategyLabCandleWindowFactory : IStrategyLabCandleWindowFactory
{
    public IReadOnlyList<Candle> CreateVisibleWindow(IReadOnlyList<Candle> source, int visibleCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (visibleCount < 0 || visibleCount > source.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleCount));
        }

        return source.Take(visibleCount).ToList();
    }
}
