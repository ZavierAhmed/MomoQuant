using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "Name", "`Name` varchar(256) NOT NULL DEFAULT ''''");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "ExchangeId", "`ExchangeId` bigint NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "InitialBalance", "`InitialBalance` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "CurrentBalance", "`CurrentBalance` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "CurrentEquity", "`CurrentEquity` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "RiskProfileId", "`RiskProfileId` bigint NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "ExecutionMode", "`ExecutionMode` varchar(32) NOT NULL DEFAULT ''MarketFill''");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "UseAiScoring", "`UseAiScoring` tinyint(1) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "Speed", "`Speed` varchar(16) NOT NULL DEFAULT ''ManualStep''");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "CurrentFrameIndex", "`CurrentFrameIndex` int NOT NULL DEFAULT -1");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "CurrentCandleId", "`CurrentCandleId` bigint NULL");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "TotalFrames", "`TotalFrames` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "RequestedByUserId", "`RequestedByUserId` bigint NULL");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "ErrorMessage", "`ErrorMessage` varchar(4000) NULL");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "StartedAtUtc", "`StartedAtUtc` datetime(6) NULL");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "PausedAtUtc", "`PausedAtUtc` datetime(6) NULL");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "CompletedAtUtc", "`CompletedAtUtc` datetime(6) NULL");
            AddColumnIfMissing(migrationBuilder, "ReplaySessions", "ConfigJson", "`ConfigJson` longtext NULL");

            migrationBuilder.Sql(
                """
                SET @table_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'ReplayFrames');

                SET @create_table_sql := IF(
                    @table_exists = 0,
                    'CREATE TABLE `ReplayFrames` (
                        `Id` bigint NOT NULL AUTO_INCREMENT,
                        `ReplaySessionId` bigint NOT NULL,
                        `FrameIndex` int NOT NULL,
                        `CandleId` bigint NOT NULL,
                        `TimestampUtc` datetime(6) NOT NULL,
                        `MarketRegime` varchar(32) NOT NULL,
                        `StrategyResultsJson` longtext NOT NULL,
                        `AiDecisionId` bigint NULL,
                        `RiskDecisionId` bigint NULL,
                        `OrderId` bigint NULL,
                        `TradeId` bigint NULL,
                        `MissedOrderId` bigint NULL,
                        `Balance` decimal(28,12) NOT NULL,
                        `Equity` decimal(28,12) NOT NULL,
                        `Drawdown` decimal(28,12) NOT NULL,
                        `DrawdownPercent` decimal(28,12) NOT NULL,
                        `Explanation` varchar(8000) NOT NULL,
                        `CreatedAt` datetime(6) NOT NULL,
                        PRIMARY KEY (`Id`),
                        UNIQUE KEY `IX_ReplayFrames_ReplaySessionId_FrameIndex` (`ReplaySessionId`, `FrameIndex`),
                        CONSTRAINT `FK_ReplayFrames_ReplaySessions_ReplaySessionId`
                            FOREIGN KEY (`ReplaySessionId`) REFERENCES `ReplaySessions` (`Id`) ON DELETE RESTRICT
                    )',
                    'SELECT 1');

                PREPARE create_table_stmt FROM @create_table_sql;
                EXECUTE create_table_stmt;
                DEALLOCATE PREPARE create_table_stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `ReplayFrames`;");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string table, string column, string definition)
        {
            migrationBuilder.Sql(
                $"""
                 SET @column_exists := (
                     SELECT COUNT(*)
                     FROM information_schema.COLUMNS
                     WHERE TABLE_SCHEMA = DATABASE()
                       AND TABLE_NAME = '{table}'
                       AND COLUMN_NAME = '{column}');

                 SET @add_column_sql := IF(
                     @column_exists = 0,
                     'ALTER TABLE `{table}` ADD COLUMN {definition}',
                     'SELECT 1');

                 PREPARE add_column_stmt FROM @add_column_sql;
                 EXECUTE add_column_stmt;
                 DEALLOCATE PREPARE add_column_stmt;
                 """);
        }
    }
}
