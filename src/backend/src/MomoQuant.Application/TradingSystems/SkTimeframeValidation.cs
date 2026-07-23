using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;

namespace MomoQuant.Application.TradingSystems;

public static class SkTimeframeValidation
{
    public const string HigherMustExceedPrimaryMessage = "Higher timeframe must be greater than primary timeframe.";

    public static ServiceResult<bool> ValidateSkPair(string higherTimeframe, string primaryTimeframe)
    {
        if (!TimeframeParser.TryParse(higherTimeframe, out _))
        {
            return ServiceResult<bool>.Fail($"Unsupported higher timeframe: {higherTimeframe}.", "higherTimeframe");
        }

        if (!TimeframeParser.TryParse(primaryTimeframe, out _))
        {
            return ServiceResult<bool>.Fail($"Unsupported primary timeframe: {primaryTimeframe}.", "primaryTimeframe");
        }

        if (!TimeframeParser.IsHigherTimeframe(higherTimeframe, primaryTimeframe))
        {
            return ServiceResult<bool>.Fail(
                $"{HigherMustExceedPrimaryMessage} HTF={higherTimeframe}, LTF={primaryTimeframe}.",
                "timeframe");
        }

        return ServiceResult<bool>.Ok(true);
    }
}
