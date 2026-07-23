using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Backtesting;

namespace MomoQuant.Persistence.Configurations;

internal sealed class BacktestResultConfiguration : IEntityTypeConfiguration<BacktestResult>
{
    public void Configure(EntityTypeBuilder<BacktestResult> builder)
    {
        builder.ToTable("BacktestResults");

        builder.Property(result => result.InitialBalance).HasTradingDecimal();
        builder.Property(result => result.FinalBalance).HasTradingDecimal();
        builder.Property(result => result.NetPnl).HasTradingDecimal();
        builder.Property(result => result.NetPnlPercent).HasTradingDecimal();
        builder.Property(result => result.GrossProfit).HasTradingDecimal();
        builder.Property(result => result.GrossLoss).HasTradingDecimal();
        builder.Property(result => result.WinRate).HasTradingDecimal();
        builder.Property(result => result.WinRatePercent).HasTradingDecimal();
        builder.Property(result => result.GrossPnl).HasTradingDecimal();
        builder.Property(result => result.TotalFees).HasTradingDecimal();
        builder.Property(result => result.TotalSlippage).HasTradingDecimal();
        builder.Property(result => result.MaxDrawdown).HasTradingDecimal();
        builder.Property(result => result.MaxDrawdownPercent).HasTradingDecimal();
        builder.Property(result => result.ProfitFactor).HasTradingDecimal();
        builder.Property(result => result.Expectancy).HasTradingDecimal();
        builder.Property(result => result.AverageWin).HasTradingDecimal();
        builder.Property(result => result.AverageLoss).HasTradingDecimal();
        builder.Property(result => result.LargestWin).HasTradingDecimal();
        builder.Property(result => result.LargestLoss).HasTradingDecimal();
        builder.Property(result => result.AverageRewardRisk).HasTradingDecimal();
        builder.Property(result => result.SharpeRatio).HasTradingDecimal();
        builder.Property(result => result.SortinoRatio).HasTradingDecimal();

        builder.Property(result => result.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(result => result.BacktestRunId).IsUnique();

        builder.HasOne<BacktestRun>()
            .WithMany()
            .HasForeignKey(result => result.BacktestRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
