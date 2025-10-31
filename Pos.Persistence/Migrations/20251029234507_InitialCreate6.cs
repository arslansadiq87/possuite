using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BarcodeHeightMm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BarcodeMarginBottomMm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BarcodeMarginLeftMm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BarcodeMarginRightMm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BarcodeMarginTopMm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "BusinessName",
                table: "BarcodeLabelSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BusinessXmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BusinessYmm",
                table: "BarcodeLabelSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "ShowBusinessName",
                table: "BarcodeLabelSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarcodeHeightMm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginBottomMm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginLeftMm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginRightMm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginTopMm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BusinessName",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BusinessXmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "BusinessYmm",
                table: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "ShowBusinessName",
                table: "BarcodeLabelSettings");
        }
    }
}
