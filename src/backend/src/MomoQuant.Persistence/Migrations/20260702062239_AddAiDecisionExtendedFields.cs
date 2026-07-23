using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDecisionExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE `AiDecisions`
                MODIFY COLUMN `TradingSessionId` bigint NULL;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'AnomalySeverity');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `AnomalySeverity` varchar(32) NULL',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'ConfidenceClassification');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `ConfidenceClassification` varchar(32) NULL',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'IsAnomalous');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `IsAnomalous` tinyint(1) NOT NULL DEFAULT 0',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'ReasonsJson');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `ReasonsJson` longtext NULL',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'RegimeConfidence');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `RegimeConfidence` decimal(28,12) NULL',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'Summary');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `Summary` varchar(2000) NULL',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'WarningsJson');

                SET @add_column_sql := IF(
                    @column_exists = 0,
                    'ALTER TABLE `AiDecisions` ADD COLUMN `WarningsJson` longtext NULL',
                    'SELECT 1');

                PREPARE add_column_stmt FROM @add_column_sql;
                EXECUTE add_column_stmt;
                DEALLOCATE PREPARE add_column_stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'WarningsJson');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `WarningsJson`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'Summary');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `Summary`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'RegimeConfidence');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `RegimeConfidence`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'ReasonsJson');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `ReasonsJson`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'IsAnomalous');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `IsAnomalous`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'ConfidenceClassification');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `ConfidenceClassification`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AiDecisions'
                      AND COLUMN_NAME = 'AnomalySeverity');

                SET @drop_column_sql := IF(
                    @column_exists > 0,
                    'ALTER TABLE `AiDecisions` DROP COLUMN `AnomalySeverity`',
                    'SELECT 1');

                PREPARE drop_column_stmt FROM @drop_column_sql;
                EXECUTE drop_column_stmt;
                DEALLOCATE PREPARE drop_column_stmt;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE `AiDecisions`
                MODIFY COLUMN `TradingSessionId` bigint NOT NULL;
                """);
        }
    }
}
