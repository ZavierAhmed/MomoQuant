using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

/// <summary>
/// Milestone 23.0D — persisted ValidationMetrics/v1.3.2 trial metric snapshots
/// (WP17 snapshot + fingerprint + population counts + dual statuses), snapshot-based
/// selection fields (WP20), and trial/segment reconciliation fields (WP21).
/// </summary>
[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260724120100_M230D_TrialMetricSnapshotFields")]
public partial class M230D_TrialMetricSnapshotFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ValidationParameterTrials — trial metric snapshot fields
        migrationBuilder.AddColumn<string>(
            name: "TrialMetricSnapshotJson",
            table: "ValidationParameterTrials",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrialMetricFingerprint",
            table: "ValidationParameterTrials",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrialMetricsVersion",
            table: "ValidationParameterTrials",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrainingScoreVersion",
            table: "ValidationParameterTrials",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "GuardrailEvaluationJson",
            table: "ValidationParameterTrials",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<int>(
            name: "CandidatePopulationCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BoundaryEligibleCandidateCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "IncludedPathInputCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ExcludedPathInputCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ClosedOutcomePopulationCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "MonetaryPnlPopulationCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "GrossRPopulationCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "NetRPopulationCount",
            table: "ValidationParameterTrials",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IncludedPopulationRiskStatus",
            table: "ValidationParameterTrials",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "CompletePathInputIntegrityStatus",
            table: "ValidationParameterTrials",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrialRankEligibility",
            table: "ValidationParameterTrials",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NotEvaluated")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "RankIneligibleReasonsJson",
            table: "ValidationParameterTrials",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        // ValidationExperiments — snapshot-based selection + trial/segment reconciliation
        migrationBuilder.AddColumn<string>(
            name: "SelectedMetricFingerprint",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrialSegmentReconciliationStatus",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NotEvaluated")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrialSegmentReconciliationJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TrialSegmentReconciliationJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "TrialSegmentReconciliationStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SelectedMetricFingerprint", table: "ValidationExperiments");

        migrationBuilder.DropColumn(name: "RankIneligibleReasonsJson", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "TrialRankEligibility", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "CompletePathInputIntegrityStatus", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "IncludedPopulationRiskStatus", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "NetRPopulationCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "GrossRPopulationCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "MonetaryPnlPopulationCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "ClosedOutcomePopulationCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "ExcludedPathInputCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "IncludedPathInputCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "BoundaryEligibleCandidateCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "CandidatePopulationCount", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "GuardrailEvaluationJson", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "TrainingScoreVersion", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "TrialMetricsVersion", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "TrialMetricFingerprint", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "TrialMetricSnapshotJson", table: "ValidationParameterTrials");
    }
}
