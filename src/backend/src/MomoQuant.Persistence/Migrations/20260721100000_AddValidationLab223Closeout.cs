using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260721100000_AddValidationLab223Closeout")]
public partial class AddValidationLab223Closeout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCanonical",
            table: "ValidationExperiments",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "SupersessionStatus",
            table: "ValidationExperiments",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "None")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<long>(
            name: "SupersededByExperimentId",
            table: "ValidationExperiments",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "SupersededAtUtc",
            table: "ValidationExperiments",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SupersessionReason",
            table: "ValidationExperiments",
            type: "varchar(512)",
            maxLength: 512,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<long>(
            name: "SelectedTrialId",
            table: "ValidationExperiments",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SelectionIntegrityStatus",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "NotEvaluated")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "CloseoutAuditJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "TrialPopulationSummaryJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "ResearchStatus",
            table: "Strategies",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NotEvaluated")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<bool>(
            name: "DeploymentQualificationEligible",
            table: "Strategies",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<long>(
            name: "CanonicalValidationExperimentId",
            table: "Strategies",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResearchDecisionJson",
            table: "Strategies",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<DateTime>(
            name: "ResearchDecisionAtUtc",
            table: "Strategies",
            type: "datetime(6)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsCanonical", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SupersessionStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SupersededByExperimentId", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SupersededAtUtc", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SupersessionReason", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SelectedTrialId", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SelectionIntegrityStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "CloseoutAuditJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "TrialPopulationSummaryJson", table: "ValidationExperiments");

        migrationBuilder.DropColumn(name: "ResearchStatus", table: "Strategies");
        migrationBuilder.DropColumn(name: "DeploymentQualificationEligible", table: "Strategies");
        migrationBuilder.DropColumn(name: "CanonicalValidationExperimentId", table: "Strategies");
        migrationBuilder.DropColumn(name: "ResearchDecisionJson", table: "Strategies");
        migrationBuilder.DropColumn(name: "ResearchDecisionAtUtc", table: "Strategies");
    }
}
