using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettings5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLocalizations");

            migrationBuilder.DropTable(
                name: "UserPreferences");

            migrationBuilder.DropTable(
                name: "InvoiceSettings");

            migrationBuilder.AlterColumn<string>(
                name: "DefaultBarcodeType",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "InvoiceSettingsLocals",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSettingsLocals_UpdatedAtUtc",
                table: "InvoiceSettingsLocals",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceSettingsLocals_UpdatedAtUtc",
                table: "InvoiceSettingsLocals");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "InvoiceSettingsLocals");

            migrationBuilder.AlterColumn<int>(
                name: "DefaultBarcodeType",
                table: "InvoiceSettingsLocals",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateTable(
                name: "InvoiceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AskToPrintOnSave = table.Column<bool>(type: "INTEGER", nullable: false),
                    BusinessNtn = table.Column<string>(type: "TEXT", nullable: true),
                    EnableDrawerKick = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableFbr = table.Column<bool>(type: "INTEGER", nullable: false),
                    FbrApiBaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    FbrAuthKey = table.Column<string>(type: "TEXT", nullable: true),
                    FbrPosId = table.Column<string>(type: "TEXT", nullable: true),
                    LogoAlignment = table.Column<string>(type: "TEXT", nullable: true),
                    LogoMaxWidthPx = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoPng = table.Column<byte[]>(type: "BLOB", nullable: true),
                    OutletDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PaperWidthMm = table.Column<int>(type: "INTEGER", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PrintBarcodeOnReceipt = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrintOnSave = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PurchaseBankAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    RowShowLineDiscount = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowShowLineTotal = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowShowProductName = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowShowProductSku = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowShowQty = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowShowUnitPrice = table.Column<bool>(type: "INTEGER", nullable: false),
                    SalesCardClearingAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShowAddress = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowBusinessName = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowBusinessNtn = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCashierOnReceipt = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowContacts = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCustomerOnReceipt = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowFbrQr = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowFooter = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowLogo = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowQr = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalsShowBalance = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalsShowDiscounts = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalsShowGrandTotal = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalsShowOtherExpenses = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalsShowPaymentRecv = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalsShowTaxes = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UseTill = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceSettings_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultBarcodeType = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayTimeZoneId = table.Column<string>(type: "TEXT", nullable: true),
                    MachineName = table.Column<string>(type: "TEXT", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseDestinationId = table.Column<int>(type: "INTEGER", nullable: true),
                    PurchaseDestinationScope = table.Column<string>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "randomblob(8)"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLocalizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceSettingsId = table.Column<int>(type: "INTEGER", nullable: false),
                    Footer = table.Column<string>(type: "TEXT", nullable: true),
                    Header = table.Column<string>(type: "TEXT", nullable: true),
                    Lang = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SaleReturnNote = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLocalizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLocalizations_InvoiceSettings_InvoiceSettingsId",
                        column: x => x.InvoiceSettingsId,
                        principalTable: "InvoiceSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLocalizations_InvoiceSettingsId_Lang",
                table: "InvoiceLocalizations",
                columns: new[] { "InvoiceSettingsId", "Lang" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSettings_OutletId",
                table: "InvoiceSettings",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_MachineName",
                table: "UserPreferences",
                column: "MachineName",
                unique: true);
        }
    }
}
