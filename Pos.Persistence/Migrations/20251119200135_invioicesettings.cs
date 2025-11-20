using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceSettingsLocals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CashDrawerKickEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoPrintOnSave = table.Column<bool>(type: "INTEGER", nullable: false),
                    AskBeforePrint = table.Column<bool>(type: "INTEGER", nullable: false),
                    FooterSale = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FooterSaleReturn = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FooterVoucher = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FooterZReport = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSettingsLocals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSettingsLocals_CounterId",
                table: "InvoiceSettingsLocals",
                column: "CounterId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSettingsLocals");
        }
    }
}
