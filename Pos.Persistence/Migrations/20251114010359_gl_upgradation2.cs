using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class gl_upgradation2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TargetType",
                table: "Purchases",
                newName: "LocationType");

            migrationBuilder.AlterColumn<int>(
                name: "OutletId",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PurchasePayments");

            migrationBuilder.RenameColumn(
                name: "LocationType",
                table: "Purchases",
                newName: "TargetType");

            migrationBuilder.AlterColumn<int>(
                name: "OutletId",
                table: "PurchasePayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
