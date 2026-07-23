using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyParameterSetConfiguration : IEntityTypeConfiguration<StrategyParameterSet>
{
    public void Configure(EntityTypeBuilder<StrategyParameterSet> builder)
    {
        builder.ToTable("StrategyParameterSets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StrategyCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(x => x.MarketRegime).HasMaxLength(64);
        builder.Property(x => x.ParametersJson).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.TrainingRangeJson).HasColumnType("longtext");
        builder.Property(x => x.ValidationRangeJson).HasColumnType("longtext");
        builder.Property(x => x.TrainingMetricsJson).HasColumnType("longtext");
        builder.Property(x => x.ValidationMetricsJson).HasColumnType("longtext");
        builder.Property(x => x.RobustnessScore).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.StrategyCode, x.Timeframe });
        builder.HasIndex(x => new { x.StrategyCode, x.SymbolId, x.Timeframe });
    }
}
