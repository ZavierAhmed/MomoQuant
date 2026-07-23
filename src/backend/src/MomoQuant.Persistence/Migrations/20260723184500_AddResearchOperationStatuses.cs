using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260723184500_AddResearchOperationStatuses")]
public partial class AddResearchOperationStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ResearchOperationStatuses",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                OperationId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                OperationType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Stage = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                PercentComplete = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                RequestedWorkCount = table.Column<int>(type: "int", nullable: false),
                CompletedWorkCount = table.Column<int>(type: "int", nullable: false),
                FailedWorkCount = table.Column<int>(type: "int", nullable: false),
                ActiveWorkItem = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastProgressAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastHeartbeatAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ErrorCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                UserSafeErrorMessage = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                DiagnosticReference = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ResearchOperationStatuses", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ResearchOperationStatuses_OperationId",
            table: "ResearchOperationStatuses",
            column: "OperationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ResearchOperationStatuses_CorrelationId",
            table: "ResearchOperationStatuses",
            column: "CorrelationId");

        migrationBuilder.CreateIndex(
            name: "IX_ResearchOperationStatuses_OperationType_EntityId",
            table: "ResearchOperationStatuses",
            columns: new[] { "OperationType", "EntityId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ResearchOperationStatuses");
    }
}
