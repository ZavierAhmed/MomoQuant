using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Persistence.Configurations;

internal sealed class CandleConfiguration : IEntityTypeConfiguration<Candle>
{
    public void Configure(EntityTypeBuilder<Candle> builder)
    {
        builder.ToTable("Candles");

        builder.Property(candle => candle.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(candle => candle.Open).HasTradingDecimal();
        builder.Property(candle => candle.High).HasTradingDecimal();
        builder.Property(candle => candle.Low).HasTradingDecimal();
        builder.Property(candle => candle.Close).HasTradingDecimal();
        builder.Property(candle => candle.Volume).HasTradingDecimal();
        builder.Property(candle => candle.QuoteVolume).HasTradingDecimal();

        builder.Property(candle => candle.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(candle => new
        {
            candle.ExchangeId,
            candle.SymbolId,
            candle.Timeframe,
            candle.OpenTimeUtc
        }).IsUnique();

        builder.HasIndex(candle => new { candle.SymbolId, candle.Timeframe, candle.OpenTimeUtc });
        builder.HasIndex(candle => candle.OpenTimeUtc);

        builder.HasOne<Domain.Exchanges.Exchange>()
            .WithMany()
            .HasForeignKey(candle => candle.ExchangeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(candle => candle.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
