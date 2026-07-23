using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Persistence.Configurations;

internal sealed class MarketDataImportConfiguration : IEntityTypeConfiguration<MarketDataImport>
{
    public void Configure(EntityTypeBuilder<MarketDataImport> builder)
    {
        builder.ToTable("MarketDataImports");

        builder.Property(import => import.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(import => import.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(import => import.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(import => import.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(import => import.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(import => new { import.SymbolId, import.Timeframe, import.StartedAtUtc });

        builder.HasOne<Domain.Exchanges.Exchange>()
            .WithMany()
            .HasForeignKey(import => import.ExchangeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(import => import.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(import => import.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
