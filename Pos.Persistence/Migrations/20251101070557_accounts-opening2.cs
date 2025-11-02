using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class accountsopening2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpeningBalanceLine_Accounts_AccountId",
                table: "OpeningBalanceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_OpeningBalanceLine_OpeningBalance_OpeningBalanceId",
                table: "OpeningBalanceLine");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpeningBalanceLine",
                table: "OpeningBalanceLine");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpeningBalance",
                table: "OpeningBalance");

            migrationBuilder.RenameTable(
                name: "OpeningBalanceLine",
                newName: "OpeningBalanceLines");

            migrationBuilder.RenameTable(
                name: "OpeningBalance",
                newName: "OpeningBalances");

            migrationBuilder.RenameIndex(
                name: "IX_OpeningBalanceLine_OpeningBalanceId",
                table: "OpeningBalanceLines",
                newName: "IX_OpeningBalanceLines_OpeningBalanceId");

            migrationBuilder.RenameIndex(
                name: "IX_OpeningBalanceLine_AccountId",
                table: "OpeningBalanceLines",
                newName: "IX_OpeningBalanceLines_AccountId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpeningBalanceLines",
                table: "OpeningBalanceLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpeningBalances",
                table: "OpeningBalances",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningBalanceLines_Accounts_AccountId",
                table: "OpeningBalanceLines",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningBalanceLines_OpeningBalances_OpeningBalanceId",
                table: "OpeningBalanceLines",
                column: "OpeningBalanceId",
                principalTable: "OpeningBalances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpeningBalanceLines_Accounts_AccountId",
                table: "OpeningBalanceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OpeningBalanceLines_OpeningBalances_OpeningBalanceId",
                table: "OpeningBalanceLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpeningBalances",
                table: "OpeningBalances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpeningBalanceLines",
                table: "OpeningBalanceLines");

            migrationBuilder.RenameTable(
                name: "OpeningBalances",
                newName: "OpeningBalance");

            migrationBuilder.RenameTable(
                name: "OpeningBalanceLines",
                newName: "OpeningBalanceLine");

            migrationBuilder.RenameIndex(
                name: "IX_OpeningBalanceLines_OpeningBalanceId",
                table: "OpeningBalanceLine",
                newName: "IX_OpeningBalanceLine_OpeningBalanceId");

            migrationBuilder.RenameIndex(
                name: "IX_OpeningBalanceLines_AccountId",
                table: "OpeningBalanceLine",
                newName: "IX_OpeningBalanceLine_AccountId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpeningBalance",
                table: "OpeningBalance",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpeningBalanceLine",
                table: "OpeningBalanceLine",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningBalanceLine_Accounts_AccountId",
                table: "OpeningBalanceLine",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningBalanceLine_OpeningBalance_OpeningBalanceId",
                table: "OpeningBalanceLine",
                column: "OpeningBalanceId",
                principalTable: "OpeningBalance",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
