using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260717050000_AddStrategyLabIndependentPathsV2153")]
public partial class AddStrategyLabIndependentPathsV2153 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GenericRiskFieldSource",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskPathAssessmentVersion",
            table: "StrategyResearchCandidates",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskOnlyFinancialRiskDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskOnlyEntryDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(48)",
            maxLength: 48,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskOnlyRejectionSourcesJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskOnlyAssessmentJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskOnlyCurrentDrawdownPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskOnlyDailyLossUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskOnlyCurrentMarginUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskOnlyConcurrentRiskPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "RiskOnlyOpenPositionCount",
            table: "StrategyResearchCandidates",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FullPipelineFinancialRiskDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FullPipelineEntryDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(48)",
            maxLength: 48,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FullPipelineRejectionSourcesJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FullPipelineAssessmentJson",
            table: "StrategyResearchCandidates",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FullPipelineCurrentDrawdownPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FullPipelineDailyLossUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FullPipelineCurrentMarginUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FullPipelineConcurrentRiskPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "FullPipelineOpenPositionCount",
            table: "StrategyResearchCandidates",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_RiskOnlyEntryDecision",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "RiskOnlyEntryDecision" });

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_FullPipelineEntryDecision",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "FullPipelineEntryDecision" });

        migrationBuilder.CreateTable(
            name: "StrategyResearchCandidatePortfolioAssessments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                StrategyResearchCandidateId = table.Column<long>(type: "bigint", nullable: false),
                PortfolioPath = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                AssessmentBalance = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                RiskAmount = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                Quantity = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                PositionNotional = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                MinimumRequiredLeverage = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                AssessmentLeverage = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                InitialMarginRequired = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CandidateMarginUsagePercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CurrentNotionalExposurePercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CurrentMarginUsagePercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                ProjectedTotalNotionalExposurePercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                ProjectedTotalMarginUsagePercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CurrentConcurrentRiskPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                ProjectedConcurrentRiskPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CurrentDailyLossUsagePercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CurrentDrawdownPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                CurrentOpenPositionCount = table.Column<int>(type: "int", nullable: false),
                PortfolioRiskScore = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                RiskScoreDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                HardRuleComplianceDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                FinancialRiskDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                RiskReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                FailedRuleKeysJson = table.Column<string>(type: "longtext", nullable: true),
                WarningRuleKeysJson = table.Column<string>(type: "longtext", nullable: true),
                RuleResultsJson = table.Column<string>(type: "longtext", nullable: true),
                EntryDecision = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                EntryDecisionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                RejectionSourcesJson = table.Column<string>(type: "longtext", nullable: true),
                AssessmentVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                EvaluatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StrategyResearchCandidatePortfolioAssessments", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidatePortfolioAssessments_StrategyResearchCandidateId",
            table: "StrategyResearchCandidatePortfolioAssessments",
            column: "StrategyResearchCandidateId");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidatePortfolioAssessments_CandidateId_Path",
            table: "StrategyResearchCandidatePortfolioAssessments",
            columns: new[] { "StrategyResearchCandidateId", "PortfolioPath" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidatePortfolioAssessments_FinancialRiskDecision",
            table: "StrategyResearchCandidatePortfolioAssessments",
            column: "FinancialRiskDecision");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidatePortfolioAssessments_EntryDecision",
            table: "StrategyResearchCandidatePortfolioAssessments",
            column: "EntryDecision");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidatePortfolioAssessments_CurrentDrawdownPercent",
            table: "StrategyResearchCandidatePortfolioAssessments",
            column: "CurrentDrawdownPercent");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidatePortfolioAssessments_CurrentDailyLossUsagePercent",
            table: "StrategyResearchCandidatePortfolioAssessments",
            column: "CurrentDailyLossUsagePercent");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StrategyResearchCandidatePortfolioAssessments");

        migrationBuilder.DropIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_RiskOnlyEntryDecision",
            table: "StrategyResearchCandidates");

        migrationBuilder.DropIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_FullPipelineEntryDecision",
            table: "StrategyResearchCandidates");

        migrationBuilder.DropColumn(name: "GenericRiskFieldSource", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskPathAssessmentVersion", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyFinancialRiskDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyEntryDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyRejectionSourcesJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyAssessmentJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyCurrentDrawdownPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyDailyLossUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyCurrentMarginUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyConcurrentRiskPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskOnlyOpenPositionCount", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineFinancialRiskDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineEntryDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineRejectionSourcesJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineAssessmentJson", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineCurrentDrawdownPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineDailyLossUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineCurrentMarginUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineConcurrentRiskPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "FullPipelineOpenPositionCount", table: "StrategyResearchCandidates");
    }
}
