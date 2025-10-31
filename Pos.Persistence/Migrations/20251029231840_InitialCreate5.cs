using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "NameXmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "NameYmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PriceXmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PriceYmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SkuXmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SkuYmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameXmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "NameYmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "PriceXmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "PriceYmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "SkuXmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "SkuYmm",
                table: "BarcodeLabelSettings");
        }
    }
}
