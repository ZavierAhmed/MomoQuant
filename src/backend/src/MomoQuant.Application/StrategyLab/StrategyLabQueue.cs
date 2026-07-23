using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MomoQuant.Application.StrategyLab;

public interface IStrategyLabQueue
{
    void Enqueue(long runId);
}

public sealed class StrategyLabQueue : BackgroundService, IStrategyLabQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategyLabQueue> _logger;

    public StrategyLabQueue(IServiceScopeFactory scopeFactory, ILogger<StrategyLabQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(long runId)
    {
        if (!_channel.Writer.TryWrite(runId))
        {
            _logger.LogWarning("Failed to enqueue strategy lab run {RunId}.", runId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IStrategyLabRunner>();
                await runner.ExecuteAsync(runId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Strategy lab run {RunId} failed unexpectedly.", runId);
            }
        }
    }
}
