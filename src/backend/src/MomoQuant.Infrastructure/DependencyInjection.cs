using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.Options;
using MomoQuant.Infrastructure.Ai;
using MomoQuant.Infrastructure.Exchanges;
using MomoQuant.Infrastructure.MarketData;
using MomoQuant.Infrastructure.Monitoring;
using MomoQuant.Infrastructure.Security;
using MomoQuant.Application.Monitoring.Abstractions;

namespace MomoQuant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<SeedSettings>(configuration.GetSection(SeedSettings.SectionName));
        services.Configure<MarketDataSettings>(configuration.GetSection(MarketDataSettings.SectionName));
        services.Configure<StrategyBenchmarkSettings>(configuration.GetSection(StrategyBenchmarkSettings.SectionName));
        services.Configure<StrategyCatalogSettings>(configuration.GetSection(StrategyCatalogSettings.SectionName));
        services.Configure<SkSystemSettings>(configuration.GetSection(SkSystemSettings.SectionName));
        services.Configure<BbLiquiditySweepSettings>(configuration.GetSection(BbLiquiditySweepSettings.SectionName));
        services.Configure<ExportSettings>(configuration.GetSection(ExportSettings.SectionName));
        services.Configure<AiIntegrationOptions>(configuration.GetSection(AiIntegrationOptions.SectionName));

        services.AddHttpClient<IAiServiceClient, AiServiceClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AiIntegrationOptions>>().Value;
            var baseUrl = options.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri($"{baseUrl}/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, 1));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IExchangeSymbolProvider, FakeExchangeSymbolProvider>();
        services.AddScoped<IExchangeConnectivityProvider, FakeExchangeConnectivityProvider>();

        services.AddScoped<IHealthCheckProvider, ApiHealthCheckProvider>();
        services.AddScoped<IHealthCheckProvider, RedisHealthCheckProvider>();
        services.AddScoped<IHealthCheckProvider, AiServiceHealthCheckProvider>();

        services.AddHttpClient<BinanceHistoricalCandleProvider>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MarketDataSettings>>().Value;
            var baseUrl = settings.Binance.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri($"{baseUrl}/");
        });

        services.AddHttpClient<IBinanceFuturesSymbolDiscoveryService, BinanceFuturesSymbolDiscoveryService>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MarketDataSettings>>().Value;
            var baseUrl = settings.Binance.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri($"{baseUrl}/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<FakeHistoricalCandleProvider>();
        services.AddScoped<IHistoricalCandleProvider>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MarketDataSettings>>().Value;
            return IsBinanceProvider(settings.HistoricalProvider)
                ? serviceProvider.GetRequiredService<BinanceHistoricalCandleProvider>()
                : serviceProvider.GetRequiredService<FakeHistoricalCandleProvider>();
        });

        services.AddSingleton<BinanceLiveWebSocketClient>();
        services.AddSingleton<IBinanceLiveWebSocketClient>(provider => provider.GetRequiredService<BinanceLiveWebSocketClient>());
        services.AddSingleton<BinanceLiveMarketDataProvider>();
        services.AddSingleton<ILiveKlineStreamProvider, BinanceLiveKlineStreamProvider>();

        return services;
    }

    private static bool IsBinanceProvider(string? provider) =>
        string.Equals(provider, "Binance", StringComparison.OrdinalIgnoreCase);
}
