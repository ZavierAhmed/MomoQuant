using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260720100000_AddValidationTrainingDurability")]
public partial class AddValidationTrainingDurability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ErrorMessage",
            table: "ValidationParameterTrials",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "RecoverySource",
            table: "ValidationParameterTrials",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "None")
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "ValidationExperimentExecutionLeases",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ValidationExperimentId = table.Column<long>(type: "bigint", nullable: false),
                LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                AcquiredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                HeartbeatAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValidationExperimentExecutionLeases", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.Sql(
            """
            DELETE t
            FROM ValidationParameterTrials t
            INNER JOIN (
                SELECT ValidationExperimentId, ParameterFingerprint, MAX(Id) AS KeepId
                FROM ValidationParameterTrials
                GROUP BY ValidationExperimentId, ParameterFingerprint
                HAVING COUNT(*) > 1
            ) d ON t.ValidationExperimentId = d.ValidationExperimentId
               AND t.ParameterFingerprint = d.ParameterFingerprint
               AND t.Id <> d.KeepId;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_ValTrials_ExpId_Fingerprint",
            table: "ValidationParameterTrials",
            columns: new[] { "ValidationExperimentId", "ParameterFingerprint" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ValExpLeases_ExperimentId",
            table: "ValidationExperimentExecutionLeases",
            column: "ValidationExperimentId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ValidationExperimentExecutionLeases");

        migrationBuilder.DropIndex(
            name: "IX_ValTrials_ExpId_Fingerprint",
            table: "ValidationParameterTrials");

        migrationBuilder.DropColumn(name: "ErrorMessage", table: "ValidationParameterTrials");
        migrationBuilder.DropColumn(name: "RecoverySource", table: "ValidationParameterTrials");
    }
}
