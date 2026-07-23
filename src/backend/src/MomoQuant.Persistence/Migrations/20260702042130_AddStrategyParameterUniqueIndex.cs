using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyParameterUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StrategyParameters_Strategies_StrategyId",
                table: "StrategyParameters");

            migrationBuilder.DropIndex(
                name: "IX_StrategyParameters_StrategyId",
                table: "StrategyParameters");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameters_StrategyId_ParameterKey_Timeframe_SymbolId",
                table: "StrategyParameters",
                columns: new[] { "StrategyId", "ParameterKey", "Timeframe", "SymbolId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StrategyParameters_Strategies_StrategyId",
                table: "StrategyParameters",
                column: "StrategyId",
                principalTable: "Strategies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StrategyParameters_Strategies_StrategyId",
                table: "StrategyParameters");

            migrationBuilder.DropIndex(
                name: "IX_StrategyParameters_StrategyId_ParameterKey_Timeframe_SymbolId",
                table: "StrategyParameters");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameters_StrategyId",
                table: "StrategyParameters",
                column: "StrategyId");

            migrationBuilder.AddForeignKey(
                name: "FK_StrategyParameters_Strategies_StrategyId",
                table: "StrategyParameters",
                column: "StrategyId",
                principalTable: "Strategies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
