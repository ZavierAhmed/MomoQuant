using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAuditExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET @schema_name = DATABASE();

                SET @details_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND COLUMN_NAME = 'DetailsJson');
                SET @details_sql = IF(
                    @details_exists = 0,
                    'ALTER TABLE `SystemHealthLogs` ADD `DetailsJson` longtext CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE details_stmt FROM @details_sql;
                EXECUTE details_stmt;
                DEALLOCATE PREPARE details_stmt;

                SET @health_severity_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND COLUMN_NAME = 'Severity');
                SET @health_severity_sql = IF(
                    @health_severity_exists = 0,
                    'ALTER TABLE `SystemHealthLogs` ADD `Severity` varchar(32) CHARACTER SET utf8mb4 NOT NULL DEFAULT ''Info''',
                    'SELECT 1');
                PREPARE health_severity_stmt FROM @health_severity_sql;
                EXECUTE health_severity_stmt;
                DEALLOCATE PREPARE health_severity_stmt;

                SET @metadata_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND COLUMN_NAME = 'MetadataJson');
                SET @metadata_sql = IF(
                    @metadata_exists = 0,
                    'ALTER TABLE `AuditLogs` ADD `MetadataJson` longtext CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE metadata_stmt FROM @metadata_sql;
                EXECUTE metadata_stmt;
                DEALLOCATE PREPARE metadata_stmt;

                SET @audit_severity_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND COLUMN_NAME = 'Severity');
                SET @audit_severity_sql = IF(
                    @audit_severity_exists = 0,
                    'ALTER TABLE `AuditLogs` ADD `Severity` varchar(32) CHARACTER SET utf8mb4 NOT NULL DEFAULT ''Info''',
                    'SELECT 1');
                PREPARE audit_severity_stmt FROM @audit_severity_sql;
                EXECUTE audit_severity_stmt;
                DEALLOCATE PREPARE audit_severity_stmt;

                SET @user_email_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND COLUMN_NAME = 'UserEmail');
                SET @user_email_sql = IF(
                    @user_email_exists = 0,
                    'ALTER TABLE `AuditLogs` ADD `UserEmail` varchar(256) CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE user_email_stmt FROM @user_email_sql;
                EXECUTE user_email_stmt;
                DEALLOCATE PREPARE user_email_stmt;

                SET @health_created_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND INDEX_NAME = 'IX_SystemHealthLogs_CreatedAt');
                SET @health_created_index_sql = IF(
                    @health_created_index_exists = 0,
                    'CREATE INDEX `IX_SystemHealthLogs_CreatedAt` ON `SystemHealthLogs` (`CreatedAt`)',
                    'SELECT 1');
                PREPARE health_created_index_stmt FROM @health_created_index_sql;
                EXECUTE health_created_index_stmt;
                DEALLOCATE PREPARE health_created_index_stmt;

                SET @health_severity_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND INDEX_NAME = 'IX_SystemHealthLogs_Severity');
                SET @health_severity_index_sql = IF(
                    @health_severity_index_exists = 0,
                    'CREATE INDEX `IX_SystemHealthLogs_Severity` ON `SystemHealthLogs` (`Severity`)',
                    'SELECT 1');
                PREPARE health_severity_index_stmt FROM @health_severity_index_sql;
                EXECUTE health_severity_index_stmt;
                DEALLOCATE PREPARE health_severity_index_stmt;

                SET @audit_action_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND INDEX_NAME = 'IX_AuditLogs_Action');
                SET @audit_action_index_sql = IF(
                    @audit_action_index_exists = 0,
                    'CREATE INDEX `IX_AuditLogs_Action` ON `AuditLogs` (`Action`)',
                    'SELECT 1');
                PREPARE audit_action_index_stmt FROM @audit_action_index_sql;
                EXECUTE audit_action_index_stmt;
                DEALLOCATE PREPARE audit_action_index_stmt;

                SET @audit_user_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND INDEX_NAME = 'IX_AuditLogs_UserId');
                SET @audit_user_index_sql = IF(
                    @audit_user_index_exists = 0,
                    'CREATE INDEX `IX_AuditLogs_UserId` ON `AuditLogs` (`UserId`)',
                    'SELECT 1');
                PREPARE audit_user_index_stmt FROM @audit_user_index_sql;
                EXECUTE audit_user_index_stmt;
                DEALLOCATE PREPARE audit_user_index_stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET @schema_name = DATABASE();

                SET @audit_action_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND INDEX_NAME = 'IX_AuditLogs_Action');
                SET @audit_action_index_sql = IF(
                    @audit_action_index_exists > 0,
                    'DROP INDEX `IX_AuditLogs_Action` ON `AuditLogs`',
                    'SELECT 1');
                PREPARE audit_action_index_stmt FROM @audit_action_index_sql;
                EXECUTE audit_action_index_stmt;
                DEALLOCATE PREPARE audit_action_index_stmt;

                SET @audit_user_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND INDEX_NAME = 'IX_AuditLogs_UserId');
                SET @audit_user_index_sql = IF(
                    @audit_user_index_exists > 0,
                    'DROP INDEX `IX_AuditLogs_UserId` ON `AuditLogs`',
                    'SELECT 1');
                PREPARE audit_user_index_stmt FROM @audit_user_index_sql;
                EXECUTE audit_user_index_stmt;
                DEALLOCATE PREPARE audit_user_index_stmt;

                SET @health_created_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND INDEX_NAME = 'IX_SystemHealthLogs_CreatedAt');
                SET @health_created_index_sql = IF(
                    @health_created_index_exists > 0,
                    'DROP INDEX `IX_SystemHealthLogs_CreatedAt` ON `SystemHealthLogs`',
                    'SELECT 1');
                PREPARE health_created_index_stmt FROM @health_created_index_sql;
                EXECUTE health_created_index_stmt;
                DEALLOCATE PREPARE health_created_index_stmt;

                SET @health_severity_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND INDEX_NAME = 'IX_SystemHealthLogs_Severity');
                SET @health_severity_index_sql = IF(
                    @health_severity_index_exists > 0,
                    'DROP INDEX `IX_SystemHealthLogs_Severity` ON `SystemHealthLogs`',
                    'SELECT 1');
                PREPARE health_severity_index_stmt FROM @health_severity_index_sql;
                EXECUTE health_severity_index_stmt;
                DEALLOCATE PREPARE health_severity_index_stmt;

                SET @details_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND COLUMN_NAME = 'DetailsJson');
                SET @details_sql = IF(
                    @details_exists > 0,
                    'ALTER TABLE `SystemHealthLogs` DROP COLUMN `DetailsJson`',
                    'SELECT 1');
                PREPARE details_stmt FROM @details_sql;
                EXECUTE details_stmt;
                DEALLOCATE PREPARE details_stmt;

                SET @health_severity_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'SystemHealthLogs'
                      AND COLUMN_NAME = 'Severity');
                SET @health_severity_sql = IF(
                    @health_severity_exists > 0,
                    'ALTER TABLE `SystemHealthLogs` DROP COLUMN `Severity`',
                    'SELECT 1');
                PREPARE health_severity_stmt FROM @health_severity_sql;
                EXECUTE health_severity_stmt;
                DEALLOCATE PREPARE health_severity_stmt;

                SET @metadata_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND COLUMN_NAME = 'MetadataJson');
                SET @metadata_sql = IF(
                    @metadata_exists > 0,
                    'ALTER TABLE `AuditLogs` DROP COLUMN `MetadataJson`',
                    'SELECT 1');
                PREPARE metadata_stmt FROM @metadata_sql;
                EXECUTE metadata_stmt;
                DEALLOCATE PREPARE metadata_stmt;

                SET @audit_severity_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND COLUMN_NAME = 'Severity');
                SET @audit_severity_sql = IF(
                    @audit_severity_exists > 0,
                    'ALTER TABLE `AuditLogs` DROP COLUMN `Severity`',
                    'SELECT 1');
                PREPARE audit_severity_stmt FROM @audit_severity_sql;
                EXECUTE audit_severity_stmt;
                DEALLOCATE PREPARE audit_severity_stmt;

                SET @user_email_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema_name
                      AND TABLE_NAME = 'AuditLogs'
                      AND COLUMN_NAME = 'UserEmail');
                SET @user_email_sql = IF(
                    @user_email_exists > 0,
                    'ALTER TABLE `AuditLogs` DROP COLUMN `UserEmail`',
                    'SELECT 1');
                PREPARE user_email_stmt FROM @user_email_sql;
                EXECUTE user_email_stmt;
                DEALLOCATE PREPARE user_email_stmt;
                """);
        }
    }
}
