using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkMultiTimeframeContextService : ISkMultiTimeframeContextService
{
    private readonly ISwingStructureService _swingStructureService;

    public SkMultiTimeframeContextService(ISwingStructureService swingStructureService)
    {
        _swingStructureService = swingStructureService;
    }

    public SkMultiTimeframeContextDto BuildContext(
        IReadOnlyList<Candle> higherTimeframeCandles,
        string higherTimeframe,
        string primaryBias,
        string sensitivity,
        SkSystemSettings settings)
    {
        if (higherTimeframeCandles is null || higherTimeframeCandles.Count < 10)
        {
            return new SkMultiTimeframeContextDto
            {
                HigherTimeframeBias = "Neutral",
                HigherTimeframeTrendDescription =
                    $"Not enough {higherTimeframe} candles to determine higher timeframe context.",
                ImportantHigherTimeframeLevels = [],
                ConflictWarning = null
            };
        }

        var ordered = higherTimeframeCandles.OrderBy(candle => candle.OpenTimeUtc).ToList();
        var swings = _swingStructureService.DetectSwings(ordered, sensitivity, settings);

        var highs = swings.Where(swing => swing.Type == "High").OrderBy(swing => swing.TimeUtc).ToList();
        var lows = swings.Where(swing => swing.Type == "Low").OrderBy(swing => swing.TimeUtc).ToList();

        var risingHighs = highs.Count >= 2 && highs[^1].Price > highs[^2].Price;
        var risingLows = lows.Count >= 2 && lows[^1].Price > lows[^2].Price;
        var fallingHighs = highs.Count >= 2 && highs[^1].Price < highs[^2].Price;
        var fallingLows = lows.Count >= 2 && lows[^1].Price < lows[^2].Price;

        var window = Math.Min(20, ordered.Count);
        var recent = ordered.Skip(ordered.Count - window).ToList();
        var closeTrendUp = recent[^1].Close > recent[0].Close;

        string bias;
        if (risingHighs && risingLows)
        {
            bias = "Bullish";
        }
        else if (fallingHighs && fallingLows)
        {
            bias = "Bearish";
        }
        else if ((risingHighs || risingLows) && (fallingHighs || fallingLows))
        {
            bias = "Mixed";
        }
        else
        {
            bias = closeTrendUp ? "Bullish" : "Bearish";
            if (highs.Count < 2 && lows.Count < 2)
            {
                bias = "Neutral";
            }
        }

        var description = bias switch
        {
            "Bullish" => $"{higherTimeframe} structure shows higher highs/lows (up-trend context).",
            "Bearish" => $"{higherTimeframe} structure shows lower highs/lows (down-trend context).",
            "Mixed" => $"{higherTimeframe} structure is mixed — no clean trend.",
            _ => $"{higherTimeframe} structure is neutral/undecided."
        };

        var levels = new List<SkKeyLevelDto>();
        if (highs.Count > 0)
        {
            levels.Add(new SkKeyLevelDto
            {
                Label = $"{higherTimeframe} swing high",
                Price = decimal.Round(highs[^1].Price, 8),
                Kind = "Resistance"
            });
        }

        if (lows.Count > 0)
        {
            levels.Add(new SkKeyLevelDto
            {
                Label = $"{higherTimeframe} swing low",
                Price = decimal.Round(lows[^1].Price, 8),
                Kind = "Support"
            });
        }

        string? conflict = null;
        if ((primaryBias == "Bullish" && bias == "Bearish") ||
            (primaryBias == "Bearish" && bias == "Bullish"))
        {
            conflict = "Primary timeframe sequence conflicts with higher timeframe context.";
        }

        return new SkMultiTimeframeContextDto
        {
            HigherTimeframeBias = bias,
            HigherTimeframeTrendDescription = description,
            ImportantHigherTimeframeLevels = levels,
            ConflictWarning = conflict
        };
    }
}
