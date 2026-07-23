using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Identity;

namespace MomoQuant.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.Property(role => role.Name)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(role => role.Description)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(role => role.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(role => role.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(role => role.Name).IsUnique();
    }
}
