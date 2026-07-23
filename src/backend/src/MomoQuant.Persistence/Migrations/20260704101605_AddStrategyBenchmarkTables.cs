using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyBenchmarkTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StrategyBenchmarkRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeframesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StrategyIdsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BenchmarkFromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    BenchmarkToUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    WarmupFromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    WarmupToUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    RiskProfileId = table.Column<long>(type: "bigint", nullable: false),
                    ExecutionMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    OrderExpiryCandles = table.Column<int>(type: "int", nullable: false),
                    UseAiScoring = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinConfidenceScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    IncludeDisabledStrategies = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConfigJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentStage = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PercentComplete = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    CurrentSymbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentStrategy = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedRuns = table.Column<int>(type: "int", nullable: false),
                    TotalRuns = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyBenchmarkRuns", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategyBenchmarkResults",
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
                    SymbolId = table.Column<long>(type: "bigint", nullable: true),
                    Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BacktestRunId = table.Column<long>(type: "bigint", nullable: true),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FinalBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    NetPnlPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    GrossProfit = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    GrossLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MaxDrawdownPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TotalTrades = table.Column<int>(type: "int", nullable: false),
                    WinningTrades = table.Column<int>(type: "int", nullable: false),
                    LosingTrades = table.Column<int>(type: "int", nullable: false),
                    BreakEvenTrades = table.Column<int>(type: "int", nullable: false),
                    WinRatePercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    AverageWin = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    AverageLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    LargestWin = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    LargestLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    AverageRewardRisk = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TotalFees = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TotalSignals = table.Column<int>(type: "int", nullable: false),
                    EntrySignals = table.Column<int>(type: "int", nullable: false),
                    NoTradeSignals = table.Column<int>(type: "int", nullable: false),
                    ApprovedSignals = table.Column<int>(type: "int", nullable: false),
                    RejectedSignals = table.Column<int>(type: "int", nullable: false),
                    MissedOrders = table.Column<int>(type: "int", nullable: false),
                    FilledOrders = table.Column<int>(type: "int", nullable: false),
                    AverageConfidenceScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Grade = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    StrengthsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WeaknessesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarningsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyBenchmarkResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyBenchmarkResults_StrategyBenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "StrategyBenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkResults_BenchmarkRunId",
                table: "StrategyBenchmarkResults",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkResults_BenchmarkRunId_StrategyCode",
                table: "StrategyBenchmarkResults",
                columns: new[] { "BenchmarkRunId", "StrategyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkRuns_CreatedAt",
                table: "StrategyBenchmarkRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyBenchmarkRuns_Status",
                table: "StrategyBenchmarkRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyBenchmarkResults");

            migrationBuilder.DropTable(
                name: "StrategyBenchmarkRuns");
        }
    }
}
