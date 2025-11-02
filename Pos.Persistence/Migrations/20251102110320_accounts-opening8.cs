using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class accountsopening8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentId",
                table: "Accounts");

            migrationBuilder.AddColumn<int>(
                name: "SystemKey",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Account_SystemKey_Outlet",
                table: "Accounts",
                columns: new[] { "SystemKey", "OutletId" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentId_Name",
                table: "Accounts",
                columns: new[] { "ParentId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Account_SystemKey_Outlet",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentId_Name",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SystemKey",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentId",
                table: "Accounts",
                column: "ParentId");
        }
    }
}
