using Microsoft.EntityFrameworkCore;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.Identity;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Monitoring;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Settings;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Simulation;
using MomoQuant.Domain.Optimization;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.Trades;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Persistence;

public class MomoQuantDbContext : DbContext
{
    public MomoQuantDbContext(DbContextOptions<MomoQuantDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Exchange> Exchanges => Set<Exchange>();
    public DbSet<Symbol> Symbols => Set<Symbol>();
    public DbSet<Candle> Candles => Set<Candle>();
    public DbSet<MarketDataImport> MarketDataImports => Set<MarketDataImport>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<StrategyParameter> StrategyParameters => Set<StrategyParameter>();
    public DbSet<TradingSession> TradingSessions => Set<TradingSession>();
    public DbSet<TradingSessionSymbol> TradingSessionSymbols => Set<TradingSessionSymbol>();
    public DbSet<IndicatorSnapshot> IndicatorSnapshots => Set<IndicatorSnapshot>();
    public DbSet<StrategySignal> StrategySignals => Set<StrategySignal>();
    public DbSet<AiDecision> AiDecisions => Set<AiDecision>();
    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();
    public DbSet<RiskRule> RiskRules => Set<RiskRule>();
    public DbSet<RiskDecision> RiskDecisions => Set<RiskDecision>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderFill> OrderFills => Set<OrderFill>();
    public DbSet<MissedOrder> MissedOrders => Set<MissedOrder>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();
    public DbSet<BacktestResult> BacktestResults => Set<BacktestResult>();
    public DbSet<BacktestEquityPoint> BacktestEquityPoints => Set<BacktestEquityPoint>();
    public DbSet<BacktestStrategyResult> BacktestStrategyResults => Set<BacktestStrategyResult>();
    public DbSet<BacktestSymbolResult> BacktestSymbolResults => Set<BacktestSymbolResult>();
    public DbSet<StrategyBenchmarkRun> StrategyBenchmarkRuns => Set<StrategyBenchmarkRun>();
    public DbSet<StrategyBenchmarkResult> StrategyBenchmarkResults => Set<StrategyBenchmarkResult>();
    public DbSet<StrategyBenchmarkRunItem> StrategyBenchmarkRunItems => Set<StrategyBenchmarkRunItem>();
    public DbSet<ReplaySession> ReplaySessions => Set<ReplaySession>();
    public DbSet<ReplayFrame> ReplayFrames => Set<ReplayFrame>();
    public DbSet<PaperAccount> PaperAccounts => Set<PaperAccount>();
    public DbSet<PaperAccountSnapshot> PaperAccountSnapshots => Set<PaperAccountSnapshot>();
    public DbSet<PaperTradingSession> PaperTradingSessions => Set<PaperTradingSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemHealthLog> SystemHealthLogs => Set<SystemHealthLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SimulationRunSummary> SimulationRunSummaries => Set<SimulationRunSummary>();
    public DbSet<TradingSystemAnalysis> TradingSystemAnalyses => Set<TradingSystemAnalysis>();
    public DbSet<SkLivePaperSession> SkLivePaperSessions => Set<SkLivePaperSession>();
    public DbSet<SkLivePaperCandidate> SkLivePaperCandidates => Set<SkLivePaperCandidate>();
    public DbSet<SkLivePaperTrade> SkLivePaperTrades => Set<SkLivePaperTrade>();
    public DbSet<SkLivePaperEvent> SkLivePaperEvents => Set<SkLivePaperEvent>();
    public DbSet<Domain.Exports.ExportJob> ExportJobs => Set<Domain.Exports.ExportJob>();
    public DbSet<StrategyParameterSet> StrategyParameterSets => Set<StrategyParameterSet>();
    public DbSet<ParameterOptimizationRun> ParameterOptimizationRuns => Set<ParameterOptimizationRun>();
    public DbSet<TargetOptimizationRun> TargetOptimizationRuns => Set<TargetOptimizationRun>();
    public DbSet<StrategyLabRun> StrategyLabRuns => Set<StrategyLabRun>();
    public DbSet<StrategyResearchCandidate> StrategyResearchCandidates => Set<StrategyResearchCandidate>();
    public DbSet<StrategyResearchCandidatePortfolioAssessment> StrategyResearchCandidatePortfolioAssessments =>
        Set<StrategyResearchCandidatePortfolioAssessment>();
    public DbSet<Domain.ValidationLab.ValidationExperiment> ValidationExperiments => Set<Domain.ValidationLab.ValidationExperiment>();
    public DbSet<Domain.ValidationLab.ValidationParameterTrial> ValidationParameterTrials => Set<Domain.ValidationLab.ValidationParameterTrial>();
    public DbSet<Domain.ValidationLab.ValidationSegmentResult> ValidationSegmentResults => Set<Domain.ValidationLab.ValidationSegmentResult>();
    public DbSet<Domain.ValidationLab.ValidationExperimentExecutionLease> ValidationExperimentExecutionLeases =>
        Set<Domain.ValidationLab.ValidationExperimentExecutionLease>();
    public DbSet<Domain.ValidationLab.ValidationCandleAccessAudit> ValidationCandleAccessAudits =>
        Set<Domain.ValidationLab.ValidationCandleAccessAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MomoQuantDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
