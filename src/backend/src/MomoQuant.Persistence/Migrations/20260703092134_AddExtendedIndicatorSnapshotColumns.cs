using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedIndicatorSnapshotColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "BollingerMiddle20", "`BollingerMiddle20` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "BollingerUpper20", "`BollingerUpper20` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "BollingerLower20", "`BollingerLower20` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "BollingerBandwidth20", "`BollingerBandwidth20` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "DonchianHigh20", "`DonchianHigh20` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "DonchianLow20", "`DonchianLow20` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "MacdLine", "`MacdLine` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "MacdSignal", "`MacdSignal` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "MacdHistogram", "`MacdHistogram` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "Supertrend", "`Supertrend` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "SupertrendDirection", "`SupertrendDirection` int NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "SupportLevel", "`SupportLevel` decimal(28,12) NULL");
            AddColumnIfMissing(migrationBuilder, "IndicatorSnapshots", "ResistanceLevel", "`ResistanceLevel` decimal(28,12) NULL");

            migrationBuilder.Sql(
                """
                SET @index_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'AuditLogs'
                      AND INDEX_NAME = 'IX_AuditLogs_CreatedAt');

                SET @create_index_sql := IF(
                    @index_exists = 0,
                    'CREATE INDEX `IX_AuditLogs_CreatedAt` ON `AuditLogs` (`CreatedAt`)',
                    'SELECT 1');

                PREPARE create_index_stmt FROM @create_index_sql;
                EXECUTE create_index_stmt;
                DEALLOCATE PREPARE create_index_stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(name: "BollingerBandwidth20", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "BollingerLower20", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "BollingerMiddle20", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "BollingerUpper20", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "DonchianHigh20", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "DonchianLow20", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "MacdHistogram", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "MacdLine", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "MacdSignal", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "ResistanceLevel", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "Supertrend", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "SupertrendDirection", table: "IndicatorSnapshots");
            migrationBuilder.DropColumn(name: "SupportLevel", table: "IndicatorSnapshots");
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
