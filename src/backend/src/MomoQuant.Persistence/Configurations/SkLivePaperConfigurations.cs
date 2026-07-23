using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Persistence.Configurations;

internal sealed class SkLivePaperSessionConfiguration : IEntityTypeConfiguration<SkLivePaperSession>
{
    public void Configure(EntityTypeBuilder<SkLivePaperSession> builder)
    {
        builder.ToTable("SkLivePaperSessions");
        builder.Property(s => s.SessionName).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(s => s.HigherTimeframe).HasMaxLength(16).IsRequired();
        builder.Property(s => s.PrimaryTimeframe).HasMaxLength(16).IsRequired();
        builder.Property(s => s.AdditionalTimeframesJson).HasColumnType("longtext").IsRequired();
        builder.Property(s => s.ConfirmationMode).HasMaxLength(64).IsRequired();
        builder.Property(s => s.SimulationMode).HasMaxLength(32).IsRequired();
        builder.Property(s => s.LastError).HasColumnType("longtext");
        builder.Property(s => s.StartingBalance).HasTradingDecimal();
        builder.Property(s => s.CurrentBalance).HasTradingDecimal();
        builder.Property(s => s.RiskPerPaperTradePercent).HasTradingDecimal();
        builder.Property(s => s.MinClarityScore).HasTradingDecimal();
        builder.Property(s => s.MinUsefulnessScore).HasTradingDecimal();
        builder.Property(s => s.SimulatedLeverage).HasTradingDecimal();
        builder.Property(s => s.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.SymbolId);
        builder.HasIndex(s => s.CreatedAtUtc);
    }
}

internal sealed class SkLivePaperCandidateConfiguration : IEntityTypeConfiguration<SkLivePaperCandidate>
{
    public void Configure(EntityTypeBuilder<SkLivePaperCandidate> builder)
    {
        builder.ToTable("SkLivePaperCandidates");
        builder.Property(c => c.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(c => c.HigherTimeframe).HasMaxLength(16).IsRequired();
        builder.Property(c => c.PrimaryTimeframe).HasMaxLength(16).IsRequired();
        builder.Property(c => c.Direction).HasMaxLength(16).IsRequired();
        builder.Property(c => c.SequenceStatus).HasMaxLength(64).IsRequired();
        builder.Property(c => c.ValidityStatus).HasMaxLength(64).IsRequired();
        builder.Property(c => c.UsefulnessStatus).HasMaxLength(64).IsRequired();
        builder.Property(c => c.RejectionReason).HasMaxLength(256);
        builder.Property(c => c.CandidateKey).HasMaxLength(128).IsRequired();
        builder.Property(c => c.ReactionZoneLow).HasTradingDecimal();
        builder.Property(c => c.ReactionZoneHigh).HasTradingDecimal();
        builder.Property(c => c.StrongReactionZoneLow).HasTradingDecimal();
        builder.Property(c => c.StrongReactionZoneHigh).HasTradingDecimal();
        builder.Property(c => c.InvalidationLevel).HasTradingDecimal();
        builder.Property(c => c.Target1).HasTradingDecimal();
        builder.Property(c => c.Target2).HasTradingDecimal();
        builder.Property(c => c.CurrentPrice).HasTradingDecimal();
        builder.Property(c => c.ClarityScore).HasTradingDecimal();
        builder.Property(c => c.UsefulnessScore).HasTradingDecimal();
        builder.Property(c => c.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();
        builder.HasIndex(c => c.SessionId);
        builder.HasIndex(c => new { c.SessionId, c.CandidateKey });
    }
}

internal sealed class SkLivePaperTradeConfiguration : IEntityTypeConfiguration<SkLivePaperTrade>
{
    public void Configure(EntityTypeBuilder<SkLivePaperTrade> builder)
    {
        builder.ToTable("SkLivePaperTrades");
        builder.Property(t => t.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Direction).HasMaxLength(16).IsRequired();
        builder.Property(t => t.SimulationMode).HasMaxLength(32).IsRequired();
        builder.Property(t => t.HtfDirection).HasMaxLength(32).IsRequired();
        builder.Property(t => t.LtfDirection).HasMaxLength(32).IsRequired();
        builder.Property(t => t.EntryPrice).HasTradingDecimal();
        builder.Property(t => t.Quantity).HasTradingDecimal();
        builder.Property(t => t.SimulatedLeverage).HasTradingDecimal();
        builder.Property(t => t.MarginUsed).HasTradingDecimal();
        builder.Property(t => t.NotionalValue).HasTradingDecimal();
        builder.Property(t => t.StopLoss).HasTradingDecimal();
        builder.Property(t => t.TakeProfit1).HasTradingDecimal();
        builder.Property(t => t.TakeProfit2).HasTradingDecimal();
        builder.Property(t => t.ExitPrice).HasTradingDecimal();
        builder.Property(t => t.GrossPnl).HasTradingDecimal();
        builder.Property(t => t.Fees).HasTradingDecimal();
        builder.Property(t => t.Slippage).HasTradingDecimal();
        builder.Property(t => t.NetPnl).HasTradingDecimal();
        builder.Property(t => t.NetPnlPercent).HasTradingDecimal();
        builder.Property(t => t.ClarityScore).HasTradingDecimal();
        builder.Property(t => t.UsefulnessScore).HasTradingDecimal();
        builder.Property(t => t.CreatedAtUtc).HasColumnName("CreatedAt").IsRequired();
        builder.HasIndex(t => t.SessionId);
        builder.HasIndex(t => new { t.SessionId, t.Status });
    }
}

internal sealed class SkLivePaperEventConfiguration : IEntityTypeConfiguration<SkLivePaperEvent>
{
    public void Configure(EntityTypeBuilder<SkLivePaperEvent> builder)
    {
        builder.ToTable("SkLivePaperEvents");
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Message).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.DetailsJson).HasColumnType("longtext");
        builder.HasIndex(e => e.SessionId);
        builder.HasIndex(e => e.CreatedAtUtc);
    }
}
