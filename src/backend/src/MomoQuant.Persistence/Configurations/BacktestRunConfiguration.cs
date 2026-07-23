using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Backtesting;

namespace MomoQuant.Persistence.Configurations;

internal sealed class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    public void Configure(EntityTypeBuilder<BacktestRun> builder)
    {
        builder.ToTable("BacktestRuns");

        builder.Property(run => run.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(run => run.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(run => run.HigherTimeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(run => run.InitialBalance).HasTradingDecimal();
        builder.Property(run => run.FinalBalance).HasTradingDecimal();

        builder.Property(run => run.ExecutionMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(run => run.ErrorMessage)
            .HasMaxLength(4000);

        builder.Property(run => run.ConfigJson)
            .HasColumnType("longtext")
            .IsRequired();

        builder.Property(run => run.StrategySetJson)
            .HasColumnType("longtext")
            .IsRequired();

        builder.Property(run => run.SettingsJson)
            .HasColumnType("longtext")
            .IsRequired();

        builder.Property(run => run.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(run => run.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(run => run.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(run => run.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(run => run.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
