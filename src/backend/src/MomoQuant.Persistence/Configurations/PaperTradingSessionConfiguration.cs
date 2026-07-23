using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Persistence.Configurations;

internal sealed class PaperTradingSessionConfiguration : IEntityTypeConfiguration<PaperTradingSession>
{
    public void Configure(EntityTypeBuilder<PaperTradingSession> builder)
    {
        builder.ToTable("PaperTradingSessions");

        builder.Property(session => session.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(session => session.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(session => session.Mode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(session => session.ExecutionMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(session => session.MinConfidenceScore).HasTradingDecimal();
        builder.Property(session => session.ErrorMessage).HasMaxLength(4000);
        builder.Property(session => session.ConfigJson).HasColumnType("longtext");

        builder.Property(session => session.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(session => session.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasOne<PaperAccount>()
            .WithMany()
            .HasForeignKey(session => session.PaperAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(session => session.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
