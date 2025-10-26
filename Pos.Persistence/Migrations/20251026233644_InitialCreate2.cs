using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_Item_Loc",
                table: "StockEntries",
                columns: new[] { "ItemId", "LocationType", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_Ref",
                table: "StockEntries",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_Ts",
                table: "StockEntries",
                column: "Ts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockEntries_Item_Loc",
                table: "StockEntries");

            migrationBuilder.DropIndex(
                name: "IX_StockEntries_Ref",
                table: "StockEntries");

            migrationBuilder.DropIndex(
                name: "IX_StockEntries_Ts",
                table: "StockEntries");
        }
    }
}
