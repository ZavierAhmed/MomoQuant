using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

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

        builder.Property(session => session.UseClass)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(Domain.Enums.PaperSessionUseClass.Research)
            .IsRequired();

        builder.Property(session => session.BoundTimeframe).HasMaxLength(16);
        builder.Property(session => session.QualificationParameterFingerprint).HasMaxLength(64);
        builder.Property(session => session.QualificationEvidenceVersion).HasMaxLength(64);

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

        builder.HasOne<StrategyParameterSet>()
            .WithMany()
            .HasForeignKey(session => session.ParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Strategy>()
            .WithMany()
            .HasForeignKey(session => session.BoundStrategyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(session => session.BoundSymbolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ValidationExperiment>()
            .WithMany()
            .HasForeignKey(session => session.QualificationSourceExperimentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ValidationParameterTrial>()
            .WithMany()
            .HasForeignKey(session => session.QualificationSourceTrialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(session => session.ParameterSetId);
        builder.HasIndex(session => session.QualificationSourceExperimentId);
        builder.HasIndex(session => session.QualificationSourceTrialId);
        builder.HasIndex(session => new { session.UseClass, session.Status });

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PaperTradingSessions_UseClassBinding",
            "(`UseClass` = 'Research' AND "
            + "`BoundStrategyId` IS NULL AND `BoundSymbolId` IS NULL AND `BoundTimeframe` IS NULL AND "
            + "`QualificationSourceExperimentId` IS NULL AND `QualificationSourceTrialId` IS NULL AND "
            + "`QualificationParameterFingerprint` IS NULL AND `QualificationEvidenceVersion` IS NULL AND "
            + "`QualificationVerifiedAtUtc` IS NULL) OR "
            + "(`UseClass` = 'DeploymentSimulation' AND `Mode` = 'LivePaper' AND "
            + "`ParameterSetId` IS NOT NULL AND `BoundStrategyId` IS NOT NULL AND `BoundSymbolId` IS NOT NULL AND "
            + "`BoundTimeframe` IS NOT NULL AND CHAR_LENGTH(`BoundTimeframe`) > 0 AND "
            + "`QualificationSourceExperimentId` IS NOT NULL AND `QualificationSourceTrialId` IS NOT NULL AND "
            + "`QualificationParameterFingerprint` IS NOT NULL AND CHAR_LENGTH(`QualificationParameterFingerprint`) > 0 AND "
            + "`QualificationEvidenceVersion` IS NOT NULL AND CHAR_LENGTH(`QualificationEvidenceVersion`) > 0 AND "
            + "`QualificationVerifiedAtUtc` IS NOT NULL)"));
    }
}
