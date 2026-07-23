namespace MomoQuant.Application.Backtesting.Simulation;

/// <summary>
/// Authoritative fully-net trade settlement for simulated execution accounting.
/// </summary>
public sealed class SimulatedTradeSettlement
{
    public const string Version = "SimulatedTradeSettlement/v1";

    public required decimal GrossPricePnl { get; init; }
    public required decimal EntryFee { get; init; }
    public required decimal ExitFee { get; init; }
    public decimal SlippageCost { get; init; }
    public decimal FundingCost { get; init; }
    public decimal OtherCosts { get; init; }
    public required decimal TotalCosts { get; init; }
    public required decimal FullyNetPnl { get; init; }
    public required decimal BalanceBefore { get; init; }
    public required decimal BalanceAfter { get; init; }
    public required DateTime RealizedAtUtc { get; init; }

    public static SimulatedTradeSettlement Create(
        decimal balanceBeforeExitCredit,
        decimal grossPricePnl,
        decimal entryFee,
        decimal exitFee,
        DateTime realizedAtUtc,
        decimal slippageCost = 0m,
        decimal fundingCost = 0m,
        decimal otherCosts = 0m)
    {
        var totalCosts = entryFee + exitFee + slippageCost + fundingCost + otherCosts;
        var fullyNet = grossPricePnl - totalCosts;
        // Entry fee already deducted from balance at open; credit gross - exit (and other close costs).
        var balanceAfter = balanceBeforeExitCredit + grossPricePnl - exitFee - slippageCost - fundingCost - otherCosts;
        return new SimulatedTradeSettlement
        {
            GrossPricePnl = grossPricePnl,
            EntryFee = entryFee,
            ExitFee = exitFee,
            SlippageCost = slippageCost,
            FundingCost = fundingCost,
            OtherCosts = otherCosts,
            TotalCosts = totalCosts,
            FullyNetPnl = fullyNet,
            BalanceBefore = balanceBeforeExitCredit,
            BalanceAfter = balanceAfter,
            RealizedAtUtc = DateTime.SpecifyKind(realizedAtUtc, DateTimeKind.Utc)
        };
    }
}
