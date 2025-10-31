using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AskToPrintOnSave",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LogoAlignment",
                table: "InvoiceSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogoMaxWidthPx",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoPng",
                table: "InvoiceSettings",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrintBarcodeOnReceipt",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrintOnSave",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BarcodeLabelSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LabelWidthMm = table.Column<int>(type: "INTEGER", nullable: false),
                    LabelHeightMm = table.Column<int>(type: "INTEGER", nullable: false),
                    HorizontalGapMm = table.Column<int>(type: "INTEGER", nullable: false),
                    VerticalGapMm = table.Column<int>(type: "INTEGER", nullable: false),
                    MarginLeftMm = table.Column<int>(type: "INTEGER", nullable: false),
                    MarginTopMm = table.Column<int>(type: "INTEGER", nullable: false),
                    Columns = table.Column<int>(type: "INTEGER", nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    CodeType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ShowName = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowPrice = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowSku = table.Column<bool>(type: "INTEGER", nullable: false),
                    FontSizePt = table.Column<int>(type: "INTEGER", nullable: false),
                    Dpi = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarcodeLabelSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeLabelSettings_OutletId",
                table: "BarcodeLabelSettings",
                column: "OutletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BarcodeLabelSettings");

            migrationBuilder.DropColumn(
                name: "AskToPrintOnSave",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "LogoAlignment",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "LogoMaxWidthPx",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "LogoPng",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "PrintBarcodeOnReceipt",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "PrintOnSave",
                table: "InvoiceSettings");
        }
    }
}
