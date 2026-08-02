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
        services.AddScoped<IStrategyExecutionRequirementsResolver, StrategyExecutionRequirementsResolver>();
        services.AddScoped<IStrategyEngine>(sp =>
            new StrategyEngine(sp.GetService<IStrategyEvaluationCapture>()));
        services.AddScoped<IStrategyParameterProvider, StrategyParameterProvider>();
        services.AddSingleton<IFourHourRangeService, FourHourRangeService>();

        services.AddSingleton<PositionSizingService>();
        services.AddSingleton<IRiskEngine, RiskEngine>();
        services.AddScoped<IRiskProfileService, RiskProfileService>();
        services.AddScoped<IRiskRuleService, RiskRuleService>();
        services.AddScoped<IRiskDecisionService, RiskDecisionService>();
        services.AddScoped<IRiskEvaluationService, RiskEvaluationService>();

        services.AddSingleton<ITradingStrategy, MomoAdaptiveMultiTimeframeTrendBreakoutStrategy>();
        services.AddSingleton<ITradingStrategy, PriceStructureBreakoutRetestStrategy>();
        services.AddSingleton<ITradingStrategy, MomoVolatilityRangeReversionStrategy>();
        services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

        // Legacy strategy supporting services remain registered for historical backtest/report infrastructure,
        // but legacy ITradingStrategy plugins are not registered (canonical portfolio only).
        services.AddSingleton<IExternalLiquidityLineEngine, MomoLiquidityLineEngine>();
        services.AddSingleton<IBbLiquiditySweepContextService, BbLiquiditySweepContextService>();
        services.AddSingleton<IBbLiquiditySweepSessionTracker, BbLiquiditySweepSessionTracker>();
        services.AddSingleton<IBbLiquiditySweepFunnelTracker, BbLiquiditySweepFunnelTracker>();
        services.AddScoped<IBbLiquiditySweepBacktestBootstrap, BbLiquiditySweepBacktestBootstrap>();
        services.AddSingleton<IVolatilityGatedSuperTrendContextService, VolatilityGatedSuperTrendContextService>();
        services.AddSingleton<IVolatilityGatedSuperTrendRetestTracker, VolatilityGatedSuperTrendRetestTracker>();
        services.AddSingleton<IVolatilityGatedSuperTrendFunnelTracker, VolatilityGatedSuperTrendFunnelTracker>();

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
        services.AddSingleton<IResearchExecutionContextAccessor, ResearchExecutionContextAccessor>();
        services.AddScoped<IHigherTimeframeDatasetEnricher, HigherTimeframeDatasetEnricher>();
        services.AddScoped<StandardStrategyLabCandleDataSource>(sp =>
            new StandardStrategyLabCandleDataSource(
                sp.GetRequiredService<IBacktestDataLoader>(),
                sp.GetRequiredService<IHigherTimeframeDatasetEnricher>(),
                sp.GetRequiredService<IStrategyRegistry>()));
        services.AddScoped<IBacktestEngine>(sp => new BacktestEngine(
            sp.GetRequiredService<IStrategyEngine>(),
            sp.GetRequiredService<IStrategyParameterProvider>(),
            sp.GetRequiredService<IRiskEngine>(),
            sp.GetRequiredService<IAiIntegrationService>(),
            sp.GetRequiredService<ISimulatedExecutionProvider>(),
            sp.GetRequiredService<IHigherTimeframeDatasetEnricher>(),
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
        services.AddScoped<IPaperDeploymentQualificationVerifier, PaperDeploymentQualificationVerifier>();
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
        services.AddScoped<IValidationRiskBasisStatusReducer, ValidationRiskBasisStatusReducer>();
        services.AddScoped<IValidationPathMetricInputBuilder, ValidationPathMetricInputBuilder>();
        services.AddScoped<IValidationTrialSelectionAuditor, ValidationTrialSelectionAuditor>();
        services.AddScoped<IValidationLaboratoryCloseoutService, ValidationLaboratoryCloseoutService>();
        services.AddScoped<IValidationLaboratoryReadinessService, ValidationLaboratoryReadinessService>();
        services.AddScoped<IValidationTrainingPreflightService, ValidationTrainingPreflightService>();
        services.AddScoped<IValidationTrainingExecutionLeaseService, ValidationTrainingExecutionLeaseService>();
        services.AddScoped<IValidationTrainingCandleScopeFactory, ValidationTrainingCandleScopeFactory>();
        services.AddSingleton<IValidationAccessPayloadCanonicalizer, ValidationAccessPayloadCanonicalizer>();
        services.AddSingleton<IValidationAccessPersistenceRetryPolicy>(_ => new ValidationAccessPersistenceRetryPolicy());
        services.AddSingleton<IValidationAuditPayloadSetHasher, ValidationAuditPayloadSetHasher>();
        services.AddScoped<IValidationAuditCompletenessVerifier, ValidationAuditCompletenessVerifier>();
        services.AddScoped<IValidationAuthoritativeAuditQualificationEvaluator, ValidationAuthoritativeAuditQualificationEvaluator>();
        services.AddScoped<IValidationParameterSetPublicationService, ValidationParameterSetPublicationService>();
        services.AddScoped<IValidationAuditExecutionFactory, ValidationAuditExecutionService>();
        services.AddScoped<IValidationAuditExecutionSupersessionService, ValidationAuditExecutionSupersessionService>();
        services.AddScoped<IValidationAuditExecutionRecoveryService, ValidationAuditExecutionRecoveryService>();
        services.AddScoped<IValidationAuditExecutionFinalizer, ValidationAuditExecutionFinalizer>();
        services.AddScoped<IValidationTrialAuditCompletionGate, ValidationTrialAuditCompletionGate>();
        services.AddScoped<IValidationCandleAccessRecorder, ValidationCandleAccessRecorder>();
        services.AddScoped<IValidationTrainingFailureHandler, ValidationTrainingFailureHandler>();
        services.AddScoped<IValidationTrainingScopeExecution, ValidationTrainingScopeExecution>();
        services.AddScoped<IValidationSegmentResultWriter, ValidationSegmentResultWriter>();
        services.AddScoped<IValidationTrialMetricsCalculator, ValidationTrialMetricsCalculator>();
        services.AddScoped<IValidationLegacyTrialMetricsMapper, ValidationLegacyTrialMetricsMapper>();
        services.AddScoped<IValidationTrialMetricsRouter, ValidationTrialMetricsRouter>();
        services.AddScoped<IValidationTrialSegmentReconciliationService, ValidationTrialSegmentReconciliationService>();
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
