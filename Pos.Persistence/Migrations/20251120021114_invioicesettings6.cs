using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettings6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "FooterText",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "HeaderText",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "LogoPng",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "OutletDisplayName",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "PrinterName",
                table: "ReceiptTemplates");

            migrationBuilder.AddColumn<bool>(
                name: "ShowFbrOnReceipt",
                table: "ReceiptTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowNtnOnReceipt",
                table: "ReceiptTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowFbrOnReceipt",
                table: "ReceiptTemplates");

            migrationBuilder.DropColumn(
                name: "ShowNtnOnReceipt",
                table: "ReceiptTemplates");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterText",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderText",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoPng",
                table: "ReceiptTemplates",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutletDisplayName",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrinterName",
                table: "ReceiptTemplates",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }
    }
}
