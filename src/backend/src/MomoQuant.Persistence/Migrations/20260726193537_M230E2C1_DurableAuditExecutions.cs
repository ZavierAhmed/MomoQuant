using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M230E2C1_DurableAuditExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuditAttemptNumber",
                table: "ValidationParameterTrials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AuditCompletionStatus",
                table: "ValidationParameterTrials",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotEvaluated")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "AuthoritativeAuditExecutionId",
                table: "ValidationParameterTrials",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            // Executions BEFORE batches so the principal unique key exists for the FK.
            migrationBuilder.CreateTable(
                name: "ValidationAuditExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AuditExecutionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ValidationExperimentId = table.Column<long>(type: "bigint", nullable: false),
                    ValidationTrialId = table.Column<long>(type: "bigint", nullable: false),
                    TrialNumber = table.Column<int>(type: "int", nullable: false),
                    ScopeExecutionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    ExecutionToken = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutionType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SupersededByAuditExecutionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    FailureCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecoveryStatus = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FinalExpectedSequence = table.Column<long>(type: "bigint", nullable: true),
                    LastConfirmedSequence = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedEventCount = table.Column<int>(type: "int", nullable: true),
                    ConfirmedEventCount = table.Column<int>(type: "int", nullable: false),
                    FinalPayloadSetHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuditContractVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllowsZeroAccess = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RowVersion = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationAuditExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationAuditExecutions_ValidationExperiments_ValidationEx~",
                        column: x => x.ValidationExperimentId,
                        principalTable: "ValidationExperiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValidationAuditExecutions_ValidationParameterTrials_Validati~",
                        column: x => x.ValidationTrialId,
                        principalTable: "ValidationParameterTrials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditExec_AuditExecutionId",
                table: "ValidationAuditExecutions",
                column: "AuditExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditExec_Experiment_Status",
                table: "ValidationAuditExecutions",
                columns: new[] { "ValidationExperimentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditExec_ExperimentId",
                table: "ValidationAuditExecutions",
                column: "ValidationExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditExec_ScopeExecutionId",
                table: "ValidationAuditExecutions",
                column: "ScopeExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditExec_Trial_Attempt",
                table: "ValidationAuditExecutions",
                columns: new[] { "ValidationTrialId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditExec_TrialId",
                table: "ValidationAuditExecutions",
                column: "ValidationTrialId");

            migrationBuilder.CreateTable(
                name: "ValidationAuditBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AuditBatchId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuditExecutionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BatchNumber = table.Column<int>(type: "int", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedEventCount = table.Column<int>(type: "int", nullable: false),
                    ExpectedEventIdsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedPayloadHashesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedPayloadSetHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PersistenceAttemptCount = table.Column<int>(type: "int", nullable: false),
                    ConfirmationAttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuditBatchContractVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowVersion = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationAuditBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationAuditBatches_ValidationAuditExecutions_AuditExecut~",
                        column: x => x.AuditExecutionId,
                        principalTable: "ValidationAuditExecutions",
                        principalColumn: "AuditExecutionId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ValTrials_AuthoritativeAuditExecutionId",
                table: "ValidationParameterTrials",
                column: "AuthoritativeAuditExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditBatch_AuditBatchId",
                table: "ValidationAuditBatches",
                column: "AuditBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditBatch_AuditExecutionId",
                table: "ValidationAuditBatches",
                column: "AuditExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditBatch_Exec_BatchNumber",
                table: "ValidationAuditBatches",
                columns: new[] { "AuditExecutionId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValAuditBatch_Exec_SeqRange",
                table: "ValidationAuditBatches",
                columns: new[] { "AuditExecutionId", "FirstSequence", "LastSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValidationAuditBatches");

            migrationBuilder.DropTable(
                name: "ValidationAuditExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ValTrials_AuthoritativeAuditExecutionId",
                table: "ValidationParameterTrials");

            migrationBuilder.DropColumn(
                name: "AuditAttemptNumber",
                table: "ValidationParameterTrials");

            migrationBuilder.DropColumn(
                name: "AuditCompletionStatus",
                table: "ValidationParameterTrials");

            migrationBuilder.DropColumn(
                name: "AuthoritativeAuditExecutionId",
                table: "ValidationParameterTrials");
        }
    }
}
