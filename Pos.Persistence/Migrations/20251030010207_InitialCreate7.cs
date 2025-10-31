using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessNtn",
                table: "InvoiceSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableFbr",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FbrApiBaseUrl",
                table: "InvoiceSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FbrAuthKey",
                table: "InvoiceSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FbrPosId",
                table: "InvoiceSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RowShowLineDiscount",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RowShowLineTotal",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RowShowProductName",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RowShowProductSku",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RowShowQty",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RowShowUnitPrice",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowAddress",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowBusinessName",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowBusinessNtn",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowContacts",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowFbrQr",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowFooter",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowLogo",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TotalsShowBalance",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TotalsShowDiscounts",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TotalsShowGrandTotal",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TotalsShowOtherExpenses",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TotalsShowPaymentRecv",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TotalsShowTaxes",
                table: "InvoiceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessNtn",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "EnableFbr",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "FbrApiBaseUrl",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "FbrAuthKey",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "FbrPosId",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowShowLineDiscount",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowShowLineTotal",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowShowProductName",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowShowProductSku",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowShowQty",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowShowUnitPrice",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowAddress",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowBusinessName",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowBusinessNtn",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowContacts",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowFbrQr",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowFooter",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowLogo",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TotalsShowBalance",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TotalsShowDiscounts",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TotalsShowGrandTotal",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TotalsShowOtherExpenses",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TotalsShowPaymentRecv",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TotalsShowTaxes",
                table: "InvoiceSettings");
        }
    }
}
