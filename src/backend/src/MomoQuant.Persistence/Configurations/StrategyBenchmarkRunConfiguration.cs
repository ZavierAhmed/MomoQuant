using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Benchmarks;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyBenchmarkRunConfiguration : IEntityTypeConfiguration<StrategyBenchmarkRun>
{
    public void Configure(EntityTypeBuilder<StrategyBenchmarkRun> builder)
    {
        builder.ToTable("StrategyBenchmarkRuns");

        builder.Property(run => run.Name).HasMaxLength(200).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.SymbolsJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.TimeframesJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.StrategyIdsJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.InitialBalance).HasTradingDecimal();
        builder.Property(run => run.ExecutionMode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.MakerFeeRate).HasTradingDecimal();
        builder.Property(run => run.TakerFeeRate).HasTradingDecimal();
        builder.Property(run => run.MinConfidenceScore).HasTradingDecimal();
        builder.Property(run => run.ConfigJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.CurrentStage).HasMaxLength(64);
        builder.Property(run => run.PercentComplete).HasTradingDecimal();
        builder.Property(run => run.CurrentSymbol).HasMaxLength(32);
        builder.Property(run => run.CurrentTimeframe).HasMaxLength(16);
        builder.Property(run => run.CurrentStrategy).HasMaxLength(96);
        builder.Property(run => run.Message).HasMaxLength(1000);
        builder.Property(run => run.ErrorMessage).HasMaxLength(4000);
        builder.Property(run => run.DataPreparationPercent).HasTradingDecimal();
        builder.Property(run => run.BacktestPercent).HasTradingDecimal();
        builder.Property(run => run.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();
        builder.Property(run => run.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(run => run.CreatedAtUtc);
        builder.HasIndex(run => run.Status);
    }
}
