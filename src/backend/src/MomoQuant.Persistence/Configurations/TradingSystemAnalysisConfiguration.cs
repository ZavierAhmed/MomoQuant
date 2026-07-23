using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Persistence.Configurations;

internal sealed class TradingSystemAnalysisConfiguration : IEntityTypeConfiguration<TradingSystemAnalysis>
{
    public void Configure(EntityTypeBuilder<TradingSystemAnalysis> builder)
    {
        builder.ToTable("TradingSystemAnalyses");

        builder.Property(analysis => analysis.SystemCode).HasMaxLength(64).IsRequired();
        builder.Property(analysis => analysis.SystemName).HasMaxLength(128).IsRequired();
        builder.Property(analysis => analysis.ExchangeName).HasMaxLength(128).IsRequired();
        builder.Property(analysis => analysis.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(analysis => analysis.PrimaryTimeframe).HasMaxLength(16).IsRequired();
        builder.Property(analysis => analysis.HigherTimeframe).HasMaxLength(16).IsRequired();
        builder.Property(analysis => analysis.SwingSensitivity).HasMaxLength(32).IsRequired();
        builder.Property(analysis => analysis.DirectionMode).HasMaxLength(32).IsRequired();
        builder.Property(analysis => analysis.Status).HasMaxLength(32).IsRequired();
        builder.Property(analysis => analysis.MarketBias).HasMaxLength(32).IsRequired();
        builder.Property(analysis => analysis.ConfidenceLabel).HasMaxLength(32).IsRequired();

        builder.Property(analysis => analysis.CurrentPrice).HasTradingDecimal();

        builder.Property(analysis => analysis.SummaryText).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.BullishScenarioText).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.BearishScenarioText).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.InvalidationsText).HasColumnType("longtext").IsRequired();

        builder.Property(analysis => analysis.WarningsJson).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.ChartDataJson).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.SequenceCandidatesJson).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.FibonacciZonesJson).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.KeyLevelsJson).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.AiSummaryJson).HasColumnType("longtext").IsRequired();
        builder.Property(analysis => analysis.RawDiagnosticsJson).HasColumnType("longtext").IsRequired();

        builder.Property(analysis => analysis.AnalysisTimeUtc).IsRequired();
        builder.Property(analysis => analysis.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();

        builder.HasIndex(analysis => analysis.SystemCode);
        builder.HasIndex(analysis => analysis.CreatedAtUtc);
        builder.HasIndex(analysis => new { analysis.SymbolId, analysis.CreatedAtUtc });
    }
}
