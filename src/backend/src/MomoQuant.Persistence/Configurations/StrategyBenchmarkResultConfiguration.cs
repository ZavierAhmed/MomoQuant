using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Benchmarks;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyBenchmarkResultConfiguration : IEntityTypeConfiguration<StrategyBenchmarkResult>
{
    public void Configure(EntityTypeBuilder<StrategyBenchmarkResult> builder)
    {
        builder.ToTable("StrategyBenchmarkResults");

        builder.Property(result => result.StrategyCode).HasMaxLength(64).IsRequired();
        builder.Property(result => result.StrategyName).HasMaxLength(200).IsRequired();
        builder.Property(result => result.Symbol).HasMaxLength(32);
        builder.Property(result => result.Timeframe).HasMaxLength(16);
        builder.Property(result => result.InitialBalance).HasTradingDecimal();
        builder.Property(result => result.FinalBalance).HasTradingDecimal();
        builder.Property(result => result.NetPnl).HasTradingDecimal();
        builder.Property(result => result.NetPnlPercent).HasTradingDecimal();
        builder.Property(result => result.GrossProfit).HasTradingDecimal();
        builder.Property(result => result.GrossLoss).HasTradingDecimal();
        builder.Property(result => result.ProfitFactor).HasTradingDecimal();
        builder.Property(result => result.MaxDrawdown).HasTradingDecimal();
        builder.Property(result => result.MaxDrawdownPercent).HasTradingDecimal();
        builder.Property(result => result.WinRatePercent).HasTradingDecimal();
        builder.Property(result => result.AverageWin).HasTradingDecimal();
        builder.Property(result => result.AverageLoss).HasTradingDecimal();
        builder.Property(result => result.LargestWin).HasTradingDecimal();
        builder.Property(result => result.LargestLoss).HasTradingDecimal();
        builder.Property(result => result.AverageRewardRisk).HasTradingDecimal();
        builder.Property(result => result.TotalFees).HasTradingDecimal();
        builder.Property(result => result.AverageConfidenceScore).HasTradingDecimal();
        builder.Property(result => result.Grade).HasMaxLength(8).IsRequired();
        builder.Property(result => result.Score).HasTradingDecimal();
        builder.Property(result => result.StrengthsJson).HasColumnType("longtext").IsRequired();
        builder.Property(result => result.WeaknessesJson).HasColumnType("longtext").IsRequired();
        builder.Property(result => result.WarningsJson).HasColumnType("longtext").IsRequired();
        builder.Property(result => result.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();

        builder.HasIndex(result => result.BenchmarkRunId);
        builder.HasIndex(result => new { result.BenchmarkRunId, result.StrategyCode });

        builder.HasOne<StrategyBenchmarkRun>()
            .WithMany()
            .HasForeignKey(result => result.BenchmarkRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
