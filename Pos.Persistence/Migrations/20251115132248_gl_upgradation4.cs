using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class gl_upgradation4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseLines_Purchases_PurchaseId1",
                table: "PurchaseLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseLines_PurchaseId1",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "PurchaseId1",
                table: "PurchaseLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseId1",
                table: "PurchaseLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_PurchaseId1",
                table: "PurchaseLines",
                column: "PurchaseId1");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseLines_Purchases_PurchaseId1",
                table: "PurchaseLines",
                column: "PurchaseId1",
                principalTable: "Purchases",
                principalColumn: "Id");
        }
    }
}
