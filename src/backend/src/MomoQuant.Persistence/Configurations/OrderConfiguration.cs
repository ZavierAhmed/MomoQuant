using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Execution;

namespace MomoQuant.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.Property(order => order.ExternalOrderId)
            .HasMaxLength(128);

        builder.Property(order => order.Mode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(order => order.Side)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(order => order.OrderType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(order => order.PositionSide)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(order => order.TimeInForce)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(order => order.Price).HasTradingDecimal();
        builder.Property(order => order.Quantity).HasTradingDecimal();

        builder.Property(order => order.FailureReason)
            .HasMaxLength(2000);

        builder.Property(order => order.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(order => order.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(order => order.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(order => order.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Trades.Trade>()
            .WithMany()
            .HasForeignKey(order => order.TradeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
