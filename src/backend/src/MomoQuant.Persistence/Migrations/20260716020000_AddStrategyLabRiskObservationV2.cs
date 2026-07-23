using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260716020000_AddStrategyLabRiskObservationV2")]
public partial class AddStrategyLabRiskObservationV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RiskProfileSnapshotJson",
            table: "StrategyLabRuns",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CandidateRiskScore",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PortfolioRiskScore",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PortfolioRiskAssessmentStatus",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskModelVersion",
            table: "StrategyResearchCandidates",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskAssessmentVersion",
            table: "StrategyResearchCandidates",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskComponentsJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskRuleResultsJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskFailedRuleKeysJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskWarningRuleKeysJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskAmount",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PositionNotional",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PositionExposurePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "EstimatedRoundTripFees",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FeeToTargetPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PositionSizingUnavailableReason",
            table: "StrategyResearchCandidates",
            type: "varchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ConcurrentRiskPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ConcurrentPositionCount",
            table: "StrategyResearchCandidates",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskPolicyEligibilityDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskPolicyReason",
            table: "StrategyResearchCandidates",
            type: "varchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskPolicyFailedRuleKeysJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskPolicyMinimumConfidence",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FinalPipelineRejectionSourcesJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RiskProfileSnapshotJson", table: "StrategyLabRuns");
        migrationBuilder.DropColumn(name: "CandidateRiskScore", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "PortfolioRiskScore", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "PortfolioRiskAssessmentStatus", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskModelVersion", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskAssessmentVersion", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskComponentsJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskRuleResultsJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskFailedRuleKeysJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskWarningRuleKeysJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskAmount", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "PositionNotional", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "PositionExposurePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "EstimatedRoundTripFees", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FeeToTargetPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "PositionSizingUnavailableReason", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ConcurrentRiskPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ConcurrentPositionCount", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskPolicyEligibilityDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskPolicyReason", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskPolicyFailedRuleKeysJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskPolicyMinimumConfidence", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FinalPipelineRejectionSourcesJson", table: "StrategyResearchCandidates");
    }
}
