using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Admin;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Persistence.Monitoring;
using MomoQuant.Persistence.Repositories;
using MomoQuant.Persistence.Seeding;
using MomoQuant.Persistence.Services;

namespace MomoQuant.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<MomoQuantDbContext>(options =>
            options.UseMySql(
                connectionString,
                ServerVersion.Parse(PersistenceConstants.MySqlServerVersion)));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IExchangeRepository, ExchangeRepository>();
        services.AddScoped<ISymbolRepository, SymbolRepository>();
        services.AddScoped<CandleRepository>();
        services.AddScoped<ICandleRepository, TrainingBoundaryCandleRepository>();
        services.AddScoped<IUnscopedCandleReader>(sp =>
            (IUnscopedCandleReader)sp.GetRequiredService<ICandleRepository>());
        services.AddScoped<IMarketDataImportRepository, MarketDataImportRepository>();
        services.AddScoped<IIndicatorSnapshotRepository, IndicatorSnapshotRepository>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IStrategyParameterRepository, StrategyParameterRepository>();
        services.AddScoped<IRiskProfileRepository, RiskProfileRepository>();
        services.AddScoped<IRiskRuleRepository, RiskRuleRepository>();
        services.AddScoped<IRiskDecisionRepository, RiskDecisionRepository>();
        services.AddScoped<IAiDecisionRepository, AiDecisionRepository>();
        services.AddScoped<IStrategySignalRepository, StrategySignalRepository>();
        services.AddScoped<IBacktestRunRepository, BacktestRunRepository>();
        services.AddScoped<IBacktestResultRepository, BacktestResultRepository>();
        services.AddScoped<IBacktestEquityPointRepository, BacktestEquityPointRepository>();
        services.AddScoped<IBacktestStrategyResultRepository, BacktestStrategyResultRepository>();
        services.AddScoped<IBacktestSymbolResultRepository, BacktestSymbolResultRepository>();
        services.AddScoped<IStrategyBenchmarkRunRepository, StrategyBenchmarkRunRepository>();
        services.AddScoped<IStrategyBenchmarkResultRepository, StrategyBenchmarkResultRepository>();
        services.AddScoped<IStrategyBenchmarkRunItemRepository, StrategyBenchmarkRunItemRepository>();
        services.AddScoped<ITradingSessionRepository, TradingSessionRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderFillRepository, OrderFillRepository>();
        services.AddScoped<IMissedOrderRepository, MissedOrderRepository>();
        services.AddScoped<IReplaySessionRepository, ReplaySessionRepository>();
        services.AddScoped<IReplayFrameRepository, ReplayFrameRepository>();
        services.AddScoped<IPaperAccountRepository, PaperAccountRepository>();
        services.AddScoped<IPaperAccountSnapshotRepository, PaperAccountSnapshotRepository>();
        services.AddScoped<IPaperTradingSessionRepository, PaperTradingSessionRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IReportDataRepository, ReportDataRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ISystemHealthLogRepository, SystemHealthLogRepository>();
        services.AddScoped<ITradingSettingsRepository, TradingSettingsRepository>();
        services.AddScoped<IMonitoringDataRepository, MonitoringDataRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IFakeMarketDataCleanupService, FakeMarketDataCleanupService>();
        services.AddScoped<ICleanBaselineService, CleanBaselineService>();
        services.AddScoped<ISimulationRunSummaryRepository, SimulationRunSummaryRepository>();
        services.AddScoped<ITradingSystemAnalysisRepository, TradingSystemAnalysisRepository>();
        services.AddScoped<ISkLivePaperSessionRepository, SkLivePaperSessionRepository>();
        services.AddScoped<ISkLivePaperCandidateRepository, SkLivePaperCandidateRepository>();
        services.AddScoped<ISkLivePaperTradeRepository, SkLivePaperTradeRepository>();
        services.AddScoped<ISkLivePaperEventRepository, SkLivePaperEventRepository>();
        services.AddScoped<IExportJobRepository, ExportJobRepository>();
        services.AddScoped<IStrategyParameterSetRepository, StrategyParameterSetRepository>();
        services.AddScoped<IParameterOptimizationRunRepository, ParameterOptimizationRunRepository>();
        services.AddScoped<ITargetOptimizationRunRepository, TargetOptimizationRunRepository>();
        services.AddScoped<IStrategyLabRunRepository, StrategyLabRunRepository>();
        services.AddScoped<IStrategyResearchCandidateRepository, StrategyResearchCandidateRepository>();
        services.AddScoped<IValidationExperimentRepository, ValidationExperimentRepository>();
        services.AddScoped<IValidationParameterTrialRepository, ValidationParameterTrialRepository>();
        services.AddScoped<IValidationSegmentResultRepository, ValidationSegmentResultRepository>();
        services.AddScoped<IValidationExperimentExecutionLeaseRepository, ValidationExperimentExecutionLeaseRepository>();
        services.AddScoped<IValidationCandleAccessAuditRepository, ValidationCandleAccessAuditRepository>();
        services.AddScoped<IValidationTrainingDatabaseProbe, ValidationTrainingDatabaseProbe>();

        services.AddScoped<IHealthCheckProvider, DatabaseHealthCheckProvider>();
        services.AddScoped<ISubsystemHealthCheckProvider, SubsystemHealthCheckProvider>();
        services.AddScoped<IIdentityDataSeeder, IdentityDataSeeder>();
        services.AddScoped<IExchangeDataSeeder, ExchangeDataSeeder>();
        services.AddScoped<IStrategyDataSeeder, StrategyDataSeeder>();
        services.AddScoped<IRiskDataSeeder, RiskDataSeeder>();

        return services;
    }
}
