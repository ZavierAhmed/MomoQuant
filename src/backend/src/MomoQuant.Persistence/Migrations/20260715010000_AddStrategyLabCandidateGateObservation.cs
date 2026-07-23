using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260715010000_AddStrategyLabCandidateGateObservation")]
public partial class AddStrategyLabCandidateGateObservation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ConfidenceThreshold",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ConfidenceMargin",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ConfidenceModelVersion",
            table: "StrategyResearchCandidates",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ConfidenceEvaluatedAtUtc",
            table: "StrategyResearchCandidates",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskScore",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskThreshold",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskMargin",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "RiskPerTradePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ProposedPositionSize",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ProposedLeverage",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "StopDistancePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CurrentExposurePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "DailyLossUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CurrentDrawdownPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "RiskProfileId",
            table: "StrategyResearchCandidates",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskProfileVersion",
            table: "StrategyResearchCandidates",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskRejectedRuleKey",
            table: "StrategyResearchCandidates",
            type: "varchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "RiskEvaluatedAtUtc",
            table: "StrategyResearchCandidates",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_RawOutcomeStatus",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "RawOutcomeStatus" });

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_ConfidenceDecision",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "ConfidenceDecision" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_RawOutcomeStatus",
            table: "StrategyResearchCandidates");

        migrationBuilder.DropIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_ConfidenceDecision",
            table: "StrategyResearchCandidates");

        migrationBuilder.DropColumn(name: "ConfidenceThreshold", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ConfidenceMargin", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ConfidenceModelVersion", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ConfidenceEvaluatedAtUtc", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskScore", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskThreshold", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskMargin", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskPerTradePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ProposedPositionSize", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ProposedLeverage", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "StopDistancePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "CurrentExposurePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "DailyLossUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "CurrentDrawdownPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskProfileId", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskProfileVersion", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskRejectedRuleKey", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskEvaluatedAtUtc", table: "StrategyResearchCandidates");
    }
}
