using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MomoQuant.Persistence;

#nullable disable

namespace MomoQuant.Persistence.Migrations;

/// <summary>
/// Milestone 23.0E1: persist StrategyLab candle-load contract version on StrategyLabRuns.
/// </summary>
[DbContext(typeof(MomoQuantDbContext))]
[Migration("20260724120200_M230E1_StrategyLabCandleLoadContract")]
public partial class M230E1_StrategyLabCandleLoadContract : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CandleLoadContractVersion",
            table: "StrategyLabRuns",
            type: "varchar(100)",
            maxLength: 100,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CandleLoadContractVersion",
            table: "StrategyLabRuns");
    }
}
