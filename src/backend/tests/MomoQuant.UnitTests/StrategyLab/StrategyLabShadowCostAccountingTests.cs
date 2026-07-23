using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public sealed class StrategyLabShadowCostAccountingTests
{
    private static StrategyLabCostSnapshot Cost(decimal taker = 0.0004m, decimal slippageBps = 0m) =>
        StrategyLabCostSnapshot.CreateDefault(0.0002m, taker, slippageBps);

    [Fact]
    public void Stop_hit_produces_minus_one_R_gross()
    {
        var s = ShadowPositionCostCalculator.Settle(
            TradeDirection.Long, 100m, 99m, quantity: 1m, riskAmountAtEntry: 1m, rawRMultiple: -1m,
            assessmentBalanceAtEntry: 100m, Cost(0m));
        Assert.Equal(-1m, s.ShadowGrossPnl);
        Assert.Equal(-1m, s.ShadowNetPnl);
    }

    [Fact]
    public void Two_R_target_produces_plus_two_R_gross()
    {
        var s = ShadowPositionCostCalculator.Settle(
            TradeDirection.Long, 100m, 102m, quantity: 1m, riskAmountAtEntry: 1m, rawRMultiple: 2m,
            assessmentBalanceAtEntry: 100m, Cost(0m));
        Assert.Equal(2m, s.ShadowGrossPnl);
    }

    [Fact]
    public void Fees_are_deducted_exactly_once_from_net()
    {
        var s = ShadowPositionCostCalculator.Settle(
            TradeDirection.Long, 100m, 99m, quantity: 10m, riskAmountAtEntry: 10m, rawRMultiple: -1m,
            assessmentBalanceAtEntry: 1000m, Cost(0.001m));
        // Gross -10; entry fee 10*100*0.001=1; exit fee 10*99*0.001=0.99
        Assert.Equal(-10m, s.ShadowGrossPnl);
        Assert.Equal(1m, s.EntryFee);
        Assert.Equal(0.99m, s.ExitFee);
        Assert.Equal(-11.99m, s.ShadowNetPnl);
    }

    [Fact]
    public void Zero_fee_configuration_produces_zero_fees()
    {
        var s = ShadowPositionCostCalculator.Settle(
            TradeDirection.Long, 100m, 102m, 1m, 2m, 2m, 100m, Cost(0m));
        Assert.Equal(0m, s.EntryFee);
        Assert.Equal(0m, s.ExitFee);
        Assert.Equal(s.ShadowGrossPnl, s.ShadowNetPnl);
    }

    [Fact]
    public void Long_entry_slippage_raises_effective_entry()
    {
        var entry = ShadowPositionCostCalculator.ApplyEntrySlippage(100m, TradeDirection.Long, 10m);
        Assert.True(entry > 100m);
        Assert.Equal(100.1m, entry);
    }

    [Fact]
    public void Long_exit_slippage_lowers_effective_exit()
    {
        var exit = ShadowPositionCostCalculator.ApplyExitSlippage(102m, TradeDirection.Long, 10m);
        Assert.True(exit < 102m);
    }

    [Fact]
    public void Short_entry_slippage_lowers_effective_entry()
    {
        var entry = ShadowPositionCostCalculator.ApplyEntrySlippage(100m, TradeDirection.Short, 10m);
        Assert.Equal(99.9m, entry);
    }

    [Fact]
    public void Short_exit_slippage_raises_effective_exit()
    {
        var exit = ShadowPositionCostCalculator.ApplyExitSlippage(98m, TradeDirection.Short, 10m);
        Assert.Equal(98.098m, exit); // 98 * 1.001
    }

    [Fact]
    public void Zero_slippage_preserves_theoretical_prices()
    {
        var s = ShadowPositionCostCalculator.Settle(
            TradeDirection.Long, 100m, 102m, 1m, 2m, 2m, 100m, Cost(0m, 0m));
        Assert.Equal(100m, s.EffectiveEntryPrice);
        Assert.Equal(102m, s.EffectiveExitPrice);
        Assert.Equal(0m, s.TotalSlippageCost);
    }

    [Fact]
    public void Balance_updates_using_shadow_net_pnl_and_compounds()
    {
        var cost = Cost(0.001m);
        var portfolio = new ChronologicalShadowPortfolio("RiskOnly", 100m, cost);
        var sizing = FuturesSizingCalculator.Calculate(100m, 99m, 102m, 100m, 0.5m, 10m, 10m, 0.001m);
        Assert.NotNull(sizing.Quantity);

        var candidate = new StrategyResearchCandidate
        {
            Id = 1,
            SetupFingerprint = "t1",
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            RawExitPrice = 99m,
            RawRMultiple = -1m,
            ExitOutcome = ResearchExitOutcome.StopHit,
            RewardRisk = 2m
        };

        var entry = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var exit = entry.AddHours(1);
        Assert.True(portfolio.TryOpen(candidate, sizing, entry, exit, 100m));
        portfolio.CloseDuePositions(exit);

        Assert.True(portfolio.CurrentBalance < 100m - sizing.RiskAmount + 0.0001m); // fees make it worse than -risk
        Assert.True(portfolio.TotalTransactionCosts > 0);
        Assert.Equal(portfolio.TotalNetPnl, portfolio.CurrentBalance - 100m);

        // Next risk amount uses net balance
        var next = FuturesSizingCalculator.Calculate(100m, 99m, 102m, portfolio.CurrentBalance, 0.5m, 10m, 10m, 0.001m);
        Assert.True(next.RiskAmount < sizing.RiskAmount);
        Assert.Equal(Math.Round(portfolio.CurrentBalance * 0.5m / 100m, 8), next.RiskAmount);
    }

    [Fact]
    public void Daily_loss_equality_blocks_with_limit_reached_message()
    {
        var comparison = RiskLimitComparison.IsBelowExclusiveMaximum(2.00m, 2.00m);
        Assert.False(comparison);
        Assert.True(RiskLimitComparison.IsBelowExclusiveMaximum(1.99m, 2.00m));
        Assert.True(RiskLimitComparison.IsWithinInclusiveMaximum(2.00m, 2.00m));
        Assert.True(RiskLimitComparison.MeetsInclusiveMinimum(50m, 50m));
    }

    [Fact]
    public void Failed_rule_cannot_say_within_limit()
    {
        var issues = ChronologicalShadowProcessor.ValidateRuleConsistency(
        [
            new RiskRuleResultDto
            {
                RuleKey = "MaxDailyLossPercent",
                Status = nameof(RiskRuleResultStatus.Failed),
                Severity = nameof(RiskRuleSeverity.HardReject),
                ActualValue = 2m,
                LimitValue = 2m,
                Reason = "Daily loss usage 2 within limit 2."
            }
        ]);
        Assert.Contains(issues, i => i.Contains("RiskRuleStatusValueMismatch"));
    }

    [Fact]
    public void Target_hit_with_high_fees_can_be_net_losing()
    {
        // Qty large, tiny target, high fee → TargetHit gross positive, net negative
        var s = ShadowPositionCostCalculator.Settle(
            TradeDirection.Long,
            theoreticalEntry: 100m,
            theoreticalExit: 100.01m,
            quantity: 1000m,
            riskAmountAtEntry: 10m,
            rawRMultiple: 1m,
            assessmentBalanceAtEntry: 10000m,
            Cost(0.001m));
        Assert.True(s.ShadowGrossPnl > 0);
        Assert.True(s.ShadowNetPnl < 0);
        Assert.Equal(ResearchNetResult.Losing, s.ShadowNetResult);
    }
}
