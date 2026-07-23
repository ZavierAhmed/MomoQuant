using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Execution;

namespace MomoQuant.Persistence.Configurations;

internal sealed class MissedOrderConfiguration : IEntityTypeConfiguration<MissedOrder>
{
    public void Configure(EntityTypeBuilder<MissedOrder> builder)
    {
        builder.ToTable("MissedOrders");

        builder.Property(missedOrder => missedOrder.RequestedPrice).HasTradingDecimal();
        builder.Property(missedOrder => missedOrder.BestBid).HasTradingDecimal();
        builder.Property(missedOrder => missedOrder.BestAsk).HasTradingDecimal();

        builder.Property(missedOrder => missedOrder.Reason)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(missedOrder => missedOrder.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(missedOrder => missedOrder.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Signals.StrategySignal>()
            .WithMany()
            .HasForeignKey(missedOrder => missedOrder.SignalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(missedOrder => missedOrder.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
