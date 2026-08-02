using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GatePaperDeploymentSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BoundStrategyId",
                table: "PaperTradingSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BoundSymbolId",
                table: "PaperTradingSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundTimeframe",
                table: "PaperTradingSessions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "ParameterSetId",
                table: "PaperTradingSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationEvidenceVersion",
                table: "PaperTradingSessions",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QualificationParameterFingerprint",
                table: "PaperTradingSessions",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "QualificationSourceExperimentId",
                table: "PaperTradingSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QualificationSourceTrialId",
                table: "PaperTradingSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QualificationVerifiedAtUtc",
                table: "PaperTradingSessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UseClass",
                table: "PaperTradingSessions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Research")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_BoundStrategyId",
                table: "PaperTradingSessions",
                column: "BoundStrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_BoundSymbolId",
                table: "PaperTradingSessions",
                column: "BoundSymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_ParameterSetId",
                table: "PaperTradingSessions",
                column: "ParameterSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_QualificationSourceExperimentId",
                table: "PaperTradingSessions",
                column: "QualificationSourceExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_QualificationSourceTrialId",
                table: "PaperTradingSessions",
                column: "QualificationSourceTrialId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingSessions_UseClass_Status",
                table: "PaperTradingSessions",
                columns: new[] { "UseClass", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaperTradingSessions_UseClassBinding",
                table: "PaperTradingSessions",
                sql: "(`UseClass` = 'Research' AND `BoundStrategyId` IS NULL AND `BoundSymbolId` IS NULL AND `BoundTimeframe` IS NULL AND `QualificationSourceExperimentId` IS NULL AND `QualificationSourceTrialId` IS NULL AND `QualificationParameterFingerprint` IS NULL AND `QualificationEvidenceVersion` IS NULL AND `QualificationVerifiedAtUtc` IS NULL) OR (`UseClass` = 'DeploymentSimulation' AND `Mode` = 'LivePaper' AND `ParameterSetId` IS NOT NULL AND `BoundStrategyId` IS NOT NULL AND `BoundSymbolId` IS NOT NULL AND `BoundTimeframe` IS NOT NULL AND CHAR_LENGTH(`BoundTimeframe`) > 0 AND `QualificationSourceExperimentId` IS NOT NULL AND `QualificationSourceTrialId` IS NOT NULL AND `QualificationParameterFingerprint` IS NOT NULL AND CHAR_LENGTH(`QualificationParameterFingerprint`) > 0 AND `QualificationEvidenceVersion` IS NOT NULL AND CHAR_LENGTH(`QualificationEvidenceVersion`) > 0 AND `QualificationVerifiedAtUtc` IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_PaperTradingSessions_Strategies_BoundStrategyId",
                table: "PaperTradingSessions",
                column: "BoundStrategyId",
                principalTable: "Strategies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperTradingSessions_StrategyParameterSets_ParameterSetId",
                table: "PaperTradingSessions",
                column: "ParameterSetId",
                principalTable: "StrategyParameterSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperTradingSessions_Symbols_BoundSymbolId",
                table: "PaperTradingSessions",
                column: "BoundSymbolId",
                principalTable: "Symbols",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperTradingSessions_ValidationExperiments_QualificationSour~",
                table: "PaperTradingSessions",
                column: "QualificationSourceExperimentId",
                principalTable: "ValidationExperiments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperTradingSessions_ValidationParameterTrials_Qualification~",
                table: "PaperTradingSessions",
                column: "QualificationSourceTrialId",
                principalTable: "ValidationParameterTrials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperTradingSessions_Strategies_BoundStrategyId",
                table: "PaperTradingSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperTradingSessions_StrategyParameterSets_ParameterSetId",
                table: "PaperTradingSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperTradingSessions_Symbols_BoundSymbolId",
                table: "PaperTradingSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperTradingSessions_ValidationExperiments_QualificationSour~",
                table: "PaperTradingSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperTradingSessions_ValidationParameterTrials_Qualification~",
                table: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingSessions_BoundStrategyId",
                table: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingSessions_BoundSymbolId",
                table: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingSessions_ParameterSetId",
                table: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingSessions_QualificationSourceExperimentId",
                table: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingSessions_QualificationSourceTrialId",
                table: "PaperTradingSessions");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingSessions_UseClass_Status",
                table: "PaperTradingSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaperTradingSessions_UseClassBinding",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "BoundStrategyId",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "BoundSymbolId",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "BoundTimeframe",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "ParameterSetId",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "QualificationEvidenceVersion",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "QualificationParameterFingerprint",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "QualificationSourceExperimentId",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "QualificationSourceTrialId",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "QualificationVerifiedAtUtc",
                table: "PaperTradingSessions");

            migrationBuilder.DropColumn(
                name: "UseClass",
                table: "PaperTradingSessions");
        }
    }
}
