using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettings7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AskBeforePrint",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "AutoPrintOnSave",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "CashDrawerKickEnabled",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "DefaultBarcodeType",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "DisplayTimeZoneId",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "EnableDailyBackup",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "EnableHourlyBackup",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "FooterSale",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "FooterSaleReturn",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "FooterVoucher",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "FooterZReport",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "PurchaseBankAccountId",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "SalesCardClearingAccountId",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "UseTill",
                table: "InvoiceSettingsLocals");

            migrationBuilder.CreateTable(
                name: "InvoiceSettingsScoped",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    CashDrawerKickEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoPrintOnSave = table.Column<bool>(type: "INTEGER", nullable: false),
                    AskBeforePrint = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayTimeZoneId = table.Column<string>(type: "TEXT", nullable: true),
                    SalesCardClearingAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    PurchaseBankAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    DefaultBarcodeType = table.Column<int>(type: "INTEGER", nullable: false),
                    FooterSale = table.Column<string>(type: "TEXT", nullable: true),
                    FooterSaleReturn = table.Column<string>(type: "TEXT", nullable: true),
                    FooterVoucher = table.Column<string>(type: "TEXT", nullable: true),
                    FooterZReport = table.Column<string>(type: "TEXT", nullable: true),
                    EnableDailyBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableHourlyBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseTill = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSettingsScoped", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSettingsScoped");

            migrationBuilder.AddColumn<bool>(
                name: "AskBeforePrint",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoPrintOnSave",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CashDrawerKickEnabled",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultBarcodeType",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DisplayTimeZoneId",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableDailyBackup",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableHourlyBackup",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FooterSale",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterSaleReturn",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterVoucher",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterZReport",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                maxLength: 2000,
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

            migrationBuilder.AddColumn<bool>(
                name: "UseTill",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
