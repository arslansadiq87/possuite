using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class PurchasesSuppliers_Init7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PaidAtUtc",
                table: "PurchasePayments",
                newName: "TsUtc");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "PurchasePayments",
                newName: "Note");

            migrationBuilder.AlterColumn<int>(
                name: "Method",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OutletId",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CashLedgers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: false),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: true),
                    TillSessionId = table.Column<int>(type: "INTEGER", nullable: true),
                    TsUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Delta = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefType = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashLedgers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayments_OutletId_TsUtc",
                table: "PurchasePayments",
                columns: new[] { "OutletId", "TsUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CashLedgers_OutletId_TsUtc",
                table: "CashLedgers",
                columns: new[] { "OutletId", "TsUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashLedgers");

            migrationBuilder.DropIndex(
                name: "IX_PurchasePayments_OutletId_TsUtc",
                table: "PurchasePayments");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PurchasePayments");

            migrationBuilder.DropColumn(
                name: "OutletId",
                table: "PurchasePayments");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "PurchasePayments");

            migrationBuilder.RenameColumn(
                name: "TsUtc",
                table: "PurchasePayments",
                newName: "PaidAtUtc");

            migrationBuilder.RenameColumn(
                name: "Note",
                table: "PurchasePayments",
                newName: "Notes");

            migrationBuilder.AlterColumn<string>(
                name: "Method",
                table: "PurchasePayments",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
