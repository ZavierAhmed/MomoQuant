using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Backtesting;

namespace MomoQuant.Persistence.Configurations;

internal sealed class BacktestEquityPointConfiguration : IEntityTypeConfiguration<BacktestEquityPoint>
{
    public void Configure(EntityTypeBuilder<BacktestEquityPoint> builder)
    {
        builder.ToTable("BacktestEquityPoints");

        builder.Property(point => point.Balance).HasTradingDecimal();
        builder.Property(point => point.Equity).HasTradingDecimal();
        builder.Property(point => point.Drawdown).HasTradingDecimal();
        builder.Property(point => point.DrawdownPercent).HasTradingDecimal();
        builder.Property(point => point.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(point => new { point.BacktestRunId, point.TimestampUtc });

        builder.HasOne<BacktestRun>()
            .WithMany()
            .HasForeignKey(point => point.BacktestRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
