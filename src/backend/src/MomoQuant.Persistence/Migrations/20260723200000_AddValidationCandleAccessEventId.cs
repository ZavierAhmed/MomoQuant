using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260723200000_AddValidationCandleAccessEventId")]
public partial class AddValidationCandleAccessEventId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AccessEventId",
            table: "ValidationCandleAccessAudits",
            type: "char(36)",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<Guid>(
            name: "ScopeExecutionId",
            table: "ValidationCandleAccessAudits",
            type: "char(36)",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<int>(
            name: "FlushAttemptCount",
            table: "ValidationCandleAccessAudits",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "PersistedAtUtc",
            table: "ValidationCandleAccessAudits",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RecorderVersion",
            table: "ValidationCandleAccessAudits",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationCandleAccess/v1")
            .Annotation("MySql:CharSet", "utf8mb4");

        // Backfill durable AccessEventId / ScopeExecutionId for any pre-existing rows.
        migrationBuilder.Sql(
            """
            UPDATE ValidationCandleAccessAudits
            SET AccessEventId = UUID(),
                ScopeExecutionId = UUID(),
                PersistedAtUtc = COALESCE(PersistedAtUtc, CreatedAtUtc),
                RecorderVersion = CASE
                    WHEN RecorderVersion IS NULL OR RecorderVersion = '' THEN 'ValidationCandleAccess/v1'
                    ELSE RecorderVersion
                END
            WHERE AccessEventId = '00000000-0000-0000-0000-000000000000'
               OR ScopeExecutionId = '00000000-0000-0000-0000-000000000000';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_ValCandleAccess_AccessEventId",
            table: "ValidationCandleAccessAudits",
            column: "AccessEventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ValCandleAccess_Exp_Trial_Scope_Accessed",
            table: "ValidationCandleAccessAudits",
            columns: new[] { "ValidationExperimentId", "TrialNumber", "ScopeExecutionId", "AccessedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ValCandleAccess_Exp_Trial_Scope_Accessed",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropIndex(
            name: "IX_ValCandleAccess_AccessEventId",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "RecorderVersion",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "PersistedAtUtc",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "FlushAttemptCount",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "ScopeExecutionId",
            table: "ValidationCandleAccessAudits");

        migrationBuilder.DropColumn(
            name: "AccessEventId",
            table: "ValidationCandleAccessAudits");
    }
}
