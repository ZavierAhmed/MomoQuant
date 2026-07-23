using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Persistence.Configurations;

internal sealed class RiskProfileConfiguration : IEntityTypeConfiguration<RiskProfile>
{
    public void Configure(EntityTypeBuilder<RiskProfile> builder)
    {
        builder.ToTable("RiskProfiles");

        builder.Property(profile => profile.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.Description)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(profile => profile.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(profile => profile.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(profile => profile.Name).IsUnique();
    }
}
