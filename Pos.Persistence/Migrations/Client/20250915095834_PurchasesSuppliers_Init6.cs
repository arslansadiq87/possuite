using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class PurchasesSuppliers_Init6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OutletId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "CreditDue",
                table: "Purchases",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashPaid",
                table: "Purchases",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAtUtc",
                table: "Purchases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId1",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetType",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemId1",
                table: "PurchaseLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseId1",
                table: "PurchaseLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Outlet",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outlet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchasePayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PurchaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasePayments_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Counter",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Counter_Outlet_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlet",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_OutletId",
                table: "Purchases",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId",
                table: "Purchases",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId1",
                table: "Purchases",
                column: "SupplierId1");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_WarehouseId",
                table: "Purchases",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_ItemId1",
                table: "PurchaseLines",
                column: "ItemId1");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_PurchaseId1",
                table: "PurchaseLines",
                column: "PurchaseId1");

            migrationBuilder.CreateIndex(
                name: "IX_Counter_OutletId",
                table: "Counter",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayments_PurchaseId",
                table: "PurchasePayments",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsActive",
                table: "Warehouses",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Name",
                table: "Warehouses",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseLines_Items_ItemId",
                table: "PurchaseLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseLines_Items_ItemId1",
                table: "PurchaseLines",
                column: "ItemId1",
                principalTable: "Items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseLines_Purchases_PurchaseId1",
                table: "PurchaseLines",
                column: "PurchaseId1",
                principalTable: "Purchases",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Outlet_OutletId",
                table: "Purchases",
                column: "OutletId",
                principalTable: "Outlet",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId",
                table: "Purchases",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId1",
                table: "Purchases",
                column: "SupplierId1",
                principalTable: "Suppliers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId",
                table: "Purchases",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseLines_Items_ItemId",
                table: "PurchaseLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseLines_Items_ItemId1",
                table: "PurchaseLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseLines_Purchases_PurchaseId1",
                table: "PurchaseLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Outlet_OutletId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId1",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId",
                table: "Purchases");

            migrationBuilder.DropTable(
                name: "Counter");

            migrationBuilder.DropTable(
                name: "PurchasePayments");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Outlet");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_OutletId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_SupplierId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_SupplierId1",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_WarehouseId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseLines_ItemId1",
                table: "PurchaseLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseLines_PurchaseId1",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "SupplierId1",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ItemId1",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "PurchaseId1",
                table: "PurchaseLines");

            migrationBuilder.AlterColumn<int>(
                name: "OutletId",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CreditDue",
                table: "Purchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashPaid",
                table: "Purchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");
        }
    }
}
