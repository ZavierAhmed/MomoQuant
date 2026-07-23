using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Replay;

namespace MomoQuant.Persistence.Configurations;

internal sealed class ReplayFrameConfiguration : IEntityTypeConfiguration<ReplayFrame>
{
    public void Configure(EntityTypeBuilder<ReplayFrame> builder)
    {
        builder.ToTable("ReplayFrames");

        builder.Property(frame => frame.MarketRegime).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(frame => frame.StrategyResultsJson).IsRequired();
        builder.Property(frame => frame.Explanation).HasMaxLength(8000).IsRequired();
        builder.Property(frame => frame.Balance).HasTradingDecimal();
        builder.Property(frame => frame.Equity).HasTradingDecimal();
        builder.Property(frame => frame.Drawdown).HasTradingDecimal();
        builder.Property(frame => frame.DrawdownPercent).HasTradingDecimal();
        builder.Property(frame => frame.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(frame => new { frame.ReplaySessionId, frame.FrameIndex }).IsUnique();

        builder.HasOne<ReplaySession>()
            .WithMany()
            .HasForeignKey(frame => frame.ReplaySessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
