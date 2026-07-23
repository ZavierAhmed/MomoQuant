using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.Persistence.Configurations;

internal sealed class SymbolConfiguration : IEntityTypeConfiguration<Symbol>
{
    public void Configure(EntityTypeBuilder<Symbol> builder)
    {
        builder.ToTable("Symbols");

        builder.Property(symbol => symbol.SymbolName)
            .HasColumnName("Symbol")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(symbol => symbol.BaseAsset)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(symbol => symbol.QuoteAsset)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(symbol => symbol.ContractType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(symbol => symbol.MinQty).HasTradingDecimal();
        builder.Property(symbol => symbol.MinNotional).HasTradingDecimal();
        builder.Property(symbol => symbol.TickSize).HasTradingDecimal();
        builder.Property(symbol => symbol.StepSize).HasTradingDecimal();
        builder.Property(symbol => symbol.MakerFeeRate).HasTradingDecimal();
        builder.Property(symbol => symbol.TakerFeeRate).HasTradingDecimal();

        builder.Property(symbol => symbol.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(symbol => symbol.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(symbol => new { symbol.ExchangeId, symbol.SymbolName }).IsUnique();

        builder.HasOne<Exchange>()
            .WithMany()
            .HasForeignKey(symbol => symbol.ExchangeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
