using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class Init2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Warehouses",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Users",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "TillSessions",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "SupplierCredits",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "StockEntries",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "StockDocs",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Sales",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "SaleLines",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Purchases",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PurchasePayments",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PurchaseLines",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Products",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyRoles",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyOutlets",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyLedgers",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyBalances",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Parties",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Outlets",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Items",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "CounterSequences",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Counters",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "CounterBindings",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Categories",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "CashLedgers",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Brands",
                type: "BLOB",
                nullable: false,
                defaultValueSql: "X''",
                oldClrType: typeof(byte[]),
                oldType: "BLOB");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Warehouses",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Users",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "TillSessions",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "SupplierCredits",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "StockEntries",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "StockDocs",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Sales",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "SaleLines",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Purchases",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PurchasePayments",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PurchaseLines",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Products",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyRoles",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyOutlets",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyLedgers",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "PartyBalances",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Parties",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Outlets",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Items",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "CounterSequences",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Counters",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "CounterBindings",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Categories",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "CashLedgers",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Brands",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValueSql: "X''");
        }
    }
}
