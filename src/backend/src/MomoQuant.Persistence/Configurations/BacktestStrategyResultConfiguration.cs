using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Configurations;

internal sealed class BacktestStrategyResultConfiguration : IEntityTypeConfiguration<BacktestStrategyResult>
{
    public void Configure(EntityTypeBuilder<BacktestStrategyResult> builder)
    {
        builder.ToTable("BacktestStrategyResults");

        builder.Property(result => result.StrategyCode)
            .HasConversion(
                code => code.ToCode(),
                value => StrategyCodeExtensions.FromCode(value))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(result => result.NetPnl).HasTradingDecimal();
        builder.Property(result => result.WinRatePercent).HasTradingDecimal();
        builder.Property(result => result.ProfitFactor).HasTradingDecimal();
        builder.Property(result => result.MaxDrawdownPercent).HasTradingDecimal();
        builder.Property(result => result.AverageConfidenceScore).HasTradingDecimal();
        builder.Property(result => result.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(result => new { result.BacktestRunId, result.StrategyCode }).IsUnique();

        builder.HasOne<BacktestRun>()
            .WithMany()
            .HasForeignKey(result => result.BacktestRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
