using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Persistence.Configurations;

internal sealed class RiskRuleConfiguration : IEntityTypeConfiguration<RiskRule>
{
    public void Configure(EntityTypeBuilder<RiskRule> builder)
    {
        builder.ToTable("RiskRules");

        builder.Property(rule => rule.RuleKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(rule => rule.RuleValue)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(rule => rule.ValueType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(rule => rule.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(rule => rule.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(rule => new { rule.RiskProfileId, rule.RuleKey })
            .IsUnique();

        builder.HasOne<RiskProfile>()
            .WithMany()
            .HasForeignKey(rule => rule.RiskProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
