using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Identity;

namespace MomoQuant.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Ignore(user => user.Role);

        builder.Property(user => user.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(user => user.UpdatedAtUtc).HasColumnName("UpdatedAt");
        builder.Property(user => user.LastLoginAtUtc).HasColumnName("LastLoginAt");

        builder.HasIndex(user => user.Email).IsUnique();

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
