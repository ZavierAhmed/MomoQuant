using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyBenchmarkRunItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BacktestPercent",
                table: "StrategyBenchmarkRuns",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CancellationRequested",
                table: "StrategyBenchmarkRuns",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DataPreparationPercent",
                table: "StrategyBenchmarkRuns",
                type: "decimal(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAtUtc",
                table: "StrategyBenchmarkRuns",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StrategyBenchmarkRunItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: false),
                    StrategyId = table.Column<long>(type: "bigint", nullable: false),
                    StrategyCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StrategyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BacktestRunId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    CandleCount = table.Column<int>(type: "int", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResultJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyBenchmarkRunItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyBenchmarkRunItems_StrategyBenchmarkRuns_BenchmarkRun~",
                        column: x => x.BenchmarkRunId,
                        principalTable: "StrategyBenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkRunItems_BenchmarkRunId",
                table: "StrategyBenchmarkRunItems",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkRunItems_BenchmarkRunId_Status",
                table: "StrategyBenchmarkRunItems",
                columns: new[] { "BenchmarkRunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkRunItems_BenchmarkRunId_StrategyId_SymbolId~",
                table: "StrategyBenchmarkRunItems",
                columns: new[] { "BenchmarkRunId", "StrategyId", "SymbolId", "Timeframe" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyBenchmarkRunItems");

            migrationBuilder.DropColumn(
                name: "BacktestPercent",
                table: "StrategyBenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CancellationRequested",
                table: "StrategyBenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "DataPreparationPercent",
                table: "StrategyBenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAtUtc",
                table: "StrategyBenchmarkRuns");
        }
    }
}
