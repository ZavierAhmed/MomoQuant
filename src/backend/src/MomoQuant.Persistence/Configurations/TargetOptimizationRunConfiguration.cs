using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Optimization;

namespace MomoQuant.Persistence.Configurations;

internal sealed class TargetOptimizationRunConfiguration : IEntityTypeConfiguration<TargetOptimizationRun>
{
    public void Configure(EntityTypeBuilder<TargetOptimizationRun> builder)
    {
        builder.ToTable("TargetOptimizationRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StrategyCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ValidationSplitMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ParameterSearchMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TargetRulesJson).HasColumnType("longtext");
        builder.Property(x => x.ResultJson).HasColumnType("longtext");
        builder.Property(x => x.WarningsJson).HasColumnType("longtext");
        builder.Property(x => x.CurrentParametersJson).HasColumnType("longtext");
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => new { x.StrategyCode, x.SymbolId, x.Timeframe });
    }
}
