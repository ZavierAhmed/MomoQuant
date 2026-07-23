using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MomoQuantDbContext))]
    [Migration("20260706120000_AddSimulationRunSummaries")]
    public partial class AddSimulationRunSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SimulationRunSummaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SymbolsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StrategiesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeframesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EvaluationMode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FinalBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    NetPnlPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TotalTrades = table.Column<int>(type: "int", nullable: false),
                    WinningTrades = table.Column<int>(type: "int", nullable: false),
                    LosingTrades = table.Column<int>(type: "int", nullable: false),
                    WinRatePercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    CandidateSignals = table.Column<int>(type: "int", nullable: false),
                    ConfidenceRejected = table.Column<int>(type: "int", nullable: false),
                    RiskRejected = table.Column<int>(type: "int", nullable: false),
                    ExecutedTrades = table.Column<int>(type: "int", nullable: false),
                    ShadowTrades = table.Column<int>(type: "int", nullable: false),
                    ShadowNetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    RejectedWouldHaveWon = table.Column<int>(type: "int", nullable: false),
                    RejectedWouldHaveLost = table.Column<int>(type: "int", nullable: false),
                    SummaryText = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyFindingsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarningsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationRunSummaries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRunSummaries_CreatedAt",
                table: "SimulationRunSummaries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRunSummaries_SourceType_SourceId",
                table: "SimulationRunSummaries",
                columns: new[] { "SourceType", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SimulationRunSummaries");
        }
    }
}
