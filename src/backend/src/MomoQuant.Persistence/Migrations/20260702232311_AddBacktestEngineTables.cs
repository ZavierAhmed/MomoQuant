using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktestEngineTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "ConfigJson", "`ConfigJson` longtext NOT NULL");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "ErrorMessage", "`ErrorMessage` varchar(4000) NULL");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "ExchangeId", "`ExchangeId` bigint NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "ExecutionMode", "`ExecutionMode` varchar(32) NOT NULL DEFAULT 'MarketFill'");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "RequestedByUserId", "`RequestedByUserId` bigint NULL");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "RiskProfileId", "`RiskProfileId` bigint NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "UpdatedAt", "`UpdatedAt` datetime(6) NULL");
            AddColumnIfMissing(migrationBuilder, "BacktestRuns", "UseAiScoring", "`UseAiScoring` tinyint(1) NOT NULL DEFAULT 0");

            AddColumnIfMissing(migrationBuilder, "BacktestResults", "ApprovedSignals", "`ApprovedSignals` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "AverageRewardRisk", "`AverageRewardRisk` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "BreakEvenTrades", "`BreakEvenTrades` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "CancelledOrders", "`CancelledOrders` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "FilledOrders", "`FilledOrders` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "FinalBalance", "`FinalBalance` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "GrossLoss", "`GrossLoss` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "GrossProfit", "`GrossProfit` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "InitialBalance", "`InitialBalance` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "MaxDrawdownPercent", "`MaxDrawdownPercent` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "MissedOrders", "`MissedOrders` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "NetPnlPercent", "`NetPnlPercent` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "RejectedSignals", "`RejectedSignals` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "TotalSignals", "`TotalSignals` int NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "TotalSlippage", "`TotalSlippage` decimal(28,12) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "BacktestResults", "WinRatePercent", "`WinRatePercent` decimal(28,12) NOT NULL DEFAULT 0");

            migrationBuilder.Sql(
                """
                SET @table_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'BacktestEquityPoints');

                SET @create_table_sql := IF(
                    @table_exists = 0,
                    'CREATE TABLE `BacktestEquityPoints` (
                        `Id` bigint NOT NULL AUTO_INCREMENT,
                        `BacktestRunId` bigint NOT NULL,
                        `TimestampUtc` datetime(6) NOT NULL,
                        `Balance` decimal(28,12) NOT NULL,
                        `Equity` decimal(28,12) NOT NULL,
                        `Drawdown` decimal(28,12) NOT NULL,
                        `DrawdownPercent` decimal(28,12) NOT NULL,
                        `OpenPositionCount` int NOT NULL,
                        `CreatedAt` datetime(6) NOT NULL,
                        PRIMARY KEY (`Id`),
                        KEY `IX_BacktestEquityPoints_BacktestRunId_TimestampUtc` (`BacktestRunId`, `TimestampUtc`),
                        CONSTRAINT `FK_BacktestEquityPoints_BacktestRuns_BacktestRunId`
                            FOREIGN KEY (`BacktestRunId`) REFERENCES `BacktestRuns` (`Id`) ON DELETE RESTRICT
                    )',
                    'SELECT 1');

                PREPARE create_table_stmt FROM @create_table_sql;
                EXECUTE create_table_stmt;
                DEALLOCATE PREPARE create_table_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @table_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'BacktestStrategyResults');

                SET @create_table_sql := IF(
                    @table_exists = 0,
                    'CREATE TABLE `BacktestStrategyResults` (
                        `Id` bigint NOT NULL AUTO_INCREMENT,
                        `BacktestRunId` bigint NOT NULL,
                        `StrategyCode` varchar(64) NOT NULL,
                        `TotalSignals` int NOT NULL,
                        `ApprovedSignals` int NOT NULL,
                        `RejectedSignals` int NOT NULL,
                        `TotalTrades` int NOT NULL,
                        `WinningTrades` int NOT NULL,
                        `LosingTrades` int NOT NULL,
                        `NetPnl` decimal(28,12) NOT NULL,
                        `WinRatePercent` decimal(28,12) NOT NULL,
                        `ProfitFactor` decimal(28,12) NOT NULL,
                        `MaxDrawdownPercent` decimal(28,12) NOT NULL,
                        `AverageConfidenceScore` decimal(28,12) NOT NULL,
                        `CreatedAt` datetime(6) NOT NULL,
                        PRIMARY KEY (`Id`),
                        UNIQUE KEY `IX_BacktestStrategyResults_BacktestRunId_StrategyCode` (`BacktestRunId`, `StrategyCode`),
                        CONSTRAINT `FK_BacktestStrategyResults_BacktestRuns_BacktestRunId`
                            FOREIGN KEY (`BacktestRunId`) REFERENCES `BacktestRuns` (`Id`) ON DELETE RESTRICT
                    )',
                    'SELECT 1');

                PREPARE create_table_stmt FROM @create_table_sql;
                EXECUTE create_table_stmt;
                DEALLOCATE PREPARE create_table_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @table_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'BacktestSymbolResults');

                SET @create_table_sql := IF(
                    @table_exists = 0,
                    'CREATE TABLE `BacktestSymbolResults` (
                        `Id` bigint NOT NULL AUTO_INCREMENT,
                        `BacktestRunId` bigint NOT NULL,
                        `SymbolId` bigint NOT NULL,
                        `Symbol` varchar(64) NOT NULL,
                        `Timeframe` varchar(16) NOT NULL,
                        `TotalTrades` int NOT NULL,
                        `WinningTrades` int NOT NULL,
                        `LosingTrades` int NOT NULL,
                        `NetPnl` decimal(28,12) NOT NULL,
                        `WinRatePercent` decimal(28,12) NOT NULL,
                        `ProfitFactor` decimal(28,12) NOT NULL,
                        `MaxDrawdownPercent` decimal(28,12) NOT NULL,
                        `TotalFees` decimal(28,12) NOT NULL,
                        `MissedOrders` int NOT NULL,
                        `CreatedAt` datetime(6) NOT NULL,
                        PRIMARY KEY (`Id`),
                        UNIQUE KEY `IX_BacktestSymbolResults_BacktestRunId_SymbolId_Timeframe` (`BacktestRunId`, `SymbolId`, `Timeframe`),
                        KEY `IX_BacktestSymbolResults_SymbolId` (`SymbolId`),
                        CONSTRAINT `FK_BacktestSymbolResults_BacktestRuns_BacktestRunId`
                            FOREIGN KEY (`BacktestRunId`) REFERENCES `BacktestRuns` (`Id`) ON DELETE RESTRICT,
                        CONSTRAINT `FK_BacktestSymbolResults_Symbols_SymbolId`
                            FOREIGN KEY (`SymbolId`) REFERENCES `Symbols` (`Id`) ON DELETE RESTRICT
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
            migrationBuilder.Sql("DROP TABLE IF EXISTS `BacktestEquityPoints`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `BacktestStrategyResults`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `BacktestSymbolResults`;");
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
