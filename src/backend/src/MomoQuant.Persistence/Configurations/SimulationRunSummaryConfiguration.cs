using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Simulation;

namespace MomoQuant.Persistence.Configurations;

internal sealed class SimulationRunSummaryConfiguration : IEntityTypeConfiguration<SimulationRunSummary>
{
    public void Configure(EntityTypeBuilder<SimulationRunSummary> builder)
    {
        builder.ToTable("SimulationRunSummaries");

        builder.Property(summary => summary.SourceType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(summary => summary.Name).HasMaxLength(300).IsRequired();
        builder.Property(summary => summary.Status).HasMaxLength(32).IsRequired();

        builder.Property(summary => summary.SymbolsJson).HasColumnType("longtext").IsRequired();
        builder.Property(summary => summary.StrategiesJson).HasColumnType("longtext").IsRequired();
        builder.Property(summary => summary.TimeframesJson).HasColumnType("longtext").IsRequired();
        builder.Property(summary => summary.EvaluationMode).HasMaxLength(64);

        builder.Property(summary => summary.InitialBalance).HasTradingDecimal();
        builder.Property(summary => summary.FinalBalance).HasTradingDecimal();
        builder.Property(summary => summary.NetPnl).HasTradingDecimal();
        builder.Property(summary => summary.NetPnlPercent).HasTradingDecimal();
        builder.Property(summary => summary.MaxDrawdown).HasTradingDecimal();
        builder.Property(summary => summary.WinRatePercent).HasTradingDecimal();
        builder.Property(summary => summary.ShadowNetPnl).HasTradingDecimal();

        builder.Property(summary => summary.SummaryText).HasColumnType("longtext").IsRequired();
        builder.Property(summary => summary.KeyFindingsJson).HasColumnType("longtext").IsRequired();
        builder.Property(summary => summary.WarningsJson).HasColumnType("longtext").IsRequired();

        builder.Property(summary => summary.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();
        builder.Property(summary => summary.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(summary => new { summary.SourceType, summary.SourceId }).IsUnique();
        builder.HasIndex(summary => summary.CreatedAtUtc);
    }
}
