using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class vouchers2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayTimeZoneId",
                table: "UserPreferences",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayTimeZoneId",
                table: "UserPreferences");
        }
    }
}
