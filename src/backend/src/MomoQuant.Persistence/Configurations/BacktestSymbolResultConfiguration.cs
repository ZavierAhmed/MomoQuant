using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Configurations;

internal sealed class BacktestSymbolResultConfiguration : IEntityTypeConfiguration<BacktestSymbolResult>
{
    public void Configure(EntityTypeBuilder<BacktestSymbolResult> builder)
    {
        builder.ToTable("BacktestSymbolResults");

        builder.Property(result => result.Symbol)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(result => result.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(result => result.NetPnl).HasTradingDecimal();
        builder.Property(result => result.WinRatePercent).HasTradingDecimal();
        builder.Property(result => result.ProfitFactor).HasTradingDecimal();
        builder.Property(result => result.MaxDrawdownPercent).HasTradingDecimal();
        builder.Property(result => result.TotalFees).HasTradingDecimal();
        builder.Property(result => result.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(result => new { result.BacktestRunId, result.SymbolId, result.Timeframe }).IsUnique();

        builder.HasOne<BacktestRun>()
            .WithMany()
            .HasForeignKey(result => result.BacktestRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(result => result.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
