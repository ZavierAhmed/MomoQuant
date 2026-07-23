using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.TradingSystems;

public interface ISkMultiTimeframeContextService
{
    /// <summary>
    /// Builds higher-timeframe bias/context to frame the primary-timeframe sequence.
    /// Adds a conflict warning if the higher timeframe disagrees with the primary bias.
    /// </summary>
    SkMultiTimeframeContextDto BuildContext(
        IReadOnlyList<Candle> higherTimeframeCandles,
        string higherTimeframe,
        string primaryBias,
        string sensitivity,
        SkSystemSettings settings);
}
