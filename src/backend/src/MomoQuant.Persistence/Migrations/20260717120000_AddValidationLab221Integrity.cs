using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260717120000_AddValidationLab221Integrity")]
public partial class AddValidationLab221Integrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ValidationMetricsVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationMetrics/v1.1")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "CandidateReconciliationJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "CandidateReconciliationStatus",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "LeakageAuditJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "LeakageAuditStatus",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ParameterStabilityApplicability",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "SegmentDetectorContinuityMode",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "FreshSessionWithWarmup")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ExpectancyMetric",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NetExpectancyR")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ProfitFactorMetric",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NetProfitFactor")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ResultCalculationVersion",
            table: "ValidationSegmentResults",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationMetrics/v1.1")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<decimal>(
            name: "GrossExpectancyR",
            table: "ValidationSegmentResults",
            type: "decimal(28,12)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossProfitFactor",
            table: "ValidationSegmentResults",
            type: "decimal(28,12)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "NetProfitFactor",
            table: "ValidationSegmentResults",
            type: "decimal(28,12)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossAverageR",
            table: "ValidationSegmentResults",
            type: "decimal(28,12)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "NetAverageR",
            table: "ValidationSegmentResults",
            type: "decimal(28,12)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossPnl",
            table: "ValidationSegmentResults",
            type: "decimal(28,12)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ValidationMetricsVersion", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "CandidateReconciliationJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "CandidateReconciliationStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "LeakageAuditJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "LeakageAuditStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ParameterStabilityApplicability", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SegmentDetectorContinuityMode", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ExpectancyMetric", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ProfitFactorMetric", table: "ValidationExperiments");

        migrationBuilder.DropColumn(name: "ResultCalculationVersion", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "GrossExpectancyR", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "GrossProfitFactor", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "NetProfitFactor", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "GrossAverageR", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "NetAverageR", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "GrossPnl", table: "ValidationSegmentResults");
    }
}
