using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MomoQuant.Persistence.Configurations;

internal static class PropertyBuilderExtensions
{
    public static PropertyBuilder<decimal> HasTradingDecimal(this PropertyBuilder<decimal> builder)
    {
        return builder.HasColumnType(PersistenceConstants.TradingDecimalType);
    }

    public static PropertyBuilder<decimal?> HasTradingDecimal(this PropertyBuilder<decimal?> builder)
    {
        return builder.HasColumnType(PersistenceConstants.TradingDecimalType);
    }
}

internal static class EntityTypeBuilderExtensions
{
    public static void ConfigureAuditableEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<DateTime>("CreatedAtUtc")
            .HasColumnName("CreatedAt")
            .IsRequired();

        builder.Property<DateTime?>("UpdatedAtUtc")
            .HasColumnName("UpdatedAt");
    }

    public static void ConfigureCreatedAtUtc<TEntity>(this EntityTypeBuilder<TEntity> builder, string propertyName = "CreatedAtUtc")
        where TEntity : class
    {
        builder.Property<DateTime>(propertyName)
            .HasColumnName("CreatedAt")
            .IsRequired();
    }
}
