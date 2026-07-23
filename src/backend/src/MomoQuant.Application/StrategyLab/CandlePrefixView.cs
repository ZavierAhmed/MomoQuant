using System.Collections;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Zero-copy prefix view over an immutable candle list. Exposes indices [0, VisibleCount).
/// </summary>
public sealed class CandlePrefixView : IReadOnlyList<Candle>
{
    private readonly IReadOnlyList<Candle> _source;
    private int _visibleCount;

    public CandlePrefixView(IReadOnlyList<Candle> source, int visibleCount)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        SetVisibleCount(visibleCount);
    }

    public int Count => _visibleCount;

    public Candle this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_visibleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _source[index];
        }
    }

    public void SetVisibleCount(int visibleCount)
    {
        if (visibleCount < 0 || visibleCount > _source.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleCount));
        }

        _visibleCount = visibleCount;
    }

    public IEnumerator<Candle> GetEnumerator()
    {
        for (var i = 0; i < _visibleCount; i++)
        {
            yield return _source[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
