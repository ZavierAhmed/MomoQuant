using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Persistence.Configurations;

internal sealed class PaperAccountSnapshotConfiguration : IEntityTypeConfiguration<PaperAccountSnapshot>
{
    public void Configure(EntityTypeBuilder<PaperAccountSnapshot> builder)
    {
        builder.ToTable("PaperAccountSnapshots");

        builder.Property(snapshot => snapshot.Balance).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Equity).HasTradingDecimal();
        builder.Property(snapshot => snapshot.UnrealizedPnl).HasTradingDecimal();
        builder.Property(snapshot => snapshot.RealizedPnl).HasTradingDecimal();
        builder.Property(snapshot => snapshot.TotalFees).HasTradingDecimal();
        builder.Property(snapshot => snapshot.Drawdown).HasTradingDecimal();
        builder.Property(snapshot => snapshot.DrawdownPercent).HasTradingDecimal();
        builder.Property(snapshot => snapshot.MarginUsed).HasTradingDecimal();

        builder.Property(snapshot => snapshot.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<PaperAccount>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.PaperAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PaperTradingSession>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.PaperSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
