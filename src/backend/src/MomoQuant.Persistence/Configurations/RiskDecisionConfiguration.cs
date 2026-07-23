using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Persistence.Configurations;

internal sealed class RiskDecisionConfiguration : IEntityTypeConfiguration<RiskDecision>
{
    public void Configure(EntityTypeBuilder<RiskDecision> builder)
    {
        builder.ToTable("RiskDecisions");

        builder.Property(decision => decision.Decision)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(decision => decision.Reason)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(decision => decision.ApprovedRiskPercent).HasTradingDecimal();
        builder.Property(decision => decision.PositionSize).HasTradingDecimal();
        builder.Property(decision => decision.StopLoss).HasTradingDecimal();
        builder.Property(decision => decision.TakeProfit).HasTradingDecimal();

        builder.Property(decision => decision.RejectedRuleKey)
            .HasMaxLength(128);

        builder.Property(decision => decision.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(decision => decision.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Signals.StrategySignal>()
            .WithMany()
            .HasForeignKey(decision => decision.SignalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Ai.AiDecision>()
            .WithMany()
            .HasForeignKey(decision => decision.AiDecisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(decision => decision.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
