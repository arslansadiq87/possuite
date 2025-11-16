using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class gl_upgradationpin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "Users");
        }
    }
}
