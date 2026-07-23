using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260707120000_AddSkLivePaperTables")]
public partial class AddSkLivePaperTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SkLivePaperSessions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                SessionName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                SymbolId = table.Column<long>(type: "bigint", nullable: false),
                Symbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                HigherTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                PrimaryTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                AdditionalTimeframesJson = table.Column<string>(type: "longtext", nullable: false),
                StartingBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                CurrentBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                RiskPerPaperTradePercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                MaxPaperTradesPerDay = table.Column<int>(type: "int", nullable: false),
                MaxOpenPaperPositions = table.Column<int>(type: "int", nullable: false),
                AllowLong = table.Column<bool>(type: "tinyint(1)", nullable: false),
                AllowShort = table.Column<bool>(type: "tinyint(1)", nullable: false),
                RequireHtfAgreement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                MinClarityScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                MinUsefulnessScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                RequireReactionConfirmation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                ConfirmationMode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                SimulatedLeverage = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                StoppedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastHeartbeatUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastAnalyzedCandleUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastError = table.Column<string>(type: "longtext", nullable: true),
                TradesOpenedToday = table.Column<int>(type: "int", nullable: false),
                TradesOpenedDayUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                SimulationMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_SkLivePaperSessions", x => x.Id))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "SkLivePaperCandidates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                SessionId = table.Column<long>(type: "bigint", nullable: false),
                AnalysisId = table.Column<long>(type: "bigint", nullable: true),
                SymbolId = table.Column<long>(type: "bigint", nullable: false),
                Symbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                HigherTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                PrimaryTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                SequenceStatus = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                ValidityStatus = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                UsefulnessStatus = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                ClarityScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                UsefulnessScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                ReactionZoneLow = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                ReactionZoneHigh = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                StrongReactionZoneLow = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                StrongReactionZoneHigh = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                InvalidationLevel = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Target1 = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Target2 = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                CurrentPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                CandidateStatus = table.Column<int>(type: "int", nullable: false),
                RejectionReason = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                ConfirmedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ExpiredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CandidateKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_SkLivePaperCandidates", x => x.Id))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "SkLivePaperTrades",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                SessionId = table.Column<long>(type: "bigint", nullable: false),
                CandidateId = table.Column<long>(type: "bigint", nullable: true),
                SymbolId = table.Column<long>(type: "bigint", nullable: false),
                Symbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                EntryTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                EntryPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                SimulatedLeverage = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                MarginUsed = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                NotionalValue = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                StopLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                TakeProfit1 = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                TakeProfit2 = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                ExitTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ExitPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                ExitReason = table.Column<int>(type: "int", nullable: true),
                GrossPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Fees = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                Slippage = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                NetPnlPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                ClarityScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                UsefulnessScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                HtfDirection = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                LtfDirection = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                SimulationMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_SkLivePaperTrades", x => x.Id))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "SkLivePaperEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                SessionId = table.Column<long>(type: "bigint", nullable: false),
                EventType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "longtext", nullable: false),
                DetailsJson = table.Column<string>(type: "longtext", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SkLivePaperEvents", x => x.Id))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(name: "IX_SkLivePaperSessions_Status", table: "SkLivePaperSessions", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_SkLivePaperSessions_SymbolId", table: "SkLivePaperSessions", column: "SymbolId");
        migrationBuilder.CreateIndex(name: "IX_SkLivePaperCandidates_SessionId", table: "SkLivePaperCandidates", column: "SessionId");
        migrationBuilder.CreateIndex(name: "IX_SkLivePaperTrades_SessionId", table: "SkLivePaperTrades", column: "SessionId");
        migrationBuilder.CreateIndex(name: "IX_SkLivePaperEvents_SessionId", table: "SkLivePaperEvents", column: "SessionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SkLivePaperEvents");
        migrationBuilder.DropTable(name: "SkLivePaperTrades");
        migrationBuilder.DropTable(name: "SkLivePaperCandidates");
        migrationBuilder.DropTable(name: "SkLivePaperSessions");
    }
}
