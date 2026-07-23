using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260714120000_AddStrategyLabTables")]
public partial class AddStrategyLabTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StrategyLabRuns",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                SymbolId = table.Column<long>(type: "bigint", nullable: false),
                Symbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ToUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ExecutionMode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ParametersJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyFeatureFlagsJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                FeeSettingsJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                SlippageSettingsJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ExperimentFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                AppVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                GitCommit = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CandleDatasetFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyCodeFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ErrorMessage = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RiskProfileId = table.Column<long>(type: "bigint", nullable: true),
                ResultSummaryJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                EvaluationsCount = table.Column<int>(type: "int", nullable: false),
                RawCandidateCount = table.Column<int>(type: "int", nullable: false),
                CurrentStage = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                PercentComplete = table.Column<decimal>(type: "decimal(28,12)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StrategyLabRuns", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "StrategyResearchCandidates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                StrategyLabRunId = table.Column<long>(type: "bigint", nullable: false),
                StrategyCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                SymbolId = table.Column<long>(type: "bigint", nullable: false),
                Symbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                SetupDetectedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ProposedEntryTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ProposedEntryPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                StopLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Target1 = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Target2 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                RewardRisk = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                CandidateStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                SetupFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ParametersJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StructureJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ConfidenceScore = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                ConfidenceDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ConfidenceReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RiskDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RiskReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FinalPipelineDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RawOutcomeStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RawExitTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                RawExitPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                RawExitReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RawGrossPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                RawNetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                RawPnlPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                RawRMultiple = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                Mfe = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                Mae = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                DurationBars = table.Column<int>(type: "int", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StrategyResearchCandidates", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyLabRuns_CreatedAtUtc",
            table: "StrategyLabRuns",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyLabRuns_Status",
            table: "StrategyLabRuns",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyLabRuns_StrategyCode",
            table: "StrategyLabRuns",
            column: "StrategyCode");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyCode",
            table: "StrategyResearchCandidates",
            column: "StrategyCode");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId",
            table: "StrategyResearchCandidates",
            column: "StrategyLabRunId");

        migrationBuilder.CreateIndex(
            name: "IX_StrategyResearchCandidates_StrategyLabRunId_SetupFingerprint",
            table: "StrategyResearchCandidates",
            columns: new[] { "StrategyLabRunId", "SetupFingerprint" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StrategyResearchCandidates");
        migrationBuilder.DropTable(name: "StrategyLabRuns");
    }
}
