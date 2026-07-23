using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.Persistence.Seeding;

public interface IExchangeDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public sealed class ExchangeDataSeeder : IExchangeDataSeeder
{
    public const string BinanceFuturesCode = "BINANCE_FUTURES";

    private readonly MomoQuantDbContext _dbContext;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ExchangeDataSeeder> _logger;

    public ExchangeDataSeeder(
        MomoQuantDbContext dbContext,
        IHostEnvironment hostEnvironment,
        ILogger<ExchangeDataSeeder> logger)
    {
        _dbContext = dbContext;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_hostEnvironment.IsDevelopment())
        {
            return;
        }

        var exists = await _dbContext.Exchanges.AnyAsync(
            e => e.Code == BinanceFuturesCode,
            cancellationToken);

        if (exists)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _dbContext.Exchanges.Add(new Exchange
        {
            Name = "Binance Futures",
            Code = BinanceFuturesCode,
            BaseUrl = "https://fapi.binance.com",
            WebSocketUrl = "wss://fstream.binance.com",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded development exchange {ExchangeCode}", BinanceFuturesCode);
        }
        catch (DbUpdateException ex) when (IsDuplicateExchangeException(ex))
        {
            _logger.LogDebug(ex, "Exchange seed skipped because another process already created the exchange.");
        }
    }

    private static bool IsDuplicateExchangeException(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("IX_Exchanges_Code", StringComparison.OrdinalIgnoreCase) == true;
}
