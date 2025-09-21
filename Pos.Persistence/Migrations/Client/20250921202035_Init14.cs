using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class Init14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReturn",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RefPurchaseId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisedFromPurchaseId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisedToPurchaseId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefPurchaseLineId",
                table: "PurchaseLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_RefPurchaseId",
                table: "Purchases",
                column: "RefPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_RevisedFromPurchaseId",
                table: "Purchases",
                column: "RevisedFromPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_RevisedToPurchaseId",
                table: "Purchases",
                column: "RevisedToPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_RefPurchaseLineId",
                table: "PurchaseLines",
                column: "RefPurchaseLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseLines_PurchaseLines_RefPurchaseLineId",
                table: "PurchaseLines",
                column: "RefPurchaseLineId",
                principalTable: "PurchaseLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Purchases_RefPurchaseId",
                table: "Purchases",
                column: "RefPurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Purchases_RevisedFromPurchaseId",
                table: "Purchases",
                column: "RevisedFromPurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Purchases_RevisedToPurchaseId",
                table: "Purchases",
                column: "RevisedToPurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseLines_PurchaseLines_RefPurchaseLineId",
                table: "PurchaseLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Purchases_RefPurchaseId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Purchases_RevisedFromPurchaseId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Purchases_RevisedToPurchaseId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_RefPurchaseId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_RevisedFromPurchaseId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_RevisedToPurchaseId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseLines_RefPurchaseLineId",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "IsReturn",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RefPurchaseId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RevisedFromPurchaseId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RevisedToPurchaseId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RefPurchaseLineId",
                table: "PurchaseLines");
        }
    }
}
