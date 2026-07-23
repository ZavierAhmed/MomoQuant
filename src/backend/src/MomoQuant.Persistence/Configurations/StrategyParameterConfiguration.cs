using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyParameterConfiguration : IEntityTypeConfiguration<StrategyParameter>
{
    public void Configure(EntityTypeBuilder<StrategyParameter> builder)
    {
        builder.ToTable("StrategyParameters");

        builder.Property(parameter => parameter.ParameterKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(parameter => parameter.ParameterValue)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(parameter => parameter.ValueType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(parameter => parameter.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(parameter => parameter.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(parameter => parameter.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(parameter => new { parameter.StrategyId, parameter.ParameterKey, parameter.Timeframe, parameter.SymbolId })
            .IsUnique();

        builder.HasOne<Strategy>()
            .WithMany()
            .HasForeignKey(parameter => parameter.StrategyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(parameter => parameter.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
