using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    OutletDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PaperWidthMm = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableDrawerKick = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowQr = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCustomerOnReceipt = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCashierOnReceipt = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "InvoiceLocalizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceSettingsId = table.Column<int>(type: "INTEGER", nullable: false),
                    Lang = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Header = table.Column<string>(type: "TEXT", nullable: true),
                    Footer = table.Column<string>(type: "TEXT", nullable: true),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLocalizations");

            migrationBuilder.DropTable(
                name: "InvoiceSettings");
        }
    }
}
