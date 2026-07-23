using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.Persistence.Configurations;

internal sealed class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
{
    public void Configure(EntityTypeBuilder<Exchange> builder)
    {
        builder.ToTable("Exchanges");

        builder.Property(exchange => exchange.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(exchange => exchange.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(exchange => exchange.BaseUrl)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(exchange => exchange.WebSocketUrl)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(exchange => exchange.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(exchange => exchange.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(exchange => exchange.Code).IsUnique();
    }
}
