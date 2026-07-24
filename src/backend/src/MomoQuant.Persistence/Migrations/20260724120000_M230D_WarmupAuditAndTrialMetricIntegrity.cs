using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

/// <summary>
/// Milestone 23.0D WP11–15: ValidationCandleAccessAudits evidence integrity columns.
///
/// Columns added by this WP (audit persistence):
/// - ScopeSequenceNumber (bigint, not null, default 0)
/// - AccessPurpose (varchar(64), nullable)
/// - DenialCode (varchar(64), nullable)
/// - CorrelationId (varchar(64), nullable)
/// - DatasetPartition (varchar(64), nullable)
/// - RequestedCandleCount (int, nullable)
/// - Index IX_ValCandleAccess_Scope_Sequence (ScopeExecutionId, ScopeSequenceNumber)
///
/// Coordination note for metrics agent (WP trial metrics / dual-status):
/// Prefer extending THIS migration carefully if columns are not yet applied in any environment,
/// OR create 20260724120100_M230D_TrialMetricIntegrity.cs for trial-metric columns so both
/// agents do not race the same Up() body after apply. Do not invent PSBR changes here.
/// </summary>
[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260724120000_M230D_WarmupAuditAndTrialMetricIntegrity")]
public partial class M230D_WarmupAuditAndTrialMetricIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ScopeSequenceNumber",
            table: "ValidationCandleAccessAudits",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<string>(
            name: "AccessPurpose",
            table: "ValidationCandleAccessAudits",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "DenialCode",
            table: "ValidationCandleAccessAudits",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            table: "ValidationCandleAccessAudits",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "DatasetPartition",
            table: "ValidationCandleAccessAudits",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<int>(
            name: "RequestedCandleCount",
            table: "ValidationCandleAccessAudits",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ValCandleAccess_Scope_Sequence",
            table: "ValidationCandleAccessAudits",
            columns: new[] { "ScopeExecutionId", "ScopeSequenceNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ValCandleAccess_Scope_Sequence",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "RequestedCandleCount",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "DatasetPartition",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "CorrelationId",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "DenialCode",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "AccessPurpose",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "ScopeSequenceNumber",
            table: "ValidationCandleAccessAudits");
    }
}
