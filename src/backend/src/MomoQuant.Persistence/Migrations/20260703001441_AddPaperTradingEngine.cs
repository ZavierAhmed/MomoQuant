using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTradingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CapturedAtUtc",
                table: "PaperAccountSnapshots",
                newName: "TimestampUtc");

            migrationBuilder.AddColumn<decimal>(
                name: "Drawdown",
                table: "PaperAccountSnapshots",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DrawdownPercent",
                table: "PaperAccountSnapshots",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "OpenPositionCount",
                table: "PaperAccountSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "PaperSessionId",
                table: "PaperAccountSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalFees",
                table: "PaperAccountSnapshots",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentEquity",
                table: "PaperAccounts",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDrawdown",
                table: "PaperAccounts",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDrawdownPercent",
                table: "PaperAccounts",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalFees",
                table: "PaperAccounts",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalRealizedPnl",
                table: "PaperAccounts",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalUnrealizedPnl",
                table: "PaperAccounts",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PaperTradingSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaperAccountId = table.Column<long>(type: "bigint", nullable: false),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                    RiskProfileId = table.Column<long>(type: "bigint", nullable: false),
                    ExecutionMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UseAiScoring = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinConfidenceScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ToUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CurrentCandleTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CurrentCandleIndex = table.Column<int>(type: "int", nullable: false),
                    TotalCandles = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PausedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StoppedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfigJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperTradingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperTradingSessions_PaperAccounts_PaperAccountId",
                        column: x => x.PaperAccountId,
                        principalTable: "PaperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperTradingSessions_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PaperAccountSnapshots_PaperSessionId",
                table: "PaperAccountSnapshots",
                column: "PaperSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_PaperAccountId",
                table: "PaperTradingSessions",
                column: "PaperAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_TradingSessionId",
                table: "PaperTradingSessions",
                column: "TradingSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaperAccountSnapshots_PaperTradingSessions_PaperSessionId",
                table: "PaperAccountSnapshots",
                column: "PaperSessionId",
                principalTable: "PaperTradingSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperAccountSnapshots_PaperTradingSessions_PaperSessionId",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropTable(
                name: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperAccountSnapshots_PaperSessionId",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropColumn(
                name: "Drawdown",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropColumn(
                name: "DrawdownPercent",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropColumn(
                name: "OpenPositionCount",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropColumn(
                name: "PaperSessionId",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalFees",
                table: "PaperAccountSnapshots");

            migrationBuilder.DropColumn(
                name: "CurrentEquity",
                table: "PaperAccounts");

            migrationBuilder.DropColumn(
                name: "MaxDrawdown",
                table: "PaperAccounts");

            migrationBuilder.DropColumn(
                name: "MaxDrawdownPercent",
                table: "PaperAccounts");

            migrationBuilder.DropColumn(
                name: "TotalFees",
                table: "PaperAccounts");

            migrationBuilder.DropColumn(
                name: "TotalRealizedPnl",
                table: "PaperAccounts");

            migrationBuilder.DropColumn(
                name: "TotalUnrealizedPnl",
                table: "PaperAccounts");

            migrationBuilder.RenameColumn(
                name: "TimestampUtc",
                table: "PaperAccountSnapshots",
                newName: "CapturedAtUtc");
        }
    }
}
