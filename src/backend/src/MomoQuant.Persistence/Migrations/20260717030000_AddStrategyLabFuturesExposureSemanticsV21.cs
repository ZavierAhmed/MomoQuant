using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260717030000_AddStrategyLabFuturesExposureSemanticsV21")]
public partial class AddStrategyLabFuturesExposureSemanticsV21 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "RiskAtStopPercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "NotionalExposurePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MarginUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MinimumRequiredLeverage",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "AssessmentLeverage",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PreferredLeverage",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MaxLeverage",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "InitialMarginRequired",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CurrentNotionalExposurePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CurrentMarginUsagePercent",
            table: "StrategyResearchCandidates",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskScoreDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "HardRuleComplianceDecision",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskProfileName",
            table: "StrategyResearchCandidates",
            type: "varchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskProfileSource",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RiskProfileSnapshotId",
            table: "StrategyResearchCandidates",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DrawdownCalculationMode",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExitOutcome",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NotSet");

        migrationBuilder.AddColumn<string>(
            name: "NetResult",
            table: "StrategyResearchCandidates",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_ExitOutcome",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "ExitOutcome" });

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_NetResult",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "NetResult" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_ExitOutcome",
            table: "StrategyResearchCandidates");

        migrationBuilder.DropIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_NetResult",
            table: "StrategyResearchCandidates");

        migrationBuilder.DropColumn(name: "RiskAtStopPercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "NotionalExposurePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "MarginUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "MinimumRequiredLeverage", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "AssessmentLeverage", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "PreferredLeverage", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "MaxLeverage", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "InitialMarginRequired", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "CurrentNotionalExposurePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "CurrentMarginUsagePercent", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskScoreDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "HardRuleComplianceDecision", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskProfileName", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskProfileSource", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "RiskProfileSnapshotId", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "DrawdownCalculationMode", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "ExitOutcome", table: "StrategyResearchCandidates");
        migrationBuilder.DropColumn(name: "NetResult", table: "StrategyResearchCandidates");
    }
}
