using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class PurchasesSuppliers_Init8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Counter_Outlet_OutletId",
                table: "Counter");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Outlet_OutletId",
                table: "Purchases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Outlet",
                table: "Outlet");

            migrationBuilder.RenameTable(
                name: "Outlet",
                newName: "Outlets");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Outlets",
                table: "Outlets",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Counter_Outlets_OutletId",
                table: "Counter",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Outlets_OutletId",
                table: "Purchases",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Counter_Outlets_OutletId",
                table: "Counter");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Outlets_OutletId",
                table: "Purchases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Outlets",
                table: "Outlets");

            migrationBuilder.RenameTable(
                name: "Outlets",
                newName: "Outlet");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Outlet",
                table: "Outlet",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Counter_Outlet_OutletId",
                table: "Counter",
                column: "OutletId",
                principalTable: "Outlet",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Outlet_OutletId",
                table: "Purchases",
                column: "OutletId",
                principalTable: "Outlet",
                principalColumn: "Id");
        }
    }
}
