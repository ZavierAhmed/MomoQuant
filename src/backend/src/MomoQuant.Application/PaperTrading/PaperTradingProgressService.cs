using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;

namespace MomoQuant.Application.PaperTrading;

public sealed class PaperTradingProgressService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaperTradingProgressService> _logger;

    public PaperTradingProgressService(IServiceScopeFactory scopeFactory, ILogger<PaperTradingProgressService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Paper trading progress service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPaperTradingSessionRepository>();
                var controlService = scope.ServiceProvider.GetRequiredService<IPaperSessionControlService>();

                var runningIds = await repository.GetRunningSessionIdsAsync(stoppingToken);
                foreach (var sessionId in runningIds)
                {
                    var session = await repository.GetByIdAsync(sessionId, stoppingToken);
                    if (session is null || session.Mode != Domain.Enums.PaperTradingMode.HistoricalPaper)
                    {
                        continue;
                    }

                    var result = await controlService.TickAsync(sessionId, stoppingToken);
                    if (!result.Succeeded)
                    {
                        _logger.LogDebug("Paper tick skipped for session {SessionId}: {Message}", sessionId, result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Paper trading progress loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
