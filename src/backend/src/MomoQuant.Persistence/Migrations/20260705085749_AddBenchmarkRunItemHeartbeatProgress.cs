using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkRunItemHeartbeatProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastProcessedCandleIndex",
                table: "StrategyBenchmarkRunItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastProcessedCandleTimeUtc",
                table: "StrategyBenchmarkRunItems",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCandles",
                table: "StrategyBenchmarkRunItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastProcessedCandleIndex",
                table: "StrategyBenchmarkRunItems");

            migrationBuilder.DropColumn(
                name: "LastProcessedCandleTimeUtc",
                table: "StrategyBenchmarkRunItems");

            migrationBuilder.DropColumn(
                name: "TotalCandles",
                table: "StrategyBenchmarkRunItems");
        }
    }
}
