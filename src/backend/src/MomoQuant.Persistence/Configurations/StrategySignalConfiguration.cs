using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Signals;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategySignalConfiguration : IEntityTypeConfiguration<StrategySignal>
{
    public void Configure(EntityTypeBuilder<StrategySignal> builder)
    {
        builder.ToTable("StrategySignals");

        builder.Property(signal => signal.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(signal => signal.SignalType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(signal => signal.Direction)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(signal => signal.Strength).HasTradingDecimal();
        builder.Property(signal => signal.ConfidenceContribution).HasTradingDecimal();
        builder.Property(signal => signal.EntryPrice).HasTradingDecimal();
        builder.Property(signal => signal.SuggestedStopLoss).HasTradingDecimal();
        builder.Property(signal => signal.SuggestedTakeProfit).HasTradingDecimal();

        builder.Property(signal => signal.Reason)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(signal => signal.RawDataJson)
            .HasColumnType("longtext");

        builder.Property(signal => signal.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(signal => signal.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Strategies.Strategy>()
            .WithMany()
            .HasForeignKey(signal => signal.StrategyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(signal => signal.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.MarketData.Candle>()
            .WithMany()
            .HasForeignKey(signal => signal.CandleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
