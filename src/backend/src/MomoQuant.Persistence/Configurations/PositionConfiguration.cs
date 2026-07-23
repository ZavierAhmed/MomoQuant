using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Persistence.Configurations;

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions");

        builder.Property(position => position.Direction)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(position => position.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(position => position.Quantity).HasTradingDecimal();
        builder.Property(position => position.AverageEntryPrice).HasTradingDecimal();
        builder.Property(position => position.MarkPrice).HasTradingDecimal();
        builder.Property(position => position.UnrealizedPnl).HasTradingDecimal();
        builder.Property(position => position.RealizedPnl).HasTradingDecimal();
        builder.Property(position => position.Leverage).HasTradingDecimal();
        builder.Property(position => position.MarginUsed).HasTradingDecimal();

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(position => position.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(position => position.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
