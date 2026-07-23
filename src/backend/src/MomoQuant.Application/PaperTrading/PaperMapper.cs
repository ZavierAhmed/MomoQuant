using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.PaperTrading;

public static class PaperMapper
{
    public static PaperAccountDto MapAccount(PaperAccount account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        InitialBalance = account.InitialBalance,
        CurrentBalance = account.CurrentBalance,
        CurrentEquity = account.CurrentEquity,
        Currency = account.Currency,
        TotalRealizedPnl = account.TotalRealizedPnl,
        TotalUnrealizedPnl = account.TotalUnrealizedPnl,
        TotalFees = account.TotalFees,
        MaxDrawdown = account.MaxDrawdown,
        MaxDrawdownPercent = account.MaxDrawdownPercent,
        IsActive = account.IsActive,
        CreatedAtUtc = account.CreatedAtUtc,
        UpdatedAtUtc = account.UpdatedAtUtc
    };

    public static PaperAccountSnapshotDto MapSnapshot(PaperAccountSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        PaperAccountId = snapshot.PaperAccountId,
        PaperSessionId = snapshot.PaperSessionId,
        TimestampUtc = snapshot.TimestampUtc,
        Balance = snapshot.Balance,
        Equity = snapshot.Equity,
        RealizedPnl = snapshot.RealizedPnl,
        UnrealizedPnl = snapshot.UnrealizedPnl,
        TotalFees = snapshot.TotalFees,
        Drawdown = snapshot.Drawdown,
        DrawdownPercent = snapshot.DrawdownPercent,
        OpenPositionCount = snapshot.OpenPositionCount
    };

    public static PaperSessionDto MapSession(PaperTradingSession session) => new()
    {
        Id = session.Id,
        Name = session.Name,
        PaperAccountId = session.PaperAccountId,
        Status = session.Status.ToString(),
        Mode = session.Mode.ToString(),
        ExchangeId = session.ExchangeId,
        RiskProfileId = session.RiskProfileId,
        ExecutionMode = session.ExecutionMode.ToString(),
        UseAiScoring = session.UseAiScoring,
        MinConfidenceScore = session.MinConfidenceScore,
        FromUtc = session.FromUtc,
        ToUtc = session.ToUtc,
        CurrentCandleTimeUtc = session.CurrentCandleTimeUtc,
        CurrentCandleIndex = session.CurrentCandleIndex,
        TotalCandles = session.TotalCandles,
        StartedAtUtc = session.StartedAtUtc,
        PausedAtUtc = session.PausedAtUtc,
        StoppedAtUtc = session.StoppedAtUtc,
        CompletedAtUtc = session.CompletedAtUtc,
        ErrorMessage = session.ErrorMessage,
        CreatedAtUtc = session.CreatedAtUtc,
        UpdatedAtUtc = session.UpdatedAtUtc
    };

    public static bool TryParseMode(string input, out PaperTradingMode mode)
    {
        if (Enum.TryParse<PaperTradingMode>(input, ignoreCase: true, out mode))
        {
            return true;
        }

        mode = default;
        return false;
    }

    public static bool TryParseExecutionMode(string input, out ExecutionMode mode)
    {
        if (Enum.TryParse<ExecutionMode>(input, ignoreCase: true, out mode))
        {
            return true;
        }

        mode = default;
        return false;
    }

    public static PaperSessionState CreateRuntimeState(
        PaperSessionSettings settings,
        PaperTradingSession session,
        PaperAccount account,
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        IReadOnlyList<RiskRule> riskRules,
        Domain.Exchanges.Symbol symbol,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>>? frozenStrategyParameters = null)
    {
        var runSettings = new RunBacktestSettings
        {
            Name = session.Name,
            SymbolIds = settings.SymbolIds,
            Timeframes = settings.Timeframes,
            FromUtc = session.FromUtc ?? DateTime.UtcNow,
            ToUtc = session.ToUtc ?? DateTime.UtcNow,
            InitialBalance = account.CurrentBalance,
            StrategyIds = settings.StrategyIds,
            ExecutionMode = settings.ExecutionMode,
            MakerFeeRate = settings.MakerFeeRate,
            TakerFeeRate = settings.TakerFeeRate,
            OrderExpiryCandles = settings.OrderExpiryCandles,
            UseAiScoring = settings.UseAiScoring,
            StrictAiRequired = settings.StrictAiRequired,
            MinConfidenceScore = settings.MinConfidenceScore,
            SlippagePercent = settings.SlippagePercent
        };

        var ruleSet = RiskRuleSet.FromRules(riskRules);
        var context = new BacktestContext
        {
            BacktestRunId = session.Id,
            SimulationMode = TradingMode.Paper,
            TradingSessionId = session.TradingSessionId,
            ExchangeId = session.ExchangeId,
            RiskProfileId = session.RiskProfileId,
            Settings = runSettings,
            RiskRules = riskRules,
            Strategies = strategies.Select(item => item.Strategy).ToList(),
            Symbols = new Dictionary<long, Domain.Exchanges.Symbol> { [symbol.Id] = symbol },
            Balance = account.CurrentBalance,
            PeakEquity = account.CurrentEquity > 0 ? account.CurrentEquity : account.CurrentBalance,
            EmergencyStopEnabled = ruleSet.EmergencyStopEnabled
        };

        return new PaperSessionState
        {
            Session = session,
            Account = account,
            Settings = settings,
            Context = context,
            Dataset = dataset,
            Strategies = strategies,
            RiskRules = riskRules,
            FrozenStrategyParameters = frozenStrategyParameters,
            NextEvaluationIndex = Math.Max(0, session.CurrentCandleIndex + 1)
        };
    }
}
