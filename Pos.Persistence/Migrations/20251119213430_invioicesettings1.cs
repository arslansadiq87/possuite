using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettings1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultBarcodeType",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DisplayTimeZoneId",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelPrinterName",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseBankAccountId",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesCardClearingAccountId",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultBarcodeType",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "DisplayTimeZoneId",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "LabelPrinterName",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "PurchaseBankAccountId",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "SalesCardClearingAccountId",
                table: "InvoiceSettingsLocals");
        }
    }
}
