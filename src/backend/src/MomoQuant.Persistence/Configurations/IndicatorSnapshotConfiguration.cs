using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Indicators;

namespace MomoQuant.Persistence.Configurations;

internal sealed class IndicatorSnapshotConfiguration : IEntityTypeConfiguration<IndicatorSnapshot>
{
    public void Configure(EntityTypeBuilder<IndicatorSnapshot> builder)
    {
        builder.ToTable("IndicatorSnapshots");

        builder.Property(snapshot => snapshot.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(snapshot => snapshot.Ema20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Ema50).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Ema200).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Vwap).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Rsi14).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Atr14).HasTradingDecimal();
        builder.Property(snapshot => snapshot.VolumeSma20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.SwingHigh).HasTradingDecimal();
        builder.Property(snapshot => snapshot.SwingLow).HasTradingDecimal();
        builder.Property(snapshot => snapshot.BollingerMiddle20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.BollingerUpper20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.BollingerLower20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.BollingerBandwidth20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.DonchianHigh20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.DonchianLow20).HasTradingDecimal();
        builder.Property(snapshot => snapshot.MacdLine).HasTradingDecimal();
        builder.Property(snapshot => snapshot.MacdSignal).HasTradingDecimal();
        builder.Property(snapshot => snapshot.MacdHistogram).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Supertrend).HasTradingDecimal();
        builder.Property(snapshot => snapshot.SupportLevel).HasTradingDecimal();
        builder.Property(snapshot => snapshot.ResistanceLevel).HasTradingDecimal();

        builder.Property(snapshot => snapshot.MarketStructure)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(snapshot => snapshot.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(snapshot => new { snapshot.SymbolId, snapshot.Timeframe, snapshot.CandleId }).IsUnique();

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.MarketData.Candle>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.CandleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
