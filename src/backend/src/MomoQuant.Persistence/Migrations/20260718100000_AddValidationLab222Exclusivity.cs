using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260718100000_AddValidationLab222Exclusivity")]
public partial class AddValidationLab222Exclusivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "ValidationMetricsVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationMetrics/v1.2")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "HoldoutExclusivityPolicyVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationHoldoutExclusivity/v1")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "HoldoutExclusivityJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "MetricConsistencyJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "MetricConsistencyStatus",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ExportVerificationJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ExportVerificationStatus",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ValidationLaboratoryReadinessStatus",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<int>(
            name: "CrossSegmentOverlapCount",
            table: "ValidationExperiments",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AlterColumn<string>(
            name: "ResultCalculationVersion",
            table: "ValidationSegmentResults",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationMetrics/v1.2")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<int>(
            name: "PersistedCandidateRowCount",
            table: "ValidationSegmentResults",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "MetricIncludedCandidateCount",
            table: "ValidationSegmentResults",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "MetricExcludedCandidateCount",
            table: "ValidationSegmentResults",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "CrossSegmentOverlapCount",
            table: "ValidationSegmentResults",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossProfit",
            table: "ValidationSegmentResults",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossLoss",
            table: "ValidationSegmentResults",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "NetProfit",
            table: "ValidationSegmentResults",
            type: "decimal(18,8)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "NetLoss",
            table: "ValidationSegmentResults",
            type: "decimal(18,8)",
            nullable: true);

        // Legacy label experiments 1–3 as ValidationMetrics/v1 when still on older defaults.
        migrationBuilder.Sql("""
            UPDATE ValidationExperiments
            SET ValidationMetricsVersion = 'ValidationMetrics/v1'
            WHERE Id IN (1, 2, 3)
              AND (ValidationMetricsVersion IS NULL
                   OR ValidationMetricsVersion = ''
                   OR ValidationMetricsVersion = 'ValidationMetrics/v1.1'
                   OR ValidationMetricsVersion = 'ValidationMetrics/v1');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "HoldoutExclusivityPolicyVersion", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "HoldoutExclusivityJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "MetricConsistencyJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "MetricConsistencyStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ExportVerificationJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ExportVerificationStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ValidationLaboratoryReadinessStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "CrossSegmentOverlapCount", table: "ValidationExperiments");

        migrationBuilder.DropColumn(name: "PersistedCandidateRowCount", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "MetricIncludedCandidateCount", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "MetricExcludedCandidateCount", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "CrossSegmentOverlapCount", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "GrossProfit", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "GrossLoss", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "NetProfit", table: "ValidationSegmentResults");
        migrationBuilder.DropColumn(name: "NetLoss", table: "ValidationSegmentResults");

        migrationBuilder.AlterColumn<string>(
            name: "ValidationMetricsVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationMetrics/v1.1")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AlterColumn<string>(
            name: "ResultCalculationVersion",
            table: "ValidationSegmentResults",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationMetrics/v1.1")
            .Annotation("MySql:CharSet", "utf8mb4");
    }
}
