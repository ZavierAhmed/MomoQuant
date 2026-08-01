using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyParameterSetConfiguration : IEntityTypeConfiguration<StrategyParameterSet>
{
    public void Configure(EntityTypeBuilder<StrategyParameterSet> builder)
    {
        builder.ToTable("StrategyParameterSets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StrategyCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(x => x.MarketRegime).HasMaxLength(64);
        builder.Property(x => x.ParametersJson).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.TrainingRangeJson).HasColumnType("longtext");
        builder.Property(x => x.ValidationRangeJson).HasColumnType("longtext");
        builder.Property(x => x.TrainingMetricsJson).HasColumnType("longtext");
        builder.Property(x => x.ValidationMetricsJson).HasColumnType("longtext");
        builder.Property(x => x.RobustnessScore).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.QualificationStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.QualificationParameterFingerprint).HasMaxLength(64);
        builder.Property(x => x.QualificationEvidenceVersion).HasMaxLength(64);
        builder.HasIndex(x => x.QualificationSourceExperimentId).IsUnique();
        builder.HasIndex(x => x.QualificationSourceTrialId).IsUnique();
        builder.HasOne<ValidationExperiment>()
            .WithMany()
            .HasForeignKey(x => x.QualificationSourceExperimentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ValidationParameterTrial>()
            .WithMany()
            .HasForeignKey(x => x.QualificationSourceTrialId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_StrategyParameterSets_DeploymentQualificationProvenance",
            "`QualificationStatus` <> 'DeploymentQualified' OR ("
            + "`Source` = 'ValidationLab' AND `IsApproved` = 1 AND "
            + "`QualificationSourceExperimentId` IS NOT NULL AND "
            + "`QualificationSourceTrialId` IS NOT NULL AND "
            + "`QualificationParameterFingerprint` IS NOT NULL AND "
            + "CHAR_LENGTH(`QualificationParameterFingerprint`) > 0 AND "
            + "`QualificationEvidenceVersion` IS NOT NULL AND "
            + "CHAR_LENGTH(`QualificationEvidenceVersion`) > 0 AND "
            + "`QualifiedAtUtc` IS NOT NULL)"));
        builder.HasIndex(x => new { x.StrategyCode, x.Timeframe });
        builder.HasIndex(x => new { x.StrategyCode, x.SymbolId, x.Timeframe });
    }
}
