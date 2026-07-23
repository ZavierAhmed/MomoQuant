using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260723120000_AddValidationCandleAccessAudits")]
public partial class AddValidationCandleAccessAudits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ValidationCandleAccessAudits",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ValidationExperimentId = table.Column<long>(type: "bigint", nullable: false),
                TrialId = table.Column<long>(type: "bigint", nullable: true),
                TrialNumber = table.Column<int>(type: "int", nullable: true),
                CallerComponent = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                RequestedStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                RequestedEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ReturnedStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ReturnedEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ReturnedCandleCount = table.Column<int>(type: "int", nullable: false),
                MinimumReturnedTimestampUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                MaximumReturnedTimestampUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CandleContentFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                AccessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                WasDenied = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DenialReason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValidationCandleAccessAudits", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ValCandleAccess_ExperimentId",
            table: "ValidationCandleAccessAudits",
            column: "ValidationExperimentId");

        migrationBuilder.CreateIndex(
            name: "IX_ValCandleAccess_Experiment_Accessed",
            table: "ValidationCandleAccessAudits",
            columns: new[] { "ValidationExperimentId", "AccessedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ValidationCandleAccessAudits");
    }
}
