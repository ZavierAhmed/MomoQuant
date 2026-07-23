using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public interface IExternalLiquidityLineEngine
{
    IReadOnlyList<LiquidityLevelDto> CalculateLiquidityLevels(
        IReadOnlyList<Candle> candles,
        string timeframe,
        BbLiquiditySweepParameters? parameters = null,
        int smoothingA = 7,
        int smoothingB = 3);

    IReadOnlyList<LiquidityLevelDto> CalculateLtfLiquidityLevels(
        IReadOnlyList<Candle> candles,
        string timeframe,
        BbLiquiditySweepParameters? parameters = null,
        int smoothingA = 5,
        int smoothingB = 1);

    ExternalLiquidityEngineInfoDto GetImplementationInfo();
}
