using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Persistence.Configurations;

internal sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("Trades");

        builder.Property(trade => trade.Direction)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(trade => trade.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(trade => trade.CloseReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(trade => trade.EntryPrice).HasTradingDecimal();
        builder.Property(trade => trade.ExitPrice).HasTradingDecimal();
        builder.Property(trade => trade.Quantity).HasTradingDecimal();
        builder.Property(trade => trade.StopLoss).HasTradingDecimal();
        builder.Property(trade => trade.TakeProfit).HasTradingDecimal();
        builder.Property(trade => trade.GrossPnl).HasTradingDecimal();
        builder.Property(trade => trade.Fees).HasTradingDecimal();
        builder.Property(trade => trade.FundingFees).HasTradingDecimal();
        builder.Property(trade => trade.NetPnl).HasTradingDecimal();
        builder.Property(trade => trade.RMultiple).HasTradingDecimal();

        builder.Property(trade => trade.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(trade => trade.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(trade => trade.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(trade => trade.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Strategies.Strategy>()
            .WithMany()
            .HasForeignKey(trade => trade.StrategyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Signals.StrategySignal>()
            .WithMany()
            .HasForeignKey(trade => trade.SignalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Ai.AiDecision>()
            .WithMany()
            .HasForeignKey(trade => trade.AiDecisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Risk.RiskDecision>()
            .WithMany()
            .HasForeignKey(trade => trade.RiskDecisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Execution.Order>()
            .WithMany()
            .HasForeignKey(trade => trade.EntryOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Execution.Order>()
            .WithMany()
            .HasForeignKey(trade => trade.ExitOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
