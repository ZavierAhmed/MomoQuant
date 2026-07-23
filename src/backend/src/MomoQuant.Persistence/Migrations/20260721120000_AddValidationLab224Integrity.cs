using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260721120000_AddValidationLab224Integrity")]
public partial class AddValidationLab224Integrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SelectedTrialNumber",
            table: "ValidationExperiments",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SelectedTrialParameterSnapshotJson",
            table: "ValidationExperiments",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SelectedTrialParameterFingerprint",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FrozenSnapshotValidationStatus",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "NotEvaluated");

        migrationBuilder.AddColumn<string>(
            name: "SelectionIntegrityVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationSelectionIntegrity/v1");

        migrationBuilder.AddColumn<string>(
            name: "RiskBasisVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationRiskBasis/v1");

        migrationBuilder.AddColumn<string>(
            name: "ParameterFingerprintVersion",
            table: "ValidationExperiments",
            type: "varchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "ValidationParameterFingerprint/v1");

        migrationBuilder.AddColumn<string>(
            name: "FreezeSource",
            table: "ValidationExperiments",
            type: "varchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "AllowInfrastructureOnlyRejectedTrialFallback",
            table: "ValidationExperiments",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "FallbackSelectionPolicyVersion",
            table: "ValidationExperiments",
            type: "varchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FallbackSelectionReason",
            table: "ValidationExperiments",
            type: "varchar(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsQualificationCapable",
            table: "ValidationExperiments",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SelectedTrialNumber", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SelectedTrialParameterSnapshotJson", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SelectedTrialParameterFingerprint", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "FrozenSnapshotValidationStatus", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "SelectionIntegrityVersion", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "RiskBasisVersion", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "ParameterFingerprintVersion", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "FreezeSource", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "AllowInfrastructureOnlyRejectedTrialFallback", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "FallbackSelectionPolicyVersion", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "FallbackSelectionReason", table: "ValidationExperiments");
        migrationBuilder.DropColumn(name: "IsQualificationCapable", table: "ValidationExperiments");
    }
}
