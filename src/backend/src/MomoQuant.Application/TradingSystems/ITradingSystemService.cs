using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public interface ITradingSystemService
{
    /// <summary>
    /// Returns the available Trading Systems. Trading Systems are analytical frameworks only.
    /// </summary>
    IReadOnlyList<TradingSystemInfoDto> GetSystems();
}
