using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> builder)
    {
        builder.ToTable("Strategies");

        builder.Property(strategy => strategy.Code)
            .HasConversion(
                code => code.ToCode(),
                value => StrategyCodeExtensions.FromCode(value))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(strategy => strategy.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(strategy => strategy.Description)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(strategy => strategy.Version)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(strategy => strategy.ResearchStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(strategy => strategy.ResearchDecisionJson).HasColumnType("longtext");

        builder.Property(strategy => strategy.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(strategy => strategy.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(strategy => strategy.Code).IsUnique();
    }
}
