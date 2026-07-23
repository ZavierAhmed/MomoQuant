using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Reports;

public interface IRiskReportService
{
    Task<ServiceResult<RiskRejectionReportDto>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskRejectionReportDto>> GetForBacktestAsync(long backtestRunId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskRejectionReportDto>> GetForPaperSessionAsync(long paperSessionId, ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class RiskReportService : IRiskReportService
{
    private readonly IReportQueryValidator _queryValidator;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly IBacktestRunRepository _backtestRunRepository;
    private readonly IPaperTradingSessionRepository _paperSessionRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategySignalRepository _signalRepository;

    public RiskReportService(
        IReportQueryValidator queryValidator,
        IReportDataRepository reportDataRepository,
        IBacktestRunRepository backtestRunRepository,
        IPaperTradingSessionRepository paperSessionRepository,
        ISymbolRepository symbolRepository,
        IStrategyRepository strategyRepository,
        IStrategySignalRepository signalRepository)
    {
        _queryValidator = queryValidator;
        _reportDataRepository = reportDataRepository;
        _backtestRunRepository = backtestRunRepository;
        _paperSessionRepository = paperSessionRepository;
        _symbolRepository = symbolRepository;
        _strategyRepository = strategyRepository;
        _signalRepository = signalRepository;
    }

    public async Task<ServiceResult<RiskRejectionReportDto>> GetAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<RiskRejectionReportDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        return ServiceResult<RiskRejectionReportDto>.Ok(await BuildAsync(validation.Data, cancellationToken));
    }

    public async Task<ServiceResult<RiskRejectionReportDto>> GetForBacktestAsync(
        long backtestRunId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var run = await _backtestRunRepository.GetByIdAsync(backtestRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<RiskRejectionReportDto>.Fail("Backtest run was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = run.StartDateUtc,
            ToUtc = run.EndDateUtc,
            TradingSessionId = run.TradingSessionId,
            Mode = TradingMode.Backtest,
            Limit = query.Limit
        };

        return ServiceResult<RiskRejectionReportDto>.Ok(await BuildAsync(filter, cancellationToken));
    }

    public async Task<ServiceResult<RiskRejectionReportDto>> GetForPaperSessionAsync(
        long paperSessionId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _paperSessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<RiskRejectionReportDto>.Fail("Paper session was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = session.FromUtc ?? DateTime.MinValue,
            ToUtc = session.ToUtc ?? DateTime.UtcNow,
            TradingSessionId = session.TradingSessionId,
            Mode = TradingMode.Paper,
            Limit = query.Limit
        };

        return ServiceResult<RiskRejectionReportDto>.Ok(await BuildAsync(filter, cancellationToken));
    }

    private async Task<RiskRejectionReportDto> BuildAsync(ReportQueryFilter filter, CancellationToken cancellationToken)
    {
        var decisions = await _reportDataRepository.GetRiskDecisionsAsync(filter, cancellationToken);
        var sessionIds = decisions.Where(decision => decision.TradingSessionId.HasValue).Select(decision => decision.TradingSessionId!.Value).Distinct().ToList();
        var sessionModes = await _reportDataRepository.GetSessionModesAsync(sessionIds, cancellationToken);
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);

        var approved = decisions.Count(decision => decision.Decision == RiskDecisionType.Approved);
        var rejected = decisions.Count(decision => decision.Decision == RiskDecisionType.Rejected);
        var adjusted = decisions.Count(decision => decision.Decision == RiskDecisionType.Adjusted);
        var emergency = decisions.Count(decision => decision.Decision == RiskDecisionType.EmergencyBlocked);
        var total = decisions.Count;

        var rejectedRules = decisions
            .Where(decision => decision.Decision == RiskDecisionType.Rejected && !string.IsNullOrWhiteSpace(decision.RejectedRuleKey))
            .GroupBy(decision => decision.RejectedRuleKey!)
            .Select(group => new RiskRuleRejectionSummaryDto
            {
                RuleKey = group.Key,
                Count = group.Count(),
                Percentage = rejected > 0 ? (decimal)group.Count() / rejected * 100m : 0m
            })
            .OrderByDescending(item => item.Count)
            .Take(filter.Limit)
            .ToList();

        var details = new List<RiskRejectionDetailDto>();
        foreach (var decision in decisions.Where(decision => decision.Decision == RiskDecisionType.Rejected).Take(filter.Limit))
        {
            var symbol = await _symbolRepository.GetByIdAsync(decision.SymbolId, cancellationToken);
            string? strategyCode = null;
            decimal? confidenceScore = null;

            if (decision.SignalId.HasValue)
            {
                var signal = await _signalRepository.GetByIdAsync(decision.SignalId.Value, cancellationToken);
                if (signal is not null)
                {
                    confidenceScore = ConfidenceScoreNormalizer.Normalize(signal.Strength);
                    strategyCode = strategies.FirstOrDefault(strategy => strategy.Id == signal.StrategyId)?.Code.ToCode();
                }
            }

            var mode = decision.TradingSessionId.HasValue && sessionModes.TryGetValue(decision.TradingSessionId.Value, out var tradingMode)
                ? tradingMode.ToString()
                : "Unknown";

            details.Add(new RiskRejectionDetailDto
            {
                TimestampUtc = decision.CreatedAtUtc,
                Mode = mode,
                Symbol = symbol?.SymbolName ?? decision.SymbolId.ToString(),
                StrategyCode = strategyCode,
                RejectedRuleKey = decision.RejectedRuleKey,
                Reason = decision.Reason,
                ConfidenceScore = confidenceScore
            });
        }

        return new RiskRejectionReportDto
        {
            TotalRiskDecisions = total,
            ApprovedCount = approved,
            RejectedCount = rejected,
            AdjustedCount = adjusted,
            EmergencyBlockedCount = emergency,
            RejectionRatePercent = total > 0 ? (decimal)rejected / total * 100m : 0m,
            TopRejectedRules = rejectedRules,
            RejectionDetails = details
        };
    }
}

public interface IAiReportService
{
    Task<ServiceResult<AiDecisionReportDto>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<AiDecisionReportDto>> GetForBacktestAsync(long backtestRunId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<AiDecisionReportDto>> GetForPaperSessionAsync(long paperSessionId, ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class AiReportService : IAiReportService
{
    private static readonly string[] ConfidenceBuckets = ["VeryLow", "Low", "Medium", "High", "VeryHigh"];

    private readonly IReportQueryValidator _queryValidator;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly IBacktestRunRepository _backtestRunRepository;
    private readonly IPaperTradingSessionRepository _paperSessionRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ITradeRepository _tradeRepository;

    public AiReportService(
        IReportQueryValidator queryValidator,
        IReportDataRepository reportDataRepository,
        IBacktestRunRepository backtestRunRepository,
        IPaperTradingSessionRepository paperSessionRepository,
        ISymbolRepository symbolRepository,
        ITradeRepository tradeRepository)
    {
        _queryValidator = queryValidator;
        _reportDataRepository = reportDataRepository;
        _backtestRunRepository = backtestRunRepository;
        _paperSessionRepository = paperSessionRepository;
        _symbolRepository = symbolRepository;
        _tradeRepository = tradeRepository;
    }

    public async Task<ServiceResult<AiDecisionReportDto>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<AiDecisionReportDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        return ServiceResult<AiDecisionReportDto>.Ok(await BuildAsync(validation.Data, cancellationToken));
    }

    public async Task<ServiceResult<AiDecisionReportDto>> GetForBacktestAsync(
        long backtestRunId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var run = await _backtestRunRepository.GetByIdAsync(backtestRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<AiDecisionReportDto>.Fail("Backtest run was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = run.StartDateUtc,
            ToUtc = run.EndDateUtc,
            TradingSessionId = run.TradingSessionId,
            Mode = TradingMode.Backtest,
            Limit = query.Limit
        };

        return ServiceResult<AiDecisionReportDto>.Ok(await BuildAsync(filter, cancellationToken));
    }

    public async Task<ServiceResult<AiDecisionReportDto>> GetForPaperSessionAsync(
        long paperSessionId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _paperSessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<AiDecisionReportDto>.Fail("Paper session was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = session.FromUtc ?? DateTime.MinValue,
            ToUtc = session.ToUtc ?? DateTime.UtcNow,
            TradingSessionId = session.TradingSessionId,
            Mode = TradingMode.Paper,
            Limit = query.Limit
        };

        return ServiceResult<AiDecisionReportDto>.Ok(await BuildAsync(filter, cancellationToken));
    }

    private async Task<AiDecisionReportDto> BuildAsync(ReportQueryFilter filter, CancellationToken cancellationToken)
    {
        var decisions = await _reportDataRepository.GetAiDecisionsAsync(filter, cancellationToken);
        var riskDecisions = await _reportDataRepository.GetRiskDecisionsAsync(filter, cancellationToken);
        var trades = filter.TradingSessionId.HasValue
            ? await _tradeRepository.GetByTradingSessionIdAsync(filter.TradingSessionId.Value, cancellationToken)
            : await _reportDataRepository.GetTradesAsync(filter, cancellationToken);

        var confidenceBreakdown = ConfidenceBuckets.ToDictionary(
            bucket => bucket,
            bucket => decisions.Count(decision => string.Equals(decision.ConfidenceClassification, bucket, StringComparison.OrdinalIgnoreCase)));

        var regimeBreakdown = Enum.GetNames<MarketRegime>()
            .ToDictionary(
                name => name,
                name => decisions.Count(decision => decision.MarketRegime.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)));

        var anomalySeverityBreakdown = decisions
            .Where(decision => decision.IsAnomalous && !string.IsNullOrWhiteSpace(decision.AnomalySeverity))
            .GroupBy(decision => decision.AnomalySeverity!)
            .ToDictionary(group => group.Key, group => group.Count());

        var highConfidenceLosses = trades.Count(trade =>
            trade.NetPnl < 0
            && trade.AiDecisionId.HasValue
            && decisions.Any(decision => decision.Id == trade.AiDecisionId && decision.ConfidenceScore >= 80m));

        var regimePerformance = decisions
            .GroupBy(decision => decision.MarketRegime)
            .Select(group =>
            {
                var regimeTrades = trades.Where(trade =>
                    trade.AiDecisionId.HasValue && group.Any(decision => decision.Id == trade.AiDecisionId)).ToList();
                var analysis = ReportMetrics.AnalyzeTrades(regimeTrades);
                var regimeRisk = riskDecisions.Count;
                var regimeRejected = riskDecisions.Count(decision => decision.Decision == RiskDecisionType.Rejected);

                return new MarketRegimeReportDto
                {
                    MarketRegime = group.Key.ToString(),
                    TotalSignals = group.Count(),
                    ApprovedSignals = group.Count(decision => decision.TradeAllowed),
                    RejectedSignals = group.Count(decision => !decision.TradeAllowed),
                    TotalTrades = analysis.Winning + analysis.Losing + analysis.BreakEven,
                    WinningTrades = analysis.Winning,
                    LosingTrades = analysis.Losing,
                    WinRatePercent = ReportMetrics.CalculateWinRate(analysis.Winning, analysis.Losing, analysis.BreakEven),
                    NetPnl = regimeTrades.Sum(trade => trade.NetPnl),
                    ProfitFactor = ReportMetrics.CalculateProfitFactor(analysis.GrossProfit, analysis.GrossLoss),
                    AverageConfidenceScore = group.Average(decision => decision.ConfidenceScore),
                    AverageRiskRejectionRate = regimeRisk > 0 ? (decimal)regimeRejected / regimeRisk * 100m : 0m
                };
            })
            .ToList();

        var strategyConfidence = decisions
            .Where(decision => decision.PreferredStrategyCode.HasValue)
            .GroupBy(decision => decision.PreferredStrategyCode!.Value.ToCode())
            .Select(group => new StrategyConfidenceSummaryDto
            {
                StrategyCode = group.Key,
                AverageConfidenceScore = group.Average(decision => decision.ConfidenceScore)
            })
            .ToList();

        var symbolConfidence = new List<SymbolConfidenceSummaryDto>();
        foreach (var group in decisions.GroupBy(decision => decision.SymbolId))
        {
            var symbol = await _symbolRepository.GetByIdAsync(group.Key, cancellationToken);
            symbolConfidence.Add(new SymbolConfidenceSummaryDto
            {
                Symbol = symbol?.SymbolName ?? group.Key.ToString(),
                AverageConfidenceScore = group.Average(decision => decision.ConfidenceScore)
            });
        }

        return new AiDecisionReportDto
        {
            TotalAiDecisions = decisions.Count,
            AverageConfidenceScore = decisions.Count > 0 ? decisions.Average(decision => decision.ConfidenceScore) : 0m,
            ConfidenceBreakdown = confidenceBreakdown,
            RegimeBreakdown = regimeBreakdown,
            AnomalyCount = decisions.Count(decision => decision.IsAnomalous),
            AnomalySeverityBreakdown = anomalySeverityBreakdown,
            AverageConfidenceByStrategy = strategyConfidence,
            AverageConfidenceBySymbol = symbolConfidence,
            HighConfidenceLosses = highConfidenceLosses,
            MarketRegimePerformance = regimePerformance
        };
    }
}

public interface IExecutionReportService
{
    Task<ServiceResult<ExecutionReportDto>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersForBacktestAsync(long backtestRunId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersForPaperSessionAsync(long paperSessionId, ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class ExecutionReportService : IExecutionReportService
{
    private readonly IReportQueryValidator _queryValidator;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly IBacktestRunRepository _backtestRunRepository;
    private readonly IPaperTradingSessionRepository _paperSessionRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IStrategyRepository _strategyRepository;

    public ExecutionReportService(
        IReportQueryValidator queryValidator,
        IReportDataRepository reportDataRepository,
        IBacktestRunRepository backtestRunRepository,
        IPaperTradingSessionRepository paperSessionRepository,
        ISymbolRepository symbolRepository,
        IStrategySignalRepository signalRepository,
        IStrategyRepository strategyRepository)
    {
        _queryValidator = queryValidator;
        _reportDataRepository = reportDataRepository;
        _backtestRunRepository = backtestRunRepository;
        _paperSessionRepository = paperSessionRepository;
        _symbolRepository = symbolRepository;
        _signalRepository = signalRepository;
        _strategyRepository = strategyRepository;
    }

    public async Task<ServiceResult<ExecutionReportDto>> GetAsync(ReportQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<ExecutionReportDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        return ServiceResult<ExecutionReportDto>.Ok(await BuildExecutionAsync(validation.Data, cancellationToken));
    }

    public async Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersForBacktestAsync(
        long backtestRunId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var run = await _backtestRunRepository.GetByIdAsync(backtestRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<MissedOrderReportDto>.Fail("Backtest run was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = run.StartDateUtc,
            ToUtc = run.EndDateUtc,
            TradingSessionId = run.TradingSessionId,
            Mode = TradingMode.Backtest,
            Limit = query.Limit
        };

        return ServiceResult<MissedOrderReportDto>.Ok(await BuildMissedOrdersAsync(filter, run.ExecutionMode.ToString(), cancellationToken));
    }

    public async Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersForPaperSessionAsync(
        long paperSessionId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _paperSessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<MissedOrderReportDto>.Fail("Paper session was not found.");
        }

        var filter = new ReportQueryFilter
        {
            FromUtc = session.FromUtc ?? DateTime.MinValue,
            ToUtc = session.ToUtc ?? DateTime.UtcNow,
            TradingSessionId = session.TradingSessionId,
            Mode = TradingMode.Paper,
            Limit = query.Limit
        };

        return ServiceResult<MissedOrderReportDto>.Ok(await BuildMissedOrdersAsync(filter, session.ExecutionMode.ToString(), cancellationToken));
    }

    private async Task<ExecutionReportDto> BuildExecutionAsync(ReportQueryFilter filter, CancellationToken cancellationToken)
    {
        var orders = await _reportDataRepository.GetOrdersAsync(filter, cancellationToken);
        var missedOrders = await _reportDataRepository.GetMissedOrdersAsync(filter, cancellationToken);
        var mode = filter.Mode?.ToString() ?? "All";

        var filled = orders.Count(order => order.Status == OrderStatus.Filled);
        var cancelled = orders.Count(order => order.Status is OrderStatus.Cancelled or OrderStatus.Expired);
        var makerOrders = orders.Count(order => order.IsPostOnly);
        var takerOrders = orders.Count(order => !order.IsPostOnly);
        var filledMaker = orders.Count(order => order.IsPostOnly && order.Status == OrderStatus.Filled);
        var total = orders.Count;

        return new ExecutionReportDto
        {
            Mode = mode,
            TotalOrders = total,
            FilledOrders = filled,
            CancelledOrders = cancelled,
            MissedOrders = missedOrders.Count,
            FillRatePercent = total > 0 ? (decimal)filled / total * 100m : 0m,
            AverageFees = 0m,
            TotalFees = 0m,
            MakerOrders = makerOrders,
            TakerOrders = takerOrders,
            MakerFillRatePercent = makerOrders > 0 ? (decimal)filledMaker / makerOrders * 100m : 0m,
            MissedMakerRatePercent = makerOrders > 0 ? (decimal)missedOrders.Count / makerOrders * 100m : 0m
        };
    }

    private async Task<MissedOrderReportDto> BuildMissedOrdersAsync(
        ReportQueryFilter filter,
        string executionMode,
        CancellationToken cancellationToken)
    {
        var missedOrders = await _reportDataRepository.GetMissedOrdersAsync(filter, cancellationToken);
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var details = new List<MissedOrderDetailDto>();

        foreach (var missed in missedOrders.Take(filter.Limit))
        {
            var symbol = await _symbolRepository.GetByIdAsync(missed.SymbolId, cancellationToken);
            string? strategyCode = null;
            string timeframe = "unknown";

            if (missed.SignalId > 0)
            {
                var signal = await _signalRepository.GetByIdAsync(missed.SignalId, cancellationToken);
                if (signal is not null)
                {
                    timeframe = signal.Timeframe.ToString();
                    strategyCode = strategies.FirstOrDefault(strategy => strategy.Id == signal.StrategyId)?.Code.ToCode();
                }
            }

            details.Add(new MissedOrderDetailDto
            {
                Id = missed.Id,
                Symbol = symbol?.SymbolName ?? missed.SymbolId.ToString(),
                StrategyCode = strategyCode,
                Timeframe = timeframe,
                ExecutionMode = executionMode,
                RequestedPrice = missed.RequestedPrice,
                Reason = missed.Reason.ToString(),
                ExpiredAtUtc = missed.ExpiredAtUtc
            });
        }

        var missDistances = missedOrders
            .Where(order => order.BestBid.HasValue && order.BestAsk.HasValue)
            .Select(order => Math.Abs(order.RequestedPrice - ((order.BestBid!.Value + order.BestAsk!.Value) / 2m)))
            .ToList();

        return new MissedOrderReportDto
        {
            TotalMissedOrders = missedOrders.Count,
            MissedByStrategy = await GroupMissedByStrategyAsync(missedOrders, strategies, cancellationToken),
            MissedBySymbol = missedOrders.GroupBy(order => order.SymbolId)
                .Select(group => new MissedOrderGroupDto { Key = group.Key.ToString(), Count = group.Count() }).ToList(),
            MissedByTimeframe = details.GroupBy(detail => detail.Timeframe)
                .Select(group => new MissedOrderGroupDto { Key = group.Key, Count = group.Count() }).ToList(),
            MissedByExecutionMode = [new MissedOrderGroupDto { Key = executionMode, Count = missedOrders.Count }],
            AverageMissDistance = missDistances.Count > 0 ? missDistances.Average() : 0m,
            EstimatedMissedPnl = null,
            MissedOrderDetails = details
        };
    }

    private async Task<IReadOnlyList<MissedOrderGroupDto>> GroupMissedByStrategyAsync(
        IReadOnlyList<Domain.Execution.MissedOrder> missedOrders,
        IReadOnlyList<Domain.Strategies.Strategy> strategies,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, int>();

        foreach (var missed in missedOrders)
        {
            var key = "Unknown";
            if (missed.SignalId > 0)
            {
                var signal = await _signalRepository.GetByIdAsync(missed.SignalId, cancellationToken);
                if (signal is not null)
                {
                    key = strategies.FirstOrDefault(strategy => strategy.Id == signal.StrategyId)?.Code.ToCode() ?? signal.StrategyId.ToString();
                }
            }

            groups[key] = groups.GetValueOrDefault(key) + 1;
        }

        return groups.Select(pair => new MissedOrderGroupDto { Key = pair.Key, Count = pair.Value }).ToList();
    }
}
