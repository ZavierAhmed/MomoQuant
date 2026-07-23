using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Configurations;

internal sealed class AiDecisionConfiguration : IEntityTypeConfiguration<AiDecision>
{
    public void Configure(EntityTypeBuilder<AiDecision> builder)
    {
        builder.ToTable("AiDecisions");

        builder.Property(decision => decision.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(decision => decision.MarketRegime)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(decision => decision.ConfidenceScore).HasTradingDecimal();
        builder.Property(decision => decision.RegimeConfidence).HasTradingDecimal();
        builder.Property(decision => decision.RiskAdjustment).HasTradingDecimal();

        builder.Property(decision => decision.ConfidenceClassification)
            .HasMaxLength(32);

        builder.Property(decision => decision.AnomalySeverity)
            .HasMaxLength(32);

        builder.Property(decision => decision.Summary)
            .HasMaxLength(2000);

        builder.Property(decision => decision.ReasonsJson)
            .HasColumnType("longtext");

        builder.Property(decision => decision.WarningsJson)
            .HasColumnType("longtext");

        builder.Property(decision => decision.PreferredStrategyCode)
            .HasConversion(
                code => code.HasValue ? code.Value.ToCode() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : StrategyCodeExtensions.FromCode(value))
            .HasMaxLength(64);

        builder.Property(decision => decision.Explanation)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(decision => decision.RawRequestJson)
            .HasColumnType("longtext");

        builder.Property(decision => decision.RawResponseJson)
            .HasColumnType("longtext");

        builder.Property(decision => decision.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(decision => decision.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(decision => decision.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.MarketData.Candle>()
            .WithMany()
            .HasForeignKey(decision => decision.CandleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Signals.StrategySignal>()
            .WithMany()
            .HasForeignKey(decision => decision.SignalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
