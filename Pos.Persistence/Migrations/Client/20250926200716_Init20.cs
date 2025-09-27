using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class Init20 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Outlets_OutletId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_IsActive",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_Name",
                table: "Warehouses");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "Warehouses",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Warehouses",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Warehouses",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Warehouses",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Warehouses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Warehouses",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Warehouses",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Warehouses",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Warehouses",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Warehouses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Warehouses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutletId1",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId1",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsActive_Name",
                table: "Warehouses",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_PublicId",
                table: "Warehouses",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_OutletId1",
                table: "Purchases",
                column: "OutletId1");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_WarehouseId1",
                table: "Purchases",
                column: "WarehouseId1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Purchase_Target",
                table: "Purchases",
                sql: "( [TargetType] = 1 AND [OutletId] IS NOT NULL AND [WarehouseId] IS NULL ) OR ( [TargetType] = 2 AND [WarehouseId] IS NOT NULL AND [OutletId] IS NULL )");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Outlets_OutletId",
                table: "Purchases",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Outlets_OutletId1",
                table: "Purchases",
                column: "OutletId1",
                principalTable: "Outlets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId",
                table: "Purchases",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId1",
                table: "Purchases",
                column: "WarehouseId1",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Outlets_OutletId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Outlets_OutletId1",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId1",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_IsActive_Name",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_PublicId",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_OutletId1",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_WarehouseId1",
                table: "Purchases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Purchase_Target",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "OutletId1",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "WarehouseId1",
                table: "Purchases");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsActive",
                table: "Warehouses",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Name",
                table: "Warehouses",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Outlets_OutletId",
                table: "Purchases",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Warehouses_WarehouseId",
                table: "Purchases",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }
    }
}
