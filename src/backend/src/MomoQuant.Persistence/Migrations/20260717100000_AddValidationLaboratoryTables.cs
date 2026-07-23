using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260717100000_AddValidationLaboratoryTables")]
public partial class AddValidationLaboratoryTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ValidationExperiments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ExperimentType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                SourceStrategyLabRunId = table.Column<long>(type: "bigint", nullable: true),
                ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                Exchange = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                SymbolId = table.Column<long>(type: "bigint", nullable: false),
                Symbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RequestedStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                RequestedEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                SplitRatio = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                SplitAlgorithmVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                TotalEligibleCandleCount = table.Column<int>(type: "int", nullable: false),
                TrainingCandleCount = table.Column<int>(type: "int", nullable: false),
                ValidationCandleCount = table.Column<int>(type: "int", nullable: false),
                TrainingStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                TrainingEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ValidationStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ValidationEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                SplitCandleOpenTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                RequiredWarmupCandles = table.Column<int>(type: "int", nullable: false),
                TrainingWarmupStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ValidationWarmupStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                WarmupAlgorithmVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CandleDataSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CandleDataFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                WarmupSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ParameterSearchSpaceSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                OptimizationObjectiveSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenStrategyParameterSnapshotJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenParameterFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenStrategyFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenConfidenceSnapshotJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenRiskSnapshotJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenCostModelSnapshotJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                QualificationProfileSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                DraftConfigurationJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                PrimaryQualificationLayer = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ValidationRevealStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FrozenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ValidationRevealedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ValidationRevealedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyRobustnessDecision = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                PrimaryFailureReason = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                FailureReasonsJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                QualificationRuleResultsJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                DecisionExplanation = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                DiagnosticsJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                OverlayResultsJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ComparisonJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                RegimeComparisonJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ParameterStabilityJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                TrainingStrategyLabRunId = table.Column<long>(type: "bigint", nullable: true),
                ValidationStrategyLabRunId = table.Column<long>(type: "bigint", nullable: true),
                BoundaryCensoredCount = table.Column<int>(type: "int", nullable: false),
                InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                MaximumTrials = table.Column<int>(type: "int", nullable: false),
                DeterministicSeed = table.Column<int>(type: "int", nullable: false),
                ErrorMessage = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CurrentStage = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                PercentComplete = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValidationExperiments", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "ValidationParameterTrials",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ValidationExperimentId = table.Column<long>(type: "bigint", nullable: false),
                TrialNumber = table.Column<int>(type: "int", nullable: false),
                ParameterSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ParameterFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                RawCandidateCount = table.Column<int>(type: "int", nullable: false),
                ClosedTradeCount = table.Column<int>(type: "int", nullable: false),
                WinnerCount = table.Column<int>(type: "int", nullable: false),
                LoserCount = table.Column<int>(type: "int", nullable: false),
                ExpiredCount = table.Column<int>(type: "int", nullable: false),
                NetExpectancyR = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                GrossPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                ProfitFactor = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                MaximumDrawdownPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                FeeImpactPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                TrainingScore = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                GuardrailDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                GuardrailFailureReasonsJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Rank = table.Column<int>(type: "int", nullable: true),
                DiagnosticWarningsJson = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyLabRunId = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValidationParameterTrials", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "ValidationSegmentResults",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ValidationExperimentId = table.Column<long>(type: "bigint", nullable: false),
                SegmentType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                LayerType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                StrategyLabRunId = table.Column<long>(type: "bigint", nullable: true),
                MetricsJson = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CandleCount = table.Column<int>(type: "int", nullable: false),
                CandidateCount = table.Column<int>(type: "int", nullable: false),
                ClosedTradeCount = table.Column<int>(type: "int", nullable: false),
                NetExpectancyR = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                ProfitFactor = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                NetReturnPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                MaximumDrawdownPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                TransactionCosts = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                BoundaryCensoredCount = table.Column<int>(type: "int", nullable: false),
                ResultFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValidationSegmentResults", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_ValidationExperiments_CreatedAtUtc",
            table: "ValidationExperiments",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ValidationExperiments_Status",
            table: "ValidationExperiments",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_ValidationExperiments_StrategyCode",
            table: "ValidationExperiments",
            column: "StrategyCode");

        migrationBuilder.CreateIndex(
            name: "IX_ValidationParameterTrials_ValidationExperimentId",
            table: "ValidationParameterTrials",
            column: "ValidationExperimentId");

        migrationBuilder.CreateIndex(
            name: "IX_ValidationParameterTrials_ValidationExperimentId_TrialNumber",
            table: "ValidationParameterTrials",
            columns: new[] { "ValidationExperimentId", "TrialNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ValidationSegmentResults_ValidationExperimentId",
            table: "ValidationSegmentResults",
            column: "ValidationExperimentId");

        migrationBuilder.CreateIndex(
            name: "IX_ValidationSegmentResults_ValidationExperimentId_SegmentType_LayerType",
            table: "ValidationSegmentResults",
            columns: new[] { "ValidationExperimentId", "SegmentType", "LayerType" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ValidationSegmentResults");
        migrationBuilder.DropTable(name: "ValidationParameterTrials");
        migrationBuilder.DropTable(name: "ValidationExperiments");
    }
}
