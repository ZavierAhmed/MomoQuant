using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyLab.Risk;

public static class StrategyLabCostModelVersions
{
    public const string V1 = "StrategyLabCostModel/v1";
}

public enum StrategyLabOrderFeeType
{
    Maker = 1,
    Taker = 2
}

public enum FundingCalculationMode
{
    NotEvaluated = 0,
    Zero = 1
}

/// <summary>Immutable cost snapshot persisted per Strategy Lab run.</summary>
public sealed class StrategyLabCostSnapshot
{
    public string CostModelVersion { get; init; } = StrategyLabCostModelVersions.V1;
    public decimal MakerFeeRate { get; init; }
    public decimal TakerFeeRate { get; init; }
    public StrategyLabOrderFeeType EntryOrderType { get; init; } = StrategyLabOrderFeeType.Taker;
    public StrategyLabOrderFeeType ExitOrderType { get; init; } = StrategyLabOrderFeeType.Taker;
    public decimal EntryFeeRateUsed { get; init; }
    public decimal ExitFeeRateUsed { get; init; }
    public decimal SlippageBasisPoints { get; init; }
    public FundingCalculationMode FundingCalculationMode { get; init; } = FundingCalculationMode.NotEvaluated;
    public decimal EstimatedFundingCost { get; init; }

    public static StrategyLabCostSnapshot CreateDefault(decimal makerFeeRate, decimal takerFeeRate, decimal slippageBps = 0m) =>
        new()
        {
            CostModelVersion = StrategyLabCostModelVersions.V1,
            MakerFeeRate = makerFeeRate,
            TakerFeeRate = takerFeeRate,
            EntryOrderType = StrategyLabOrderFeeType.Taker,
            ExitOrderType = StrategyLabOrderFeeType.Taker,
            EntryFeeRateUsed = takerFeeRate,
            ExitFeeRateUsed = takerFeeRate,
            SlippageBasisPoints = slippageBps,
            FundingCalculationMode = FundingCalculationMode.NotEvaluated,
            EstimatedFundingCost = 0m
        };

    public decimal ResolveFeeRate(StrategyLabOrderFeeType orderType) =>
        orderType == StrategyLabOrderFeeType.Maker ? MakerFeeRate : TakerFeeRate;
}

public sealed class ShadowSettlementResult
{
    public decimal TheoreticalEntryPrice { get; init; }
    public decimal EffectiveEntryPrice { get; init; }
    public decimal TheoreticalExitPrice { get; init; }
    public decimal EffectiveExitPrice { get; init; }
    public decimal EntryNotional { get; init; }
    public decimal ExitNotional { get; init; }
    public decimal EntryFee { get; init; }
    public decimal ExitFee { get; init; }
    public decimal EntrySlippageCost { get; init; }
    public decimal ExitSlippageCost { get; init; }
    public decimal TotalSlippageCost { get; init; }
    public decimal EstimatedFundingCost { get; init; }
    public decimal TotalTransactionCosts { get; init; }
    public decimal ShadowGrossPnl { get; init; }
    public decimal ShadowGrossPnlFromR { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public decimal ShadowNetReturnPercent { get; init; }
    public decimal GrossRMultiple { get; init; }
    public ResearchNetResult ShadowNetResult { get; init; }
    public bool GrossPnlAgreementWarning { get; init; }
    public string CostModelVersion { get; init; } = StrategyLabCostModelVersions.V1;
}

/// <summary>
/// Shadow portfolio gross/net PnL with explicit fees and slippage (StrategyLabCostModel/v1).
/// Does not mutate raw candidate Raw* fields.
/// </summary>
public static class ShadowPositionCostCalculator
{
    public static decimal ApplyEntrySlippage(decimal theoreticalEntry, TradeDirection direction, decimal slippageBps)
    {
        if (slippageBps <= 0) return theoreticalEntry;
        var factor = slippageBps / 10000m;
        return direction == TradeDirection.Long
            ? theoreticalEntry * (1m + factor)
            : theoreticalEntry * (1m - factor);
    }

    public static decimal ApplyExitSlippage(decimal theoreticalExit, TradeDirection direction, decimal slippageBps)
    {
        if (slippageBps <= 0) return theoreticalExit;
        var factor = slippageBps / 10000m;
        return direction == TradeDirection.Long
            ? theoreticalExit * (1m - factor)
            : theoreticalExit * (1m + factor);
    }

    public static ShadowSettlementResult Settle(
        TradeDirection direction,
        decimal theoreticalEntry,
        decimal theoreticalExit,
        decimal quantity,
        decimal riskAmountAtEntry,
        decimal? rawRMultiple,
        decimal assessmentBalanceAtEntry,
        StrategyLabCostSnapshot cost)
    {
        var effectiveEntry = ApplyEntrySlippage(theoreticalEntry, direction, cost.SlippageBasisPoints);
        var effectiveExit = ApplyExitSlippage(theoreticalExit, direction, cost.SlippageBasisPoints);

        // Notionals for fees use effective fill prices.
        var entryNotional = Math.Round(quantity * effectiveEntry, 8);
        var exitNotional = Math.Round(quantity * effectiveExit, 8);

        var entryFeeRate = cost.EntryFeeRateUsed > 0 ? cost.EntryFeeRateUsed : cost.ResolveFeeRate(cost.EntryOrderType);
        var exitFeeRate = cost.ExitFeeRateUsed > 0 ? cost.ExitFeeRateUsed : cost.ResolveFeeRate(cost.ExitOrderType);

        var entryFee = Math.Round(entryNotional * entryFeeRate, 8);
        var exitFee = Math.Round(exitNotional * exitFeeRate, 8);

        var entrySlippageCost = Math.Round(Math.Abs(effectiveEntry - theoreticalEntry) * quantity, 8);
        var exitSlippageCost = Math.Round(Math.Abs(effectiveExit - theoreticalExit) * quantity, 8);
        var totalSlippage = entrySlippageCost + exitSlippageCost;

        var funding = cost.FundingCalculationMode == FundingCalculationMode.Zero
            ? 0m
            : cost.EstimatedFundingCost;

        // Gross from theoretical prices (R-consistent). Slippage is a separate cost line.
        var priceGross = direction == TradeDirection.Long
            ? Math.Round(quantity * (theoreticalExit - theoreticalEntry), 8)
            : Math.Round(quantity * (theoreticalEntry - theoreticalExit), 8);

        var rGross = rawRMultiple.HasValue && riskAmountAtEntry > 0
            ? Math.Round(riskAmountAtEntry * rawRMultiple.Value, 8)
            : priceGross;

        var gross = priceGross;
        var disagree = riskAmountAtEntry > 0
            && Math.Abs(priceGross - rGross) > Math.Max(0.01m, riskAmountAtEntry * 0.05m);

        var totalCosts = Math.Round(entryFee + exitFee + totalSlippage + funding, 8);
        var net = Math.Round(gross - entryFee - exitFee - totalSlippage - funding, 8);

        var netReturn = assessmentBalanceAtEntry > 0
            ? Math.Round(net / assessmentBalanceAtEntry * 100m, 6)
            : 0m;

        var grossR = riskAmountAtEntry > 0
            ? Math.Round(gross / riskAmountAtEntry, 6)
            : (rawRMultiple ?? 0m);

        var netResult = net > 0.00000001m
            ? ResearchNetResult.Profitable
            : net < -0.00000001m
                ? ResearchNetResult.Losing
                : ResearchNetResult.Breakeven;

        return new ShadowSettlementResult
        {
            TheoreticalEntryPrice = theoreticalEntry,
            EffectiveEntryPrice = effectiveEntry,
            TheoreticalExitPrice = theoreticalExit,
            EffectiveExitPrice = effectiveExit,
            EntryNotional = entryNotional,
            ExitNotional = exitNotional,
            EntryFee = entryFee,
            ExitFee = exitFee,
            EntrySlippageCost = entrySlippageCost,
            ExitSlippageCost = exitSlippageCost,
            TotalSlippageCost = totalSlippage,
            EstimatedFundingCost = funding,
            TotalTransactionCosts = totalCosts,
            ShadowGrossPnl = gross,
            ShadowGrossPnlFromR = rGross,
            ShadowNetPnl = net,
            ShadowNetReturnPercent = netReturn,
            GrossRMultiple = grossR,
            ShadowNetResult = netResult,
            GrossPnlAgreementWarning = disagree,
            CostModelVersion = cost.CostModelVersion
        };
    }

    /// <summary>
    /// Candidate-level fee-to-target estimate using the same cost snapshot (target-exit assumption).
    /// </summary>
    public static (decimal? FeeToTargetPercent, decimal ExpectedCosts, decimal TargetGross) EstimateFeeToTarget(
        TradeDirection direction,
        decimal theoreticalEntry,
        decimal targetPrice,
        decimal quantity,
        StrategyLabCostSnapshot cost)
    {
        if (quantity <= 0 || theoreticalEntry <= 0) return (null, 0m, 0m);

        var effectiveEntry = ApplyEntrySlippage(theoreticalEntry, direction, cost.SlippageBasisPoints);
        var effectiveTarget = ApplyExitSlippage(targetPrice, direction, cost.SlippageBasisPoints);

        var entryNotional = quantity * effectiveEntry;
        var exitNotional = quantity * effectiveTarget;
        var entryFeeRate = cost.EntryFeeRateUsed > 0 ? cost.EntryFeeRateUsed : cost.ResolveFeeRate(cost.EntryOrderType);
        var exitFeeRate = cost.ExitFeeRateUsed > 0 ? cost.ExitFeeRateUsed : cost.ResolveFeeRate(cost.ExitOrderType);
        var entryFee = entryNotional * entryFeeRate;
        var exitFee = exitNotional * exitFeeRate;
        var entrySlip = Math.Abs(effectiveEntry - theoreticalEntry) * quantity;
        var exitSlip = Math.Abs(effectiveTarget - targetPrice) * quantity;
        // Fee efficiency uses fees + explicit slippage costs (target path).
        var expectedCosts = entryFee + exitFee + entrySlip + exitSlip;
        var targetGross = Math.Abs(targetPrice - theoreticalEntry) * quantity;
        if (targetGross <= 0) return (null, Math.Round(expectedCosts, 8), 0m);

        return (Math.Round(expectedCosts / targetGross * 100m, 6), Math.Round(expectedCosts, 8), Math.Round(targetGross, 8));
    }
}
