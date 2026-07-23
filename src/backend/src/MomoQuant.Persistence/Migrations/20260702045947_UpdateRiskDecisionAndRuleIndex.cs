using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRiskDecisionAndRuleIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE rr
                FROM RiskRules rr
                INNER JOIN (
                    SELECT RiskProfileId, RuleKey, MIN(Id) AS KeepId
                    FROM RiskRules
                    GROUP BY RiskProfileId, RuleKey
                    HAVING COUNT(*) > 1
                ) dup ON rr.RiskProfileId = dup.RiskProfileId
                    AND rr.RuleKey = dup.RuleKey
                    AND rr.Id <> dup.KeepId;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE `RiskDecisions`
                MODIFY COLUMN `TradingSessionId` bigint NULL;
                """);

            migrationBuilder.Sql(
                """
                SET @fk_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.TABLE_CONSTRAINTS
                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'RiskRules'
                      AND CONSTRAINT_NAME = 'FK_RiskRules_RiskProfiles_RiskProfileId');

                SET @drop_fk_sql := IF(
                    @fk_exists > 0,
                    'ALTER TABLE `RiskRules` DROP FOREIGN KEY `FK_RiskRules_RiskProfiles_RiskProfileId`',
                    'SELECT 1');

                PREPARE drop_fk_stmt FROM @drop_fk_sql;
                EXECUTE drop_fk_stmt;
                DEALLOCATE PREPARE drop_fk_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @index_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.statistics
                    WHERE table_schema = DATABASE()
                      AND table_name = 'RiskRules'
                      AND index_name = 'IX_RiskRules_RiskProfileId');

                SET @drop_index_sql := IF(
                    @index_exists > 0,
                    'ALTER TABLE `RiskRules` DROP INDEX `IX_RiskRules_RiskProfileId`',
                    'SELECT 1');

                PREPARE drop_index_stmt FROM @drop_index_sql;
                EXECUTE drop_index_stmt;
                DEALLOCATE PREPARE drop_index_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @unique_index_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.statistics
                    WHERE table_schema = DATABASE()
                      AND table_name = 'RiskRules'
                      AND index_name = 'IX_RiskRules_RiskProfileId_RuleKey');

                SET @create_index_sql := IF(
                    @unique_index_exists = 0,
                    'CREATE UNIQUE INDEX `IX_RiskRules_RiskProfileId_RuleKey` ON `RiskRules` (`RiskProfileId`, `RuleKey`)',
                    'SELECT 1');

                PREPARE create_index_stmt FROM @create_index_sql;
                EXECUTE create_index_stmt;
                DEALLOCATE PREPARE create_index_stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @fk_exists := (
                    SELECT COUNT(*)
                    FROM information_schema.TABLE_CONSTRAINTS
                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'RiskRules'
                      AND CONSTRAINT_NAME = 'FK_RiskRules_RiskProfiles_RiskProfileId');

                SET @add_fk_sql := IF(
                    @fk_exists = 0,
                    'ALTER TABLE `RiskRules` ADD CONSTRAINT `FK_RiskRules_RiskProfiles_RiskProfileId` FOREIGN KEY (`RiskProfileId`) REFERENCES `RiskProfiles` (`Id`) ON DELETE RESTRICT',
                    'SELECT 1');

                PREPARE add_fk_stmt FROM @add_fk_sql;
                EXECUTE add_fk_stmt;
                DEALLOCATE PREPARE add_fk_stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiskRules_RiskProfiles_RiskProfileId",
                table: "RiskRules");

            migrationBuilder.DropIndex(
                name: "IX_RiskRules_RiskProfileId_RuleKey",
                table: "RiskRules");

            migrationBuilder.AlterColumn<long>(
                name: "TradingSessionId",
                table: "RiskDecisions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskRules_RiskProfileId",
                table: "RiskRules",
                column: "RiskProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_RiskRules_RiskProfiles_RiskProfileId",
                table: "RiskRules",
                column: "RiskProfileId",
                principalTable: "RiskProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
