using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M230E2B_AccessPayloadVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessPayloadContractVersion",
                table: "ValidationCandleAccessAudits",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AccessPayloadHash",
                table: "ValidationCandleAccessAudits",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessPayloadContractVersion",
                table: "ValidationCandleAccessAudits");

            migrationBuilder.DropColumn(
                name: "AccessPayloadHash",
                table: "ValidationCandleAccessAudits");
        }
    }
}
