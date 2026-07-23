using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Risk;

public sealed class PositionSizingService
{
    public PositionSizingResult Calculate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (context.AccountBalance <= 0)
        {
            return PositionSizingResult.Failed("Account balance must be greater than zero.");
        }

        if (!context.SuggestedStopLoss.HasValue)
        {
            if (rules.RequireStopLoss)
            {
                return PositionSizingResult.Failed("Stop loss is required for position sizing.");
            }

            return PositionSizingResult.Failed("Stop loss is missing. Position size cannot be calculated safely.");
        }

        var riskPerUnit = Math.Abs(context.EntryPrice - context.SuggestedStopLoss.Value);
        if (riskPerUnit <= 0)
        {
            return PositionSizingResult.Failed("Stop loss must be different from entry price.");
        }

        var riskAmount = context.AccountBalance * rules.MaxRiskPerTradePercent / 100m;
        var positionSize = decimal.Round(riskAmount / riskPerUnit, 8, MidpointRounding.ToZero);

        if (positionSize <= 0)
        {
            return PositionSizingResult.Failed("Calculated position size is zero.");
        }

        return PositionSizingResult.Success(
            positionSize,
            rules.MaxRiskPerTradePercent,
            riskAmount,
            context.SuggestedStopLoss,
            context.SuggestedTakeProfit);
    }
}

public sealed class PositionSizingResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public decimal? PositionSize { get; init; }
    public decimal? ApprovedRiskPercent { get; init; }
    public decimal? RiskAmount { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }

    public static PositionSizingResult Success(
        decimal positionSize,
        decimal approvedRiskPercent,
        decimal riskAmount,
        decimal? stopLoss,
        decimal? takeProfit) =>
        new()
        {
            Succeeded = true,
            PositionSize = positionSize,
            ApprovedRiskPercent = approvedRiskPercent,
            RiskAmount = riskAmount,
            StopLoss = stopLoss,
            TakeProfit = takeProfit
        };

    public static PositionSizingResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            ErrorMessage = message
        };
}
