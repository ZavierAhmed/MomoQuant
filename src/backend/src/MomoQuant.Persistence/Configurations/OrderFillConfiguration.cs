using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Execution;

namespace MomoQuant.Persistence.Configurations;

internal sealed class OrderFillConfiguration : IEntityTypeConfiguration<OrderFill>
{
    public void Configure(EntityTypeBuilder<OrderFill> builder)
    {
        builder.ToTable("OrderFills");

        builder.Property(fill => fill.ExternalFillId)
            .HasMaxLength(128);

        builder.Property(fill => fill.FillPrice).HasTradingDecimal();
        builder.Property(fill => fill.FillQuantity).HasTradingDecimal();
        builder.Property(fill => fill.Fee).HasTradingDecimal();

        builder.Property(fill => fill.FeeAsset)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(fill => fill.LiquidityType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(fill => fill.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(fill => fill.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
