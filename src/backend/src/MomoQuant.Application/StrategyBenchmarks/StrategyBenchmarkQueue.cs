using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MomoQuant.Application.StrategyBenchmarks;

public interface IStrategyBenchmarkQueue
{
    void Enqueue(long benchmarkRunId);
}

public sealed class StrategyBenchmarkQueue : BackgroundService, IStrategyBenchmarkQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategyBenchmarkQueue> _logger;

    public StrategyBenchmarkQueue(IServiceScopeFactory scopeFactory, ILogger<StrategyBenchmarkQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(long benchmarkRunId)
    {
        if (!_channel.Writer.TryWrite(benchmarkRunId))
        {
            _logger.LogWarning("Failed to enqueue strategy benchmark run {BenchmarkRunId}.", benchmarkRunId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var benchmarkRunId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IStrategyBenchmarkRunner>();
                await runner.ExecuteAsync(benchmarkRunId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Strategy benchmark run {BenchmarkRunId} failed unexpectedly.", benchmarkRunId);
            }
        }
    }
}
