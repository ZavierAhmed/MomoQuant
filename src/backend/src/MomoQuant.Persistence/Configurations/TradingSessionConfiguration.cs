using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Sessions;

namespace MomoQuant.Persistence.Configurations;

internal sealed class TradingSessionConfiguration : IEntityTypeConfiguration<TradingSession>
{
    public void Configure(EntityTypeBuilder<TradingSession> builder)
    {
        builder.ToTable("TradingSessions");

        builder.Property(session => session.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(session => session.Mode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(session => session.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(session => session.InitialBalance).HasTradingDecimal();
        builder.Property(session => session.FinalBalance).HasTradingDecimal();

        builder.Property(session => session.Notes)
            .HasMaxLength(4000);

        builder.Property(session => session.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(session => session.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasOne<Domain.Exchanges.Exchange>()
            .WithMany()
            .HasForeignKey(session => session.ExchangeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(session => session.StartedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
