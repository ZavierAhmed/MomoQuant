using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterSetQualificationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualificationStatus",
                table: "StrategyParameterSets",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "HistoricalNotEvaluated")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QualificationStatus",
                table: "StrategyParameterSets");
        }
    }
}
