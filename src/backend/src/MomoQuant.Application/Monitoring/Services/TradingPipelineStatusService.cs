using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring.Services;

public interface ITradingPipelineStatusService
{
    Task<ServiceResult<TradingPipelineStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class TradingPipelineStatusService : ITradingPipelineStatusService
{
    private readonly IMonitoringDataRepository _monitoringDataRepository;
    private readonly IHealthCheckProvider _aiHealthProvider;

    public TradingPipelineStatusService(
        IMonitoringDataRepository monitoringDataRepository,
        IEnumerable<IHealthCheckProvider> healthCheckProviders)
    {
        _monitoringDataRepository = monitoringDataRepository;
        _aiHealthProvider = healthCheckProviders.First(provider => provider.ComponentName == "AI Service");
    }

    public async Task<ServiceResult<TradingPipelineStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var marketDataAvailable = await _monitoringDataRepository.HasMarketDataAsync(cancellationToken);
        var indicatorsAvailable = await _monitoringDataRepository.HasIndicatorsAsync(cancellationToken);
        var aiHealth = await _aiHealthProvider.CheckAsync(cancellationToken);
        var emergencyStopEnabled = await _monitoringDataRepository.GetEmergencyStopEnabledAsync(cancellationToken);

        if (!marketDataAvailable)
        {
            warnings.Add("Market data is not available.");
        }

        if (!indicatorsAvailable)
        {
            warnings.Add("Indicator snapshots are not available.");
        }

        if (aiHealth.Status is SystemHealthStatus.Degraded or SystemHealthStatus.Unhealthy)
        {
            warnings.Add(aiHealth.Message);
        }

        if (emergencyStopEnabled)
        {
            warnings.Add("Emergency stop is enabled.");
        }

        return ServiceResult<TradingPipelineStatusDto>.Ok(new TradingPipelineStatusDto
        {
            MarketDataAvailable = marketDataAvailable,
            IndicatorsAvailable = indicatorsAvailable,
            StrategiesEnabled = await _monitoringDataRepository.CountEnabledStrategiesAsync(cancellationToken),
            RiskProfilesAvailable = await _monitoringDataRepository.CountRiskProfilesAsync(cancellationToken) > 0,
            AiServiceAvailable = aiHealth.Status == SystemHealthStatus.Healthy,
            BacktestingAvailable = marketDataAvailable && indicatorsAvailable,
            ReplayAvailable = marketDataAvailable && indicatorsAvailable,
            PaperTradingAvailable = marketDataAvailable && indicatorsAvailable,
            LatestCandleTimeUtc = await _monitoringDataRepository.GetLatestCandleTimeUtcAsync(cancellationToken),
            LatestIndicatorSnapshotTimeUtc = await _monitoringDataRepository.GetLatestIndicatorSnapshotTimeUtcAsync(cancellationToken),
            OpenPaperPositions = await _monitoringDataRepository.CountOpenPaperPositionsAsync(cancellationToken),
            EmergencyStopEnabled = emergencyStopEnabled,
            Warnings = warnings
        });
    }
}
