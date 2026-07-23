using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab.Risk;

/// <summary>
/// Event-driven chronological shadow portfolio for Strategy Lab risk observation.
/// Positions remain open until RawExitAtUtc. Balance updates use ShadowNetPnl (gross − costs).
/// Drawdown mode: RealizedOnly. Daily loss denominator: DailyStartingEquity at UTC day open.
/// </summary>
public sealed class ChronologicalShadowPortfolio
{
    public string PathName { get; }
    public decimal InitialBalance { get; }
    public decimal CurrentBalance { get; private set; }
    public decimal PeakRealizedEquity { get; private set; }
    public DateTime? CurrentUtcTradingDate { get; private set; }
    public decimal DailyStartingEquity { get; private set; }
    public decimal DailyRealizedPnl { get; private set; }
    public DrawdownCalculationMode DrawdownMode { get; } = DrawdownCalculationMode.RealizedOnly;
    public List<ShadowPosition> OpenPositions { get; } = [];
    public List<ShadowTradeLedgerEntry> ClosedTrades { get; } = [];
    public List<ShadowPortfolioAuditEvent> AuditEvents { get; } = [];
    public int AcceptedCount { get; private set; }
    public int RejectedCount { get; private set; }
    public decimal PeakMarginUsagePercent { get; private set; }
    public decimal PeakNotionalExposurePercent { get; private set; }
    public decimal PeakConcurrentRiskPercent { get; private set; }
    public int PeakOpenPositionCount { get; private set; }
    public decimal MaxRealizedDrawdownPercent { get; private set; }

    public decimal TotalGrossPnl { get; private set; }
    public decimal TotalEntryFees { get; private set; }
    public decimal TotalExitFees { get; private set; }
    public decimal TotalSlippageCost { get; private set; }
    public decimal TotalFundingCost { get; private set; }
    public decimal TotalTransactionCosts { get; private set; }
    public decimal TotalNetPnl { get; private set; }
    public int ProfitableTrades { get; private set; }
    public int LosingTrades { get; private set; }
    public int BreakevenTrades { get; private set; }

    private readonly StrategyLabCostSnapshot _cost;

    public ChronologicalShadowPortfolio(string pathName, decimal initialBalance, StrategyLabCostSnapshot cost)
    {
        PathName = pathName;
        InitialBalance = initialBalance;
        CurrentBalance = initialBalance;
        PeakRealizedEquity = initialBalance;
        DailyStartingEquity = initialBalance;
        _cost = cost;
    }

    public decimal CurrentNotional => OpenPositions.Sum(p => p.EntryNotional);
    public decimal CurrentMargin => OpenPositions.Sum(p => p.InitialMargin);
    public decimal ConcurrentRiskAtStop => OpenPositions.Sum(p => p.RiskAmountAtEntry);

    public decimal? CurrentNotionalExposurePercent =>
        CurrentBalance > 0 ? Math.Round(CurrentNotional / CurrentBalance * 100m, 6) : null;

    public decimal? CurrentMarginUsagePercent =>
        CurrentBalance > 0 ? Math.Round(CurrentMargin / CurrentBalance * 100m, 6) : null;

    public decimal? ConcurrentRiskPercent =>
        CurrentBalance > 0 ? Math.Round(ConcurrentRiskAtStop / CurrentBalance * 100m, 6) : null;

    public decimal? CurrentDrawdownPercent =>
        PeakRealizedEquity > 0
            ? Math.Round((PeakRealizedEquity - CurrentBalance) / PeakRealizedEquity * 100m, 6)
            : null;

    /// <summary>
    /// Daily loss usage vs DailyStartingEquity (balance at UTC day open). Uses net realized daily PnL.
    /// </summary>
    public decimal? DailyLossUsagePercent
    {
        get
        {
            if (DailyStartingEquity <= 0 || DailyRealizedPnl >= 0) return 0m;
            var lossAmount = Math.Max(0m, -DailyRealizedPnl);
            return Math.Round(lossAmount / DailyStartingEquity * 100m, 6);
        }
    }

    public void AdvanceTo(DateTime utc)
    {
        var day = DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
        if (CurrentUtcTradingDate is null)
        {
            CurrentUtcTradingDate = day;
            DailyStartingEquity = CurrentBalance;
            DailyRealizedPnl = 0m;
            AddAudit("UtcDayReset", utc, null, CurrentBalance, CurrentBalance, OpenPositions.Count, OpenPositions.Count,
                $"Trading date initialized to {day:yyyy-MM-dd} UTC.");
            return;
        }

        if (day > CurrentUtcTradingDate.Value)
        {
            var before = CurrentBalance;
            var openBefore = OpenPositions.Count;
            CurrentUtcTradingDate = day;
            DailyStartingEquity = CurrentBalance;
            DailyRealizedPnl = 0m;
            AddAudit("UtcDayReset", utc, null, before, CurrentBalance, openBefore, OpenPositions.Count,
                $"Daily loss reset at {day:yyyy-MM-dd} UTC. DailyStartingEquity={DailyStartingEquity:0.####}.");
        }
    }

    public void CloseDuePositions(DateTime atOrBeforeUtc)
    {
        var due = OpenPositions
            .Where(p => p.ExpectedExitAtUtc <= atOrBeforeUtc)
            .OrderBy(p => p.ExpectedExitAtUtc)
            .ThenBy(p => p.SetupFingerprint, StringComparer.Ordinal)
            .ToList();

        foreach (var pos in due)
        {
            AdvanceTo(pos.ExpectedExitAtUtc);
            ClosePosition(pos);
        }
    }

    public void ClosePosition(ShadowPosition pos)
    {
        if (!OpenPositions.Remove(pos)) return;

        var openBefore = OpenPositions.Count + 1;
        var balanceBefore = CurrentBalance;
        var theoreticalExit = pos.TheoreticalExitPrice ?? pos.ExitPrice ?? pos.TheoreticalEntryPrice;
        var settlement = ShadowPositionCostCalculator.Settle(
            pos.Direction,
            pos.TheoreticalEntryPrice,
            theoreticalExit,
            pos.Quantity,
            pos.RiskAmountAtEntry,
            pos.RawRMultiple,
            pos.AssessmentBalanceAtEntry,
            _cost);

        ApplySettlement(pos, settlement, balanceBefore);

        CurrentBalance += settlement.ShadowNetPnl;
        DailyRealizedPnl += settlement.ShadowNetPnl;
        PeakRealizedEquity = Math.Max(PeakRealizedEquity, CurrentBalance);
        if (CurrentDrawdownPercent is { } dd)
        {
            MaxRealizedDrawdownPercent = Math.Max(MaxRealizedDrawdownPercent, dd);
        }

        pos.CurrentStatus = ShadowPositionStatus.Closed;
        pos.RealizedNetPnl = settlement.ShadowNetPnl;
        pos.Settlement = settlement;
        pos.BalanceBeforeExit = balanceBefore;
        pos.BalanceAfterExit = CurrentBalance;
        pos.RealizedAtUtc = pos.ExpectedExitAtUtc;

        TotalGrossPnl += settlement.ShadowGrossPnl;
        TotalEntryFees += settlement.EntryFee;
        TotalExitFees += settlement.ExitFee;
        TotalSlippageCost += settlement.TotalSlippageCost;
        TotalFundingCost += settlement.EstimatedFundingCost;
        TotalTransactionCosts += settlement.TotalTransactionCosts;
        TotalNetPnl += settlement.ShadowNetPnl;

        switch (settlement.ShadowNetResult)
        {
            case ResearchNetResult.Profitable: ProfitableTrades++; break;
            case ResearchNetResult.Losing: LosingTrades++; break;
            default: BreakevenTrades++; break;
        }

        ClosedTrades.Add(ShadowTradeLedgerEntry.From(pos, settlement, balanceBefore, CurrentBalance, CurrentDrawdownPercent));
        AddAudit("PositionExited", pos.ExpectedExitAtUtc, pos.CandidateId, balanceBefore, CurrentBalance, openBefore, OpenPositions.Count,
            $"NetPnL={settlement.ShadowNetPnl:0.####} Gross={settlement.ShadowGrossPnl:0.####} Costs={settlement.TotalTransactionCosts:0.####}");
        AddAudit("BalanceUpdated", pos.ExpectedExitAtUtc, pos.CandidateId, balanceBefore, CurrentBalance, OpenPositions.Count, OpenPositions.Count,
            "Balance updated from ShadowNetPnl.");
        AddAudit("DailyLossUpdated", pos.ExpectedExitAtUtc, pos.CandidateId, CurrentBalance, CurrentBalance, OpenPositions.Count, OpenPositions.Count,
            $"DailyRealizedPnl={DailyRealizedPnl:0.####} DailyLossUsage={DailyLossUsagePercent:0.####}%");
        AddAudit("DrawdownUpdated", pos.ExpectedExitAtUtc, pos.CandidateId, CurrentBalance, CurrentBalance, OpenPositions.Count, OpenPositions.Count,
            $"CurrentDrawdown={CurrentDrawdownPercent:0.####}% Max={MaxRealizedDrawdownPercent:0.####}%");

        RefreshPeaks();
    }

    public bool TryOpen(
        StrategyResearchCandidate candidate,
        FuturesSizingCalculator.Result sizing,
        DateTime entryAtUtc,
        DateTime exitAtUtc,
        decimal assessmentBalanceAtEntry)
    {
        if (sizing.Quantity is null
            || sizing.PositionNotional is null
            || sizing.InitialMarginRequired is null
            || sizing.AssessmentLeverage is null
            || sizing.RiskAmount <= 0)
        {
            RejectedCount++;
            AddAudit("CandidateRejected", entryAtUtc, candidate.Id, CurrentBalance, CurrentBalance, OpenPositions.Count, OpenPositions.Count,
                sizing.UnavailableReason ?? "Sizing unavailable.");
            return false;
        }

        var theoreticalEntry = candidate.ProposedEntryPrice;
        var theoreticalExit = candidate.RawExitPrice ?? theoreticalEntry;
        var effectiveEntry = ShadowPositionCostCalculator.ApplyEntrySlippage(
            theoreticalEntry, candidate.Direction, _cost.SlippageBasisPoints);
        var entryNotional = Math.Round(sizing.Quantity.Value * effectiveEntry, 8);
        var entryFeeRate = _cost.EntryFeeRateUsed > 0 ? _cost.EntryFeeRateUsed : _cost.ResolveFeeRate(_cost.EntryOrderType);
        var entryFee = Math.Round(entryNotional * entryFeeRate, 8);

        var openBefore = OpenPositions.Count;
        var pos = new ShadowPosition
        {
            CandidateId = candidate.Id,
            SetupFingerprint = candidate.SetupFingerprint,
            Symbol = candidate.Symbol,
            Direction = candidate.Direction,
            EntryAtUtc = entryAtUtc,
            ExpectedExitAtUtc = exitAtUtc,
            TheoreticalEntryPrice = theoreticalEntry,
            EffectiveEntryPrice = effectiveEntry,
            EntryPrice = theoreticalEntry,
            TheoreticalExitPrice = theoreticalExit,
            ExitPrice = theoreticalExit,
            Quantity = sizing.Quantity.Value,
            EntryNotional = entryNotional,
            PositionNotional = sizing.PositionNotional.Value,
            AssessmentLeverage = sizing.AssessmentLeverage.Value,
            InitialMargin = sizing.InitialMarginRequired.Value,
            RiskAmountAtEntry = sizing.RiskAmount,
            AssessmentBalanceAtEntry = assessmentBalanceAtEntry,
            StopLoss = candidate.StopLoss,
            TargetPrice = candidate.Target1,
            RawRMultiple = candidate.RawRMultiple,
            ExitOutcome = candidate.ExitOutcome,
            RewardRisk = candidate.RewardRisk,
            EntryFeeAtOpen = entryFee,
            CurrentStatus = ShadowPositionStatus.Open,
            CostModelVersion = _cost.CostModelVersion,
            PortfolioPath = PathName
        };

        OpenPositions.Add(pos);
        AcceptedCount++;
        RefreshPeaks();
        AddAudit("PositionOpened", entryAtUtc, candidate.Id, CurrentBalance, CurrentBalance, openBefore, OpenPositions.Count,
            $"Qty={pos.Quantity:0.########} RiskAmount={pos.RiskAmountAtEntry:0.####} Notional={pos.EntryNotional:0.####}");
        return true;
    }

    public void RecordRejection(DateTime atUtc, long? candidateId, string reason)
    {
        RejectedCount++;
        AddAudit("CandidateRejected", atUtc, candidateId, CurrentBalance, CurrentBalance, OpenPositions.Count, OpenPositions.Count, reason);
    }

    public void RecordEvaluation(DateTime atUtc, long? candidateId, string reason) =>
        AddAudit("CandidateEvaluated", atUtc, candidateId, CurrentBalance, CurrentBalance, OpenPositions.Count, OpenPositions.Count, reason);

    private static void ApplySettlement(ShadowPosition pos, ShadowSettlementResult settlement, decimal balanceBefore)
    {
        pos.EffectiveExitPrice = settlement.EffectiveExitPrice;
        pos.ExitNotional = settlement.ExitNotional;
        pos.EntryFee = settlement.EntryFee;
        pos.ExitFee = settlement.ExitFee;
        pos.EntrySlippageCost = settlement.EntrySlippageCost;
        pos.ExitSlippageCost = settlement.ExitSlippageCost;
        pos.TotalSlippageCost = settlement.TotalSlippageCost;
        pos.EstimatedFundingCost = settlement.EstimatedFundingCost;
        pos.TotalTransactionCosts = settlement.TotalTransactionCosts;
        pos.ShadowGrossPnl = settlement.ShadowGrossPnl;
        pos.ShadowNetPnl = settlement.ShadowNetPnl;
        pos.ShadowNetReturnPercent = settlement.ShadowNetReturnPercent;
        pos.ShadowNetResult = settlement.ShadowNetResult;
        pos.BalanceBeforeExit = balanceBefore;
    }

    private void RefreshPeaks()
    {
        PeakOpenPositionCount = Math.Max(PeakOpenPositionCount, OpenPositions.Count);
        if (CurrentNotionalExposurePercent is { } n)
            PeakNotionalExposurePercent = Math.Max(PeakNotionalExposurePercent, n);
        if (CurrentMarginUsagePercent is { } m)
            PeakMarginUsagePercent = Math.Max(PeakMarginUsagePercent, m);
        if (ConcurrentRiskPercent is { } r)
            PeakConcurrentRiskPercent = Math.Max(PeakConcurrentRiskPercent, r);
    }

    private void AddAudit(
        string eventType,
        DateTime eventTimeUtc,
        long? candidateId,
        decimal balanceBefore,
        decimal balanceAfter,
        int openBefore,
        int openAfter,
        string reason)
    {
        AuditEvents.Add(new ShadowPortfolioAuditEvent
        {
            EventType = eventType,
            EventTimeUtc = eventTimeUtc,
            PortfolioPath = PathName,
            CandidateId = candidateId,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            OpenPositionCountBefore = openBefore,
            OpenPositionCountAfter = openAfter,
            Reason = reason
        });
    }

    public decimal PortfolioRiskScore(RiskProfileSnapshotDto snapshot)
    {
        var parts = new List<decimal>();

        if (snapshot.MaxConcurrentPositions > 0)
        {
            parts.Add(1m - Math.Clamp((decimal)OpenPositions.Count / snapshot.MaxConcurrentPositions, 0m, 1m));
        }

        if (snapshot.MaxTotalNotionalExposurePercent is > 0 && CurrentNotionalExposurePercent is { } n)
        {
            parts.Add(1m - Math.Clamp(n / snapshot.MaxTotalNotionalExposurePercent.Value, 0m, 1m));
        }

        if (snapshot.MaxTotalMarginUsagePercent is > 0 && CurrentMarginUsagePercent is { } m)
        {
            parts.Add(1m - Math.Clamp(m / snapshot.MaxTotalMarginUsagePercent.Value, 0m, 1m));
        }

        if (snapshot.MaxConcurrentRiskPercent is > 0 && ConcurrentRiskPercent is { } c)
        {
            parts.Add(1m - Math.Clamp(c / snapshot.MaxConcurrentRiskPercent.Value, 0m, 1m));
        }

        if (snapshot.MaxDailyLossPercent > 0 && DailyLossUsagePercent is { } d)
        {
            parts.Add(1m - Math.Clamp(d / snapshot.MaxDailyLossPercent, 0m, 1m));
        }

        if (snapshot.MaxDrawdownPercent > 0 && CurrentDrawdownPercent is { } dd)
        {
            parts.Add(1m - Math.Clamp(dd / snapshot.MaxDrawdownPercent, 0m, 1m));
        }

        if (parts.Count == 0)
        {
            return OpenPositions.Count == 0 && (DailyLossUsagePercent ?? 0m) <= 0 ? 95m : 70m;
        }

        return Math.Round(parts.Average() * 100m, 2);
    }
}

public enum ShadowPositionStatus
{
    Open = 1,
    Closed = 2,
    RejectedAtEntry = 3,
    Cancelled = 4
}

public sealed class ShadowPosition
{
    public long CandidateId { get; init; }
    public string SetupFingerprint { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public TradeDirection Direction { get; init; }
    public DateTime EntryAtUtc { get; init; }
    public DateTime ExpectedExitAtUtc { get; init; }
    public decimal TheoreticalEntryPrice { get; init; }
    public decimal EffectiveEntryPrice { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? TheoreticalExitPrice { get; set; }
    public decimal? EffectiveExitPrice { get; set; }
    public decimal? ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal EntryNotional { get; init; }
    public decimal? ExitNotional { get; set; }
    public decimal PositionNotional { get; init; }
    public decimal AssessmentLeverage { get; init; }
    public decimal InitialMargin { get; init; }
    public decimal RiskAmountAtEntry { get; init; }
    public decimal AssessmentBalanceAtEntry { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TargetPrice { get; init; }
    public decimal? RawRMultiple { get; init; }
    public decimal RewardRisk { get; init; }
    public ResearchExitOutcome ExitOutcome { get; init; }
    public decimal EntryFeeAtOpen { get; init; }
    public decimal? EntryFee { get; set; }
    public decimal? ExitFee { get; set; }
    public decimal? EntrySlippageCost { get; set; }
    public decimal? ExitSlippageCost { get; set; }
    public decimal? TotalSlippageCost { get; set; }
    public decimal? EstimatedFundingCost { get; set; }
    public decimal? TotalTransactionCosts { get; set; }
    public decimal? ShadowGrossPnl { get; set; }
    public decimal? ShadowNetPnl { get; set; }
    public decimal? ShadowNetReturnPercent { get; set; }
    public ResearchNetResult? ShadowNetResult { get; set; }
    public decimal? BalanceBeforeExit { get; set; }
    public decimal? BalanceAfterExit { get; set; }
    public DateTime? RealizedAtUtc { get; set; }
    public string CostModelVersion { get; init; } = StrategyLabCostModelVersions.V1;
    public string PortfolioPath { get; init; } = string.Empty;
    public ShadowPositionStatus CurrentStatus { get; set; }
    public decimal? RealizedNetPnl { get; set; }
    public ShadowSettlementResult? Settlement { get; set; }
}

public sealed class ShadowTradeLedgerEntry
{
    public string PortfolioPath { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public TradeDirection Direction { get; init; }
    public long CandidateId { get; init; }
    public string SetupFingerprint { get; init; } = string.Empty;
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal EffectiveEntryPrice { get; init; }
    public decimal EffectiveExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal GrossR { get; init; }
    public decimal GrossPnl { get; init; }
    public decimal EntryFee { get; init; }
    public decimal ExitFee { get; init; }
    public decimal Slippage { get; init; }
    public decimal TotalCost { get; init; }
    public decimal NetPnl { get; init; }
    public string NetResult { get; init; } = string.Empty;
    public string ExitOutcome { get; init; } = string.Empty;
    public decimal BalanceBefore { get; init; }
    public decimal BalanceAfter { get; init; }
    public decimal? DrawdownAfterExit { get; init; }

    public static ShadowTradeLedgerEntry From(
        ShadowPosition pos,
        ShadowSettlementResult settlement,
        decimal balanceBefore,
        decimal balanceAfter,
        decimal? drawdownAfter) =>
        new()
        {
            PortfolioPath = pos.PortfolioPath,
            EntryTimeUtc = pos.EntryAtUtc,
            ExitTimeUtc = pos.ExpectedExitAtUtc,
            Symbol = pos.Symbol,
            Direction = pos.Direction,
            CandidateId = pos.CandidateId,
            SetupFingerprint = pos.SetupFingerprint,
            EntryPrice = pos.TheoreticalEntryPrice,
            ExitPrice = pos.TheoreticalExitPrice ?? settlement.TheoreticalExitPrice,
            EffectiveEntryPrice = settlement.EffectiveEntryPrice,
            EffectiveExitPrice = settlement.EffectiveExitPrice,
            Quantity = pos.Quantity,
            GrossR = settlement.GrossRMultiple,
            GrossPnl = settlement.ShadowGrossPnl,
            EntryFee = settlement.EntryFee,
            ExitFee = settlement.ExitFee,
            Slippage = settlement.TotalSlippageCost,
            TotalCost = settlement.TotalTransactionCosts,
            NetPnl = settlement.ShadowNetPnl,
            NetResult = settlement.ShadowNetResult.ToString(),
            ExitOutcome = pos.ExitOutcome.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            DrawdownAfterExit = drawdownAfter
        };
}

public sealed class ShadowPortfolioAuditEvent
{
    public string EventType { get; init; } = string.Empty;
    public DateTime EventTimeUtc { get; init; }
    public string PortfolioPath { get; init; } = string.Empty;
    public long? CandidateId { get; init; }
    public decimal BalanceBefore { get; init; }
    public decimal BalanceAfter { get; init; }
    public int OpenPositionCountBefore { get; init; }
    public int OpenPositionCountAfter { get; init; }
    public string Reason { get; init; } = string.Empty;
}
