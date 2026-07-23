using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <summary>
    /// Milestone 23.0C Parts 16–18: EF model snapshot reconciliation.
    /// Physical schema already exists via prior hand-written migrations.
    /// This migration updates Designer + ModelSnapshot only; Up/Down are intentionally empty.
    ///
    /// Removed generated Up operations (already applied by historical migrations):
    /// - AddColumn Strategies: CanonicalValidationExperimentId, DeploymentQualificationEligible,
    ///   ResearchDecisionAtUtc, ResearchDecisionJson, ResearchStatus
    ///   (from 20260721100000_AddValidationLab223Closeout)
    /// - CreateTable + indexes: ExportJobs (20260708120000_AddExportJobs)
    /// - CreateTable + indexes: ParameterOptimizationRuns, StrategyParameterSets
    ///   (20260708120000_AddStrategyParameterSetsAndOptimization)
    /// - CreateTable + indexes: ResearchOperationStatuses (20260723184500_AddResearchOperationStatuses)
    /// - CreateTable + indexes: SkLivePaper* (20260707120000_AddSkLivePaperTables)
    /// - CreateTable + indexes: StrategyLabRuns, StrategyResearchCandidates,
    ///   StrategyResearchCandidatePortfolioAssessments (20260714120000_AddStrategyLabTables + follow-ons)
    /// - CreateTable + indexes: TargetOptimizationRuns (20260714120000_AddTargetOptimizationRuns)
    /// - CreateTable + indexes: ValidationExperiments/Trials/Segments/Leases
    ///   (20260717100000_AddValidationLaboratoryTables + follow-ons)
    /// - CreateTable + indexes: ValidationCandleAccessAudits including AccessEventId columns/indexes
    ///   (20260723120000_AddValidationCandleAccessAudits + 20260723200000_AddValidationCandleAccessEventId)
    /// Retained: none (no genuine safe schema drift identified beyond already-applied DDL).
    /// </summary>
    public partial class M230C_ReconcileModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: schema already applied by hand-written migrations.
            // Designer + MomoQuantDbContextModelSnapshot capture the reconciled model.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: pair of empty Up for snapshot-only reconciliation.
        }
    }
}
