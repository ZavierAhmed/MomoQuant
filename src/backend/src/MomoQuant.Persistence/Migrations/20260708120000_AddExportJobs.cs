using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260708120000_AddExportJobs")]
public partial class AddExportJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExportJobs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Scope = table.Column<int>(type: "int", nullable: false),
                SourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Format = table.Column<int>(type: "int", nullable: false),
                DetailLevel = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                FileName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                FilePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                ContentType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ErrorMessage = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                RequestedByUserId = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportJobs", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_ExportJobs_Scope_SourceId_RequestedAtUtc",
            table: "ExportJobs",
            columns: new[] { "Scope", "SourceId", "RequestedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ExportJobs");
    }
}
