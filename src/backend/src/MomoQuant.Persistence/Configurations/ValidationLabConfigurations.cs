using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Persistence.Configurations;

internal sealed class ValidationExperimentConfiguration : IEntityTypeConfiguration<ValidationExperiment>
{
    public void Configure(EntityTypeBuilder<ValidationExperiment> builder)
    {
        builder.ToTable("ValidationExperiments");
        builder.Property(e => e.Name).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.ExperimentType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.StrategyCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.StrategyVersion).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Exchange).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(e => e.SplitAlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.WarmupAlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.CandleDataFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(e => e.FrozenParameterFingerprint).HasMaxLength(64);
        builder.Property(e => e.FrozenStrategyFingerprint).HasMaxLength(128);
        builder.Property(e => e.ValidationRevealStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.PrimaryQualificationLayer).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.StrategyRobustnessDecision).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.PrimaryFailureReason).HasMaxLength(128);
        builder.Property(e => e.ValidationRevealedBy).HasMaxLength(128);
        builder.Property(e => e.CurrentStage).HasMaxLength(200);
        builder.Property(e => e.SplitRatio).HasTradingDecimal();
        builder.Property(e => e.InitialBalance).HasTradingDecimal();
        builder.Property(e => e.PercentComplete).HasTradingDecimal();
        builder.Property(e => e.CandleDataSnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.WarmupSnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.ParameterSearchSpaceSnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.OptimizationObjectiveSnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.FrozenStrategyParameterSnapshotJson).HasColumnType("longtext");
        builder.Property(e => e.FrozenConfidenceSnapshotJson).HasColumnType("longtext");
        builder.Property(e => e.FrozenRiskSnapshotJson).HasColumnType("longtext");
        builder.Property(e => e.FrozenCostModelSnapshotJson).HasColumnType("longtext");
        builder.Property(e => e.QualificationProfileSnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.DraftConfigurationJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.FailureReasonsJson).HasColumnType("longtext");
        builder.Property(e => e.QualificationRuleResultsJson).HasColumnType("longtext");
        builder.Property(e => e.DecisionExplanation).HasColumnType("longtext");
        builder.Property(e => e.DiagnosticsJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.OverlayResultsJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.ComparisonJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.RegimeComparisonJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.ParameterStabilityJson).HasColumnType("longtext").IsRequired();
        builder.Property(e => e.ErrorMessage).HasColumnType("longtext");
        builder.Property(e => e.ValidationMetricsVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.CandidateReconciliationJson).HasColumnType("longtext");
        builder.Property(e => e.CandidateReconciliationStatus).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.LeakageAuditJson).HasColumnType("longtext");
        builder.Property(e => e.LeakageAuditStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ParameterStabilityApplicability).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.SegmentDetectorContinuityMode).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(e => e.ExpectancyMetric).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ProfitFactorMetric).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.HoldoutExclusivityPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.HoldoutExclusivityJson).HasColumnType("longtext");
        builder.Property(e => e.MetricConsistencyJson).HasColumnType("longtext");
        builder.Property(e => e.MetricConsistencyStatus).HasMaxLength(32);
        builder.Property(e => e.ExportVerificationJson).HasColumnType("longtext");
        builder.Property(e => e.ExportVerificationStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ValidationLaboratoryReadinessStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.SupersessionStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.SupersessionReason).HasMaxLength(512);
        builder.Property(e => e.SelectionIntegrityStatus).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(e => e.SelectedTrialParameterFingerprint).HasMaxLength(64);
        builder.Property(e => e.SelectedTrialParameterSnapshotJson).HasColumnType("longtext");
        builder.Property(e => e.FrozenSnapshotValidationStatus).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(e => e.SelectionIntegrityVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.RiskBasisVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ParameterFingerprintVersion).HasMaxLength(64).IsRequired();
        builder.Property(e => e.FreezeSource).HasMaxLength(128);
        builder.Property(e => e.FallbackSelectionPolicyVersion).HasMaxLength(128);
        builder.Property(e => e.FallbackSelectionReason).HasMaxLength(512);
        builder.Property(e => e.CloseoutAuditJson).HasColumnType("longtext");
        builder.Property(e => e.TrialPopulationSummaryJson).HasColumnType("longtext");
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.StrategyCode);
    }
}

internal sealed class ValidationParameterTrialConfiguration : IEntityTypeConfiguration<ValidationParameterTrial>
{
    public void Configure(EntityTypeBuilder<ValidationParameterTrial> builder)
    {
        builder.ToTable("ValidationParameterTrials");
        builder.Property(t => t.ParameterSnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(t => t.ParameterFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.GuardrailDecision).HasMaxLength(32).IsRequired();
        builder.Property(t => t.GuardrailFailureReasonsJson).HasColumnType("longtext");
        builder.Property(t => t.DiagnosticWarningsJson).HasColumnType("longtext");
        builder.Property(t => t.NetExpectancyR).HasTradingDecimal();
        builder.Property(t => t.GrossPnl).HasTradingDecimal();
        builder.Property(t => t.NetPnl).HasTradingDecimal();
        builder.Property(t => t.ProfitFactor).HasTradingDecimal();
        builder.Property(t => t.MaximumDrawdownPercent).HasTradingDecimal();
        builder.Property(t => t.FeeImpactPercent).HasTradingDecimal();
        builder.Property(t => t.TrainingScore).HasTradingDecimal();
        builder.HasIndex(t => t.ValidationExperimentId);
        builder.HasIndex(t => new { t.ValidationExperimentId, t.TrialNumber }).IsUnique();
        builder.HasIndex(t => new { t.ValidationExperimentId, t.ParameterFingerprint })
            .IsUnique()
            .HasDatabaseName("IX_ValTrials_ExpId_Fingerprint");
        builder.Property(t => t.ErrorMessage).HasColumnType("longtext");
        builder.Property(t => t.RecoverySource).HasConversion<string>().HasMaxLength(64).IsRequired();
    }
}

internal sealed class ValidationExperimentExecutionLeaseConfiguration : IEntityTypeConfiguration<ValidationExperimentExecutionLease>
{
    public void Configure(EntityTypeBuilder<ValidationExperimentExecutionLease> builder)
    {
        builder.ToTable("ValidationExperimentExecutionLeases");
        builder.Property(l => l.LeaseOwner).HasMaxLength(128).IsRequired();
        builder.HasIndex(l => l.ValidationExperimentId)
            .IsUnique()
            .HasDatabaseName("IX_ValExpLeases_ExperimentId");
    }
}

internal sealed class ValidationSegmentResultConfiguration : IEntityTypeConfiguration<ValidationSegmentResult>
{
    public void Configure(EntityTypeBuilder<ValidationSegmentResult> builder)
    {
        builder.ToTable("ValidationSegmentResults");
        builder.Property(r => r.SegmentType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.LayerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.MetricsJson).HasColumnType("longtext").IsRequired();
        builder.Property(r => r.ResultFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(r => r.NetExpectancyR).HasTradingDecimal();
        builder.Property(r => r.ProfitFactor).HasTradingDecimal();
        builder.Property(r => r.NetPnl).HasTradingDecimal();
        builder.Property(r => r.NetReturnPercent).HasTradingDecimal();
        builder.Property(r => r.MaximumDrawdownPercent).HasTradingDecimal();
        builder.Property(r => r.TransactionCosts).HasTradingDecimal();
        builder.Property(r => r.ResultCalculationVersion).HasMaxLength(64).IsRequired();
        builder.Property(r => r.GrossExpectancyR).HasTradingDecimal();
        builder.Property(r => r.GrossProfitFactor).HasTradingDecimal();
        builder.Property(r => r.NetProfitFactor).HasTradingDecimal();
        builder.Property(r => r.GrossAverageR).HasTradingDecimal();
        builder.Property(r => r.NetAverageR).HasTradingDecimal();
        builder.Property(r => r.GrossPnl).HasTradingDecimal();
        builder.Property(r => r.GrossProfit).HasTradingDecimal();
        builder.Property(r => r.GrossLoss).HasTradingDecimal();
        builder.Property(r => r.NetProfit).HasTradingDecimal();
        builder.Property(r => r.NetLoss).HasTradingDecimal();
        builder.HasIndex(r => r.ValidationExperimentId);
        builder.HasIndex(r => new { r.ValidationExperimentId, r.SegmentType, r.LayerType }).IsUnique();
    }
}

internal sealed class ValidationCandleAccessAuditConfiguration : IEntityTypeConfiguration<ValidationCandleAccessAudit>
{
    public void Configure(EntityTypeBuilder<ValidationCandleAccessAudit> builder)
    {
        builder.ToTable("ValidationCandleAccessAudits");
        builder.Property(a => a.AccessEventId)
            .HasColumnType("char(36)")
            .IsRequired();
        builder.Property(a => a.ScopeExecutionId)
            .HasColumnType("char(36)")
            .IsRequired();
        builder.Property(a => a.CallerComponent).HasMaxLength(128).IsRequired();
        builder.Property(a => a.CandleContentFingerprint).HasMaxLength(64);
        builder.Property(a => a.DenialReason).HasMaxLength(512);
        builder.Property(a => a.RecorderVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(a => a.AccessEventId)
            .IsUnique()
            .HasDatabaseName("IX_ValCandleAccess_AccessEventId");
        builder.HasIndex(a => a.ValidationExperimentId)
            .HasDatabaseName("IX_ValCandleAccess_ExperimentId");
        builder.HasIndex(a => new { a.ValidationExperimentId, a.AccessedAtUtc })
            .HasDatabaseName("IX_ValCandleAccess_Experiment_Accessed");
        builder.HasIndex(a => new { a.ValidationExperimentId, a.TrialNumber, a.ScopeExecutionId, a.AccessedAtUtc })
            .HasDatabaseName("IX_ValCandleAccess_Exp_Trial_Scope_Accessed");
    }
}

