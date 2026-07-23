using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.TradingSystems;

public interface ISwingStructureService
{
    /// <summary>
    /// Detects meaningful swing highs/lows from closed candles.
    /// Uses only closed candles and does not repaint historical structure.
    /// </summary>
    IReadOnlyList<SwingPointDto> DetectSwings(
        IReadOnlyList<Candle> candles,
        string sensitivity,
        SkSystemSettings settings);
}
