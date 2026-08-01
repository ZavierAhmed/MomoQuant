using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublishQualifiedValidationLabParameterSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualificationEvidenceVersion",
                table: "StrategyParameterSets",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QualificationParameterFingerprint",
                table: "StrategyParameterSets",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "QualificationSourceExperimentId",
                table: "StrategyParameterSets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QualificationSourceTrialId",
                table: "StrategyParameterSets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QualifiedAtUtc",
                table: "StrategyParameterSets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameterSets_QualificationSourceExperimentId",
                table: "StrategyParameterSets",
                column: "QualificationSourceExperimentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameterSets_QualificationSourceTrialId",
                table: "StrategyParameterSets",
                column: "QualificationSourceTrialId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StrategyParameterSets_DeploymentQualificationProvenance",
                table: "StrategyParameterSets",
                sql: "`QualificationStatus` <> 'DeploymentQualified' OR (`Source` = 'ValidationLab' AND `IsApproved` = 1 AND `QualificationSourceExperimentId` IS NOT NULL AND `QualificationSourceTrialId` IS NOT NULL AND `QualificationParameterFingerprint` IS NOT NULL AND CHAR_LENGTH(`QualificationParameterFingerprint`) > 0 AND `QualificationEvidenceVersion` IS NOT NULL AND CHAR_LENGTH(`QualificationEvidenceVersion`) > 0 AND `QualifiedAtUtc` IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_StrategyParameterSets_ValidationExperiments_QualificationSou~",
                table: "StrategyParameterSets",
                column: "QualificationSourceExperimentId",
                principalTable: "ValidationExperiments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StrategyParameterSets_ValidationParameterTrials_Qualificatio~",
                table: "StrategyParameterSets",
                column: "QualificationSourceTrialId",
                principalTable: "ValidationParameterTrials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StrategyParameterSets_ValidationExperiments_QualificationSou~",
                table: "StrategyParameterSets");

            migrationBuilder.DropForeignKey(
                name: "FK_StrategyParameterSets_ValidationParameterTrials_Qualificatio~",
                table: "StrategyParameterSets");

            migrationBuilder.DropIndex(
                name: "IX_StrategyParameterSets_QualificationSourceExperimentId",
                table: "StrategyParameterSets");

            migrationBuilder.DropIndex(
                name: "IX_StrategyParameterSets_QualificationSourceTrialId",
                table: "StrategyParameterSets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StrategyParameterSets_DeploymentQualificationProvenance",
                table: "StrategyParameterSets");

            migrationBuilder.DropColumn(
                name: "QualificationEvidenceVersion",
                table: "StrategyParameterSets");

            migrationBuilder.DropColumn(
                name: "QualificationParameterFingerprint",
                table: "StrategyParameterSets");

            migrationBuilder.DropColumn(
                name: "QualificationSourceExperimentId",
                table: "StrategyParameterSets");

            migrationBuilder.DropColumn(
                name: "QualificationSourceTrialId",
                table: "StrategyParameterSets");

            migrationBuilder.DropColumn(
                name: "QualifiedAtUtc",
                table: "StrategyParameterSets");
        }
    }
}
