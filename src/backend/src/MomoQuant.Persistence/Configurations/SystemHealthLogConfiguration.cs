using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Monitoring;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Persistence.Configurations;

internal sealed class SystemHealthLogConfiguration : IEntityTypeConfiguration<SystemHealthLog>
{
    public void Configure(EntityTypeBuilder<SystemHealthLog> builder)
    {
        builder.ToTable("SystemHealthLogs");

        builder.Property(log => log.ServiceName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(log => log.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(log => log.Severity)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(LogSeverity.Info)
            .IsRequired();

        builder.Property(log => log.DetailsJson)
            .HasColumnType("longtext");

        builder.Property(log => log.Message)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(log => log.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasIndex(log => log.Severity);
        builder.HasIndex(log => log.CheckedAtUtc);
        builder.HasIndex(log => log.CreatedAtUtc);
        builder.HasIndex(log => new { log.ServiceName, log.CheckedAtUtc });
    }
}
