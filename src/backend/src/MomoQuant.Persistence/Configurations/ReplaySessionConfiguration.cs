using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Replay;

namespace MomoQuant.Persistence.Configurations;

internal sealed class ReplaySessionConfiguration : IEntityTypeConfiguration<ReplaySession>
{
    public void Configure(EntityTypeBuilder<ReplaySession> builder)
    {
        builder.ToTable("ReplaySessions");

        builder.Property(session => session.Name).HasMaxLength(256).IsRequired();
        builder.Property(session => session.Timeframe).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(session => session.ExecutionMode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(session => session.Speed).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(session => session.ConfigJson).IsRequired();
        builder.Property(session => session.ErrorMessage).HasMaxLength(4000);
        builder.Property(session => session.InitialBalance).HasTradingDecimal();
        builder.Property(session => session.CurrentBalance).HasTradingDecimal();
        builder.Property(session => session.CurrentEquity).HasTradingDecimal();

        builder.Property(session => session.FromUtc).HasColumnName("StartTimeUtc");
        builder.Property(session => session.ToUtc).HasColumnName("EndTimeUtc");

        builder.Property(session => session.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(session => session.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(session => session.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(session => session.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
