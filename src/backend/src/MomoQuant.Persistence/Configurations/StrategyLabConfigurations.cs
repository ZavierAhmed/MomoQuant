using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Persistence.Configurations;

internal sealed class StrategyLabRunConfiguration : IEntityTypeConfiguration<StrategyLabRun>
{
    public void Configure(EntityTypeBuilder<StrategyLabRun> builder)
    {
        builder.ToTable("StrategyLabRuns");
        builder.Property(run => run.Name).HasMaxLength(300).IsRequired();
        builder.Property(run => run.StrategyCode).HasMaxLength(100).IsRequired();
        builder.Property(run => run.StrategyVersion).HasMaxLength(32).IsRequired();
        builder.Property(run => run.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(run => run.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(run => run.ExecutionMode).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.ParametersJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.StrategyFeatureFlagsJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.FeeSettingsJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.SlippageSettingsJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.ResultSummaryJson).HasColumnType("longtext").IsRequired();
        builder.Property(run => run.ExperimentFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(run => run.AppVersion).HasMaxLength(64).IsRequired();
        builder.Property(run => run.GitCommit).HasMaxLength(64);
        builder.Property(run => run.CandleDatasetFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(run => run.StrategyCodeFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(run => run.InitialBalance).HasTradingDecimal();
        builder.Property(run => run.PercentComplete).HasTradingDecimal();
        builder.Property(run => run.CurrentStage).HasMaxLength(200);
        builder.Property(run => run.ErrorMessage).HasColumnType("longtext");
        builder.Property(run => run.RiskProfileSnapshotJson).HasColumnType("longtext");
        builder.HasIndex(run => run.StrategyCode);
        builder.HasIndex(run => run.CreatedAtUtc);
        builder.HasIndex(run => run.Status);
    }
}

internal sealed class StrategyResearchCandidateConfiguration : IEntityTypeConfiguration<StrategyResearchCandidate>
{
    public void Configure(EntityTypeBuilder<StrategyResearchCandidate> builder)
    {
        builder.ToTable("StrategyResearchCandidates");
        builder.Property(candidate => candidate.StrategyCode).HasMaxLength(100).IsRequired();
        builder.Property(candidate => candidate.StrategyVersion).HasMaxLength(32).IsRequired();
        builder.Property(candidate => candidate.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(candidate => candidate.Timeframe).HasMaxLength(16).IsRequired();
        builder.Property(candidate => candidate.Direction).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(candidate => candidate.CandidateStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(candidate => candidate.RawOutcomeStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(candidate => candidate.StrategyReason).HasMaxLength(500).IsRequired();
        builder.Property(candidate => candidate.SetupFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(candidate => candidate.ParametersJson).HasColumnType("longtext").IsRequired();
        builder.Property(candidate => candidate.StructureJson).HasColumnType("longtext").IsRequired();
        builder.Property(candidate => candidate.ConfidenceDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.RiskDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.FinalPipelineDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.RiskPolicyEligibilityDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.PortfolioRiskAssessmentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.RiskScoreDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.HardRuleComplianceDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.ExitOutcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.NetResult).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.DrawdownCalculationMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.ConfidenceReason).HasMaxLength(500);
        builder.Property(candidate => candidate.RiskReason).HasMaxLength(500);
        builder.Property(candidate => candidate.RiskPolicyReason).HasMaxLength(500);
        builder.Property(candidate => candidate.PositionSizingUnavailableReason).HasMaxLength(500);
        builder.Property(candidate => candidate.ConfidenceModelVersion).HasMaxLength(64);
        builder.Property(candidate => candidate.ConfidenceComponentsJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskProfileVersion).HasMaxLength(64);
        builder.Property(candidate => candidate.RiskProfileName).HasMaxLength(200);
        builder.Property(candidate => candidate.RiskProfileSource).HasMaxLength(32);
        builder.Property(candidate => candidate.RiskProfileSnapshotId).HasMaxLength(64);
        builder.Property(candidate => candidate.RiskModelVersion).HasMaxLength(64);
        builder.Property(candidate => candidate.RiskAssessmentVersion).HasMaxLength(64);
        builder.Property(candidate => candidate.RiskComponentsJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskRuleResultsJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskFailedRuleKeysJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskWarningRuleKeysJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskPolicyFailedRuleKeysJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.FinalPipelineRejectionSourcesJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskRejectedRuleKey).HasMaxLength(100);
        builder.Property(candidate => candidate.RawExitReason).HasMaxLength(200);
        builder.Property(candidate => candidate.ProposedEntryPrice).HasTradingDecimal();
        builder.Property(candidate => candidate.StopLoss).HasTradingDecimal();
        builder.Property(candidate => candidate.Target1).HasTradingDecimal();
        builder.Property(candidate => candidate.Target2).HasTradingDecimal();
        builder.Property(candidate => candidate.RewardRisk).HasTradingDecimal();
        builder.Property(candidate => candidate.ConfidenceScore).HasTradingDecimal();
        builder.Property(candidate => candidate.ConfidenceThreshold).HasTradingDecimal();
        builder.Property(candidate => candidate.ConfidenceMargin).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskScore).HasTradingDecimal();
        builder.Property(candidate => candidate.CandidateRiskScore).HasTradingDecimal();
        builder.Property(candidate => candidate.PortfolioRiskScore).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskThreshold).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskMargin).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskPerTradePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskAmount).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskAtStopPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.ProposedPositionSize).HasTradingDecimal();
        builder.Property(candidate => candidate.PositionNotional).HasTradingDecimal();
        builder.Property(candidate => candidate.ProposedLeverage).HasTradingDecimal();
        builder.Property(candidate => candidate.MinimumRequiredLeverage).HasTradingDecimal();
        builder.Property(candidate => candidate.AssessmentLeverage).HasTradingDecimal();
        builder.Property(candidate => candidate.PreferredLeverage).HasTradingDecimal();
        builder.Property(candidate => candidate.MaxLeverage).HasTradingDecimal();
        builder.Property(candidate => candidate.InitialMarginRequired).HasTradingDecimal();
        builder.Property(candidate => candidate.StopDistancePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.PositionExposurePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.NotionalExposurePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.MarginUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.EstimatedRoundTripFees).HasTradingDecimal();
        builder.Property(candidate => candidate.FeeToTargetPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.CurrentExposurePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.CurrentNotionalExposurePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.CurrentMarginUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.ConcurrentRiskPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.DailyLossUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.CurrentDrawdownPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskPolicyMinimumConfidence).HasTradingDecimal();
        builder.Property(candidate => candidate.RawGrossPnl).HasTradingDecimal();
        builder.Property(candidate => candidate.RawNetPnl).HasTradingDecimal();
        builder.Property(candidate => candidate.RawPnlPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.RawRMultiple).HasTradingDecimal();
        builder.Property(candidate => candidate.Mfe).HasTradingDecimal();
        builder.Property(candidate => candidate.Mae).HasTradingDecimal();
        builder.HasIndex(candidate => candidate.StrategyLabRunId);
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.SetupFingerprint }).IsUnique();
        builder.HasIndex(candidate => candidate.StrategyCode);
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.RawOutcomeStatus });
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.ConfidenceDecision });
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.RiskDecision });
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.ExitOutcome });
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.NetResult });

        // IndependentPaths/v1
        builder.Property(candidate => candidate.GenericRiskFieldSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.RiskPathAssessmentVersion).HasMaxLength(64);
        builder.Property(candidate => candidate.RiskOnlyFinancialRiskDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.RiskOnlyEntryDecision).HasConversion<string>().HasMaxLength(48);
        builder.Property(candidate => candidate.RiskOnlyRejectionSourcesJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskOnlyAssessmentJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.RiskOnlyCurrentDrawdownPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskOnlyDailyLossUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskOnlyCurrentMarginUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.RiskOnlyConcurrentRiskPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.FullPipelineFinancialRiskDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(candidate => candidate.FullPipelineEntryDecision).HasConversion<string>().HasMaxLength(48);
        builder.Property(candidate => candidate.FullPipelineRejectionSourcesJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.FullPipelineAssessmentJson).HasColumnType("longtext");
        builder.Property(candidate => candidate.FullPipelineCurrentDrawdownPercent).HasTradingDecimal();
        builder.Property(candidate => candidate.FullPipelineDailyLossUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.FullPipelineCurrentMarginUsagePercent).HasTradingDecimal();
        builder.Property(candidate => candidate.FullPipelineConcurrentRiskPercent).HasTradingDecimal();
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.RiskOnlyEntryDecision });
        builder.HasIndex(candidate => new { candidate.StrategyLabRunId, candidate.FullPipelineEntryDecision });
    }
}

internal sealed class StrategyResearchCandidatePortfolioAssessmentConfiguration
    : IEntityTypeConfiguration<StrategyResearchCandidatePortfolioAssessment>
{
    public void Configure(EntityTypeBuilder<StrategyResearchCandidatePortfolioAssessment> builder)
    {
        builder.ToTable("StrategyResearchCandidatePortfolioAssessments");
        builder.Property(a => a.PortfolioPath).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.AssessmentBalance).HasTradingDecimal();
        builder.Property(a => a.RiskAmount).HasTradingDecimal();
        builder.Property(a => a.Quantity).HasTradingDecimal();
        builder.Property(a => a.PositionNotional).HasTradingDecimal();
        builder.Property(a => a.MinimumRequiredLeverage).HasTradingDecimal();
        builder.Property(a => a.AssessmentLeverage).HasTradingDecimal();
        builder.Property(a => a.InitialMarginRequired).HasTradingDecimal();
        builder.Property(a => a.CandidateMarginUsagePercent).HasTradingDecimal();
        builder.Property(a => a.CurrentNotionalExposurePercent).HasTradingDecimal();
        builder.Property(a => a.CurrentMarginUsagePercent).HasTradingDecimal();
        builder.Property(a => a.ProjectedTotalNotionalExposurePercent).HasTradingDecimal();
        builder.Property(a => a.ProjectedTotalMarginUsagePercent).HasTradingDecimal();
        builder.Property(a => a.CurrentConcurrentRiskPercent).HasTradingDecimal();
        builder.Property(a => a.ProjectedConcurrentRiskPercent).HasTradingDecimal();
        builder.Property(a => a.CurrentDailyLossUsagePercent).HasTradingDecimal();
        builder.Property(a => a.CurrentDrawdownPercent).HasTradingDecimal();
        builder.Property(a => a.PortfolioRiskScore).HasTradingDecimal();
        builder.Property(a => a.RiskScoreDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.HardRuleComplianceDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.FinancialRiskDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.RiskReason).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.FailedRuleKeysJson).HasColumnType("longtext");
        builder.Property(a => a.WarningRuleKeysJson).HasColumnType("longtext");
        builder.Property(a => a.RuleResultsJson).HasColumnType("longtext");
        builder.Property(a => a.EntryDecision).HasConversion<string>().HasMaxLength(48);
        builder.Property(a => a.EntryDecisionReason).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.RejectionSourcesJson).HasColumnType("longtext");
        builder.Property(a => a.AssessmentVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(a => a.StrategyResearchCandidateId);
        builder.HasIndex(a => new { a.StrategyResearchCandidateId, a.PortfolioPath }).IsUnique();
        builder.HasIndex(a => a.FinancialRiskDecision);
        builder.HasIndex(a => a.EntryDecision);
        builder.HasIndex(a => a.CurrentDrawdownPercent);
        builder.HasIndex(a => a.CurrentDailyLossUsagePercent);
    }
}
