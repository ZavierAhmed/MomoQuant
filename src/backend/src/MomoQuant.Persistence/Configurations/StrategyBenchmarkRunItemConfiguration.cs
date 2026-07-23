using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Benchmarks;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyBenchmarkRunItemConfiguration : IEntityTypeConfiguration<StrategyBenchmarkRunItem>
{
    public void Configure(EntityTypeBuilder<StrategyBenchmarkRunItem> builder)
    {
        builder.ToTable("StrategyBenchmarkRunItems");

        builder.Property(item => item.StrategyCode).HasMaxLength(64).IsRequired();
        builder.Property(item => item.StrategyName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(item => item.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.ErrorMessage).HasMaxLength(4000);
        builder.Property(item => item.ResultJson).HasColumnType("longtext");
        builder.Property(item => item.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();
        builder.Property(item => item.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(item => item.BenchmarkRunId);
        builder.HasIndex(item => new { item.BenchmarkRunId, item.Status });
        builder.HasIndex(item => new { item.BenchmarkRunId, item.StrategyId, item.SymbolId, item.Timeframe });

        builder.HasOne<StrategyBenchmarkRun>()
            .WithMany()
            .HasForeignKey(item => item.BenchmarkRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
