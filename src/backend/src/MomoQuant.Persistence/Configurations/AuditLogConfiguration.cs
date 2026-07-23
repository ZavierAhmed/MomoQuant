using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(log => log.Action)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(log => log.EntityType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(log => log.UserEmail)
            .HasMaxLength(256);

        builder.Property(log => log.Severity)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(LogSeverity.Info)
            .IsRequired();

        builder.Property(log => log.MetadataJson)
            .HasColumnType("longtext");

        builder.Property(log => log.OldValueJson)
            .HasColumnType("longtext");

        builder.Property(log => log.NewValueJson)
            .HasColumnType("longtext");

        builder.Property(log => log.IpAddress)
            .HasMaxLength(64);

        builder.Property(log => log.UserAgent)
            .HasMaxLength(512);

        builder.Property(log => log.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(log => log.Action);
        builder.HasIndex(log => log.UserId);
        builder.HasIndex(log => log.CreatedAtUtc);
        builder.HasIndex(log => new { log.EntityType, log.EntityId });

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Sessions.TradingSession>()
            .WithMany()
            .HasForeignKey(log => log.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
