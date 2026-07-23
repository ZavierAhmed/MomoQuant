using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Settings;

namespace MomoQuant.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");

        builder.Property(setting => setting.SettingKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(setting => setting.SettingValue)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(setting => setting.ValueType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(setting => setting.Category)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(setting => setting.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(setting => setting.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(setting => setting.SettingKey).IsUnique();
    }
}
