using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class UserRoles_Init3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Counter_Outlets_OutletId",
                table: "Counter");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Counter",
                table: "Counter");

            migrationBuilder.RenameTable(
                name: "Counter",
                newName: "Counters");

            migrationBuilder.RenameIndex(
                name: "IX_Counter_OutletId",
                table: "Counters",
                newName: "IX_Counters_OutletId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Counters",
                table: "Counters",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Counters_Outlets_OutletId",
                table: "Counters",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Counters_Outlets_OutletId",
                table: "Counters");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Counters",
                table: "Counters");

            migrationBuilder.RenameTable(
                name: "Counters",
                newName: "Counter");

            migrationBuilder.RenameIndex(
                name: "IX_Counters_OutletId",
                table: "Counter",
                newName: "IX_Counter_OutletId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Counter",
                table: "Counter",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Counter_Outlets_OutletId",
                table: "Counter",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
