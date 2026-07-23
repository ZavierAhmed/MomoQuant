using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Auth;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.Exports;
using MomoQuant.Application.Exchanges;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.StrategyRecommendations;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.Research;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Audit.Services;
using MomoQuant.Application.Monitoring;
using MomoQuant.Application.Monitoring.Services;
using MomoQuant.Application.Reports;
using Reports = MomoQuant.Application.Reports;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Symbols;
using MomoQuant.Application.Settings;
using MomoQuant.Application.Simulation;
using MomoQuant.Application.Trading;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.Users;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITradingSessionPreflightValidator, TradingSessionPreflightValidator>();
        services.AddScoped<IPipelineDiagnosticsService, PipelineDiagnosticsService>();
        services.AddScoped<ITradingSettingsService, TradingSettingsService>();
        services.AddScoped<ISimulationRunSummaryService, SimulationRunSummaryService>();

        services.AddScoped<IExportService, ExportService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IExchangeService, ExchangeService>();
        services.AddScoped<ISymbolService, SymbolService>();
        services.AddScoped<IBinanceFuturesSymbolService, BinanceFuturesSymbolService>();
        services.AddScoped<IMarketDataService, MarketDataService>();
        services.AddScoped<IIndicatorQueryService, IndicatorQueryService>();
        services.AddScoped<IIndicatorCalculationService, IndicatorCalculationService>();
        services.AddScoped<IStrategyService, StrategyService>();
        services.AddScoped<IStrategyDataRequirementService, StrategyDataRequirementService>();
        services.AddScoped<IStrategyEngine, StrategyEngine>();
        services.AddScoped<IStrategyParameterProvider, StrategyParameterProvider>();
        services.AddSingleton<IFourHourRangeService, FourHourRangeService>();

        services.AddSingleton<PositionSizingService>();
        services.AddSingleton<IRiskEngine, RiskEngine>();
        services.AddScoped<IRiskProfileService, RiskProfileService>();
        services.AddScoped<IRiskRuleService, RiskRuleService>();
        services.AddScoped<IRiskDecisionService, RiskDecisionService>();
        services.AddScoped<IRiskEvaluationService, RiskEvaluationService>();

        services.AddSingleton<ITradingStrategy, EmaPullbackStrategy>();
        services.AddSingleton<ITradingStrategy, VwapMeanReversionStrategy>();
        services.AddSingleton<ITradingStrategy, LiquiditySweepStrategy>();
        services.AddSingleton<ITradingStrategy, BollingerSqueezeBreakoutStrategy>();
        services.AddSingleton<ITradingStrategy, DonchianBreakoutStrategy>();
        services.AddSingleton<ITradingStrategy, RsiDivergenceReversalStrategy>();
        services.AddSingleton<ITradingStrategy, MacdMomentumContinuationStrategy>();
        services.AddSingleton<ITradingStrategy, AtrVolatilityBreakoutStrategy>();
        services.AddSingleton<ITradingStrategy, SupportResistanceBreakoutRetestStrategy>();
        services.AddSingleton<ITradingStrategy, SupertrendContinuationStrategy>();
        services.AddSingleton<IFourHourRangeService, FourHourRangeService>();
        services.AddSingleton<ITradingStrategy, FourHourRangeReEntryStrategy>();
        services.AddSingleton<IExternalLiquidityLineEngine, MomoLiquidityLineEngine>();
        services.AddSingleton<IBbLiquiditySweepContextService, BbLiquiditySweepContextService>();
        services.AddSingleton<IBbLiquiditySweepSessionTracker, BbLiquiditySweepSessionTracker>();
        services.AddSingleton<IBbLiquiditySweepFunnelTracker, BbLiquiditySweepFunnelTracker>();
        services.AddScoped<IBbLiquiditySweepBacktestBootstrap, BbLiquiditySweepBacktestBootstrap>();
        services.AddSingleton<ITradingStrategy>(sp => new BbLiquiditySweepCisdStrategy(
            sp.GetRequiredService<IBbLiquiditySweepContextService>(),
            new BbLiquiditySweepEvaluator(sp.GetRequiredService<IExternalLiquidityLineEngine>()),
            sp.GetRequiredService<IBbLiquiditySweepSessionTracker>(),
            sp.GetRequiredService<IBbLiquiditySweepFunnelTracker>()));
        services.AddSingleton<ITradingStrategy>(sp => new BbLiquiditySweepCisdRsiPrimedStrategy(
            sp.GetRequiredService<IBbLiquiditySweepContextService>(),
            new BbLiquiditySweepEvaluator(sp.GetRequiredService<IExternalLiquidityLineEngine>()),
            sp.GetRequiredService<IBbLiquiditySweepSessionTracker>(),
            sp.GetRequiredService<IBbLiquiditySweepFunnelTracker>()));
        services.AddSingleton<IVolatilityGatedSuperTrendContextService, VolatilityGatedSuperTrendContextService>();
        services.AddSingleton<IVolatilityGatedSuperTrendRetestTracker, VolatilityGatedSuperTrendRetestTracker>();
        services.AddSingleton<IVolatilityGatedSuperTrendFunnelTracker, VolatilityGatedSuperTrendFunnelTracker>();
        services.AddSingleton<ITradingStrategy>(sp => new VolatilityGatedSuperTrendMomentumStrategy(
            sp.GetRequiredService<IVolatilityGatedSuperTrendContextService>(),
            new VolatilityGatedSuperTrendEvaluator(),
            sp.GetRequiredService<IVolatilityGatedSuperTrendRetestTracker>(),
            sp.GetRequiredService<IVolatilityGatedSuperTrendFunnelTracker>()));
        services.AddSingleton<ITradingStrategy, PriceStructureBreakoutRetestStrategy>();
        services.AddSingleton<ITradingStrategy, PriceStructureLiquiditySweepReclaimStrategy>();
        services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

        services.AddSingleton<IStrategyParameterDefinitionProvider, StrategyParameterDefinitionProvider>();
        services.AddScoped<IValidationDateSplitService, ValidationDateSplitService>();
        services.AddScoped<IStrategyValidationEvaluator, StrategyValidationEvaluator>();
        services.AddScoped<IStrategyResearchBacktestExecutor, StrategyResearchBacktestExecutor>();
        services.AddScoped<IMarketDataCoverageService, MarketDataCoverageService>();
        services.AddScoped<IHistoricalCandleCoverageService, HistoricalCandleCoverageService>();
        services.AddScoped<IStrategyResearchCandleCoverageService, StrategyResearchCandleCoverageService>();
        services.AddScoped<IStrategyValidationService, StrategyValidationService>();
        services.AddScoped<IParameterOptimizationScorer, ParameterOptimizationScorer>();
        services.AddScoped<IParameterOptimizationService, ParameterOptimizationService>();
        services.AddScoped<ITargetOptimizationRulesEvaluator, TargetOptimizationRulesEvaluator>();
        services.AddScoped<ITargetParameterOptimizationService, TargetParameterOptimizationService>();
        services.AddScoped<IStrategyParameterSetService, StrategyParameterSetService>();

        services.AddScoped<IAiIntegrationService, AiIntegrationService>();
        services.AddScoped<IAiDecisionService, AiDecisionService>();
        services.AddScoped<IAiSetupAdvisorService, AiSetupAdvisorService>();

        services.AddScoped<IBacktestDataLoader, BacktestDataLoader>();
        services.AddScoped<IBacktestEngine>(sp => new BacktestEngine(
            sp.GetRequiredService<IStrategyEngine>(),
            sp.GetRequiredService<IStrategyParameterProvider>(),
            sp.GetRequiredService<IRiskEngine>(),
            sp.GetRequiredService<IAiIntegrationService>(),
            sp.GetRequiredService<ISimulatedExecutionProvider>(),
            sp.GetRequiredService<IBacktestProgressStore>(),
            sp.GetRequiredService<IBbLiquiditySweepBacktestBootstrap>(),
            sp.GetRequiredService<IBbLiquiditySweepSessionTracker>(),
            sp.GetRequiredService<IBbLiquiditySweepFunnelTracker>(),
            sp.GetRequiredService<IVolatilityGatedSuperTrendFunnelTracker>(),
            sp.GetRequiredService<ILogger<BacktestEngine>>()));
        services.AddScoped<IStrategyBacktestSliceRunner, StrategyBacktestSliceRunner>();
        services.AddScoped<IBacktestRunner, BacktestRunner>();
        services.AddSingleton<IBacktestProgressStore, BacktestProgressStore>();
        services.AddScoped<Backtesting.IBacktestReportService, Backtesting.BacktestReportService>();
        services.AddSingleton<IBacktestMetricsCalculator, BacktestMetricsCalculator>();
        services.AddSingleton<ISimulatedExecutionProvider, SimulatedExecutionProvider>();

        services.AddSingleton<IReplayStateStore, ReplayStateStore>();
        services.AddScoped<IReplayDataLoader, ReplayDataLoader>();
        services.AddScoped<IReplayEngine, ReplayEngine>();
        services.AddScoped<IReplayPersistenceService, ReplayPersistenceService>();
        services.AddScoped<IReplaySessionService, ReplaySessionService>();
        services.AddScoped<IReplayControlService, ReplayControlService>();
        services.AddScoped<IReplayFrameService, ReplayFrameService>();
        services.AddScoped<IReplayChartService, ReplayChartService>();

        services.AddSingleton<IPaperStateStore, PaperStateStore>();
        services.AddSingleton<ILiveMarketSnapshotStore, LiveMarketSnapshotStore>();
        services.AddSingleton<LiveMarketConnectionManager>();
        services.AddSingleton<ILiveMarketConnectionManager>(provider => provider.GetRequiredService<LiveMarketConnectionManager>());
        services.AddHostedService(provider => provider.GetRequiredService<LiveMarketConnectionManager>());
        services.AddScoped<ILiveCandlePersistenceService, LiveCandlePersistenceService>();
        services.AddScoped<ILiveIndicatorUpdateService, LiveIndicatorUpdateService>();
        services.AddScoped<ILiveMarketBootstrapService, LiveMarketBootstrapService>();
        services.AddScoped<ILivePaperCandleHandler, LivePaperCandleHandler>();
        services.AddScoped<IMarketSituationService, MarketSituationService>();
        services.AddScoped<IStrategyRecommendationService, StrategyRecommendationService>();
        services.AddScoped<ILiveMarketDataProvider, LiveMarketDataProviderAdapter>();
        services.AddScoped<IPaperExecutionProvider, PaperExecutionProvider>();
        services.AddScoped<IPaperTradingEngine, PaperTradingEngine>();
        services.AddScoped<IPaperPersistenceService, PaperPersistenceService>();
        services.AddScoped<IPaperAccountService, PaperAccountService>();
        services.AddScoped<IPaperSessionService, PaperSessionService>();
        services.AddScoped<IPaperSessionControlService, PaperSessionControlService>();
        services.AddScoped<IPaperSessionQueryService, PaperSessionQueryService>();
        services.AddScoped<ILivePaperChartService, LivePaperChartService>();
        services.AddHostedService<PaperTradingProgressService>();

        services.AddSingleton<IStrategyGradeService, StrategyGradeService>();
        services.AddSingleton<IRiskConfidenceCalibrationAdvisor, RiskConfidenceCalibrationAdvisor>();
        services.AddSingleton<IBenchmarkImportRangeChunker, BenchmarkImportRangeChunker>();
        services.AddScoped<IStrategyBenchmarkReportService, StrategyBenchmarkReportService>();
        services.AddScoped<IStrategyBenchmarkRunner, StrategyBenchmarkRunner>();
        services.AddScoped<IStrategyBenchmarkService, StrategyBenchmarkService>();
        services.AddSingleton<StrategyBenchmarkQueue>();
        services.AddSingleton<IStrategyBenchmarkQueue>(provider => provider.GetRequiredService<StrategyBenchmarkQueue>());
        services.AddHostedService(provider => provider.GetRequiredService<StrategyBenchmarkQueue>());

        services.AddSingleton<IStrategyLabCandleWindowFactory, CandlePrefixViewStrategyLabCandleWindowFactory>();
        services.AddScoped<IStrategyLabRunner, StrategyLabRunner>();
        services.AddScoped<IStrategyLabService, StrategyLabService>();
        services.AddScoped<IValidationCandidateReconciliationService, ValidationCandidateReconciliationService>();
        services.AddScoped<IValidationMetricConsistencyService, ValidationMetricConsistencyService>();
        services.AddScoped<IValidationLeakageAuditor, ValidationLeakageAuditor>();
        services.AddScoped<IValidationVerdictService, ValidationVerdictService>();
        services.AddScoped<IValidationHoldoutExclusivityService, ValidationHoldoutExclusivityService>();
        services.AddScoped<IValidationExportContentVerifier, ValidationExportContentVerifier>();
        services.AddScoped<IValidationMetricAuditService, ValidationMetricAuditService>();
        services.AddScoped<IValidationParameterFingerprintService, ValidationParameterFingerprintService>();
        services.AddScoped<IValidationTrainingSelectionService, ValidationTrainingSelectionService>();
        services.AddScoped<IValidationSelectionIntegrityService, ValidationSelectionIntegrityService>();
        services.AddScoped<IValidationRiskBasisService, ValidationRiskBasisService>();
        services.AddScoped<IValidationPathMetricInputBuilder, ValidationPathMetricInputBuilder>();
        services.AddScoped<IValidationTrialSelectionAuditor, ValidationTrialSelectionAuditor>();
        services.AddScoped<IValidationLaboratoryCloseoutService, ValidationLaboratoryCloseoutService>();
        services.AddScoped<IValidationLaboratoryReadinessService, ValidationLaboratoryReadinessService>();
        services.AddScoped<IValidationTrainingPreflightService, ValidationTrainingPreflightService>();
        services.AddScoped<IValidationTrainingExecutionLeaseService, ValidationTrainingExecutionLeaseService>();
        services.AddScoped<IValidationTrainingCandleScopeFactory, ValidationTrainingCandleScopeFactory>();
        services.AddScoped<IValidationCandleAccessRecorder, ValidationCandleAccessRecorder>();
        services.AddScoped<IValidationTrainingScopeExecution, ValidationTrainingScopeExecution>();
        services.AddScoped<IValidationSegmentResultWriter, ValidationSegmentResultWriter>();
        services.AddScoped<IValidationTrialRecoveryService, ValidationTrialRecoveryService>();
        services.AddScoped<IValidationLabService, ValidationLabService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IResearchOperationStatusService, ResearchOperationStatusService>();
        services.AddSingleton<ICandidateConfidenceScorer, StrategySetupQualityScorer>();
        services.AddSingleton<StrategyLabQueue>();
        services.AddSingleton<IStrategyLabQueue>(provider => provider.GetRequiredService<StrategyLabQueue>());
        services.AddHostedService(provider => provider.GetRequiredService<StrategyLabQueue>());

        services.AddScoped<IReportQueryValidator, ReportQueryValidator>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<Reports.IBacktestReportService, Reports.BacktestReportService>();
        services.AddScoped<IPaperTradingReportService, PaperTradingReportService>();
        services.AddScoped<IStrategyPerformanceReportService, StrategyPerformanceReportService>();
        services.AddScoped<ISymbolPerformanceReportService, SymbolPerformanceReportService>();
        services.AddScoped<IRiskReportService, RiskReportService>();
        services.AddScoped<IAiReportService, AiReportService>();
        services.AddScoped<IExecutionReportService, ExecutionReportService>();

        services.AddScoped<IMonitoringQueryValidator, MonitoringQueryValidator>();
        services.AddScoped<IAuditLogQueryValidator, AuditLogQueryValidator>();
        services.AddScoped<ISystemHealthLogService, SystemHealthLogService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IRecentErrorService, RecentErrorService>();
        services.AddScoped<ITradingPipelineStatusService, TradingPipelineStatusService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Trading Systems module (analysis only — never executes trades, benchmarks, or bots).
        services.AddSingleton<ITradingSystemService, TradingSystemService>();
        services.AddSingleton<ISwingStructureService, SwingStructureService>();
        services.AddSingleton<ISkSequenceAnalyzer, SkSequenceAnalyzer>();
        services.AddSingleton<ISkMultiTimeframeContextService, SkMultiTimeframeContextService>();
        services.AddScoped<ISkSystemAiSummaryService, SkSystemAiSummaryService>();
        services.AddScoped<ISkSystemAnalysisService, SkSystemAnalysisService>();
        services.AddScoped<ISkSystemPdfExportService, SkSystemPdfExportService>();

        // SK LivePaper — simulated orders only; separate from strategy paper trading.
        services.AddSingleton<SkLivePaperDiagnosticsStore>();
        services.AddScoped<ISkLivePaperSessionService, SkLivePaperSessionService>();
        services.AddScoped<ISkLivePaperEngine, SkLivePaperEngine>();
        services.AddScoped<ISkLivePaperCandleHandler, SkLivePaperCandleHandler>();

        return services;
    }
}
