using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupBaseFolder",
                table: "InvoiceSettingsScoped",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseServerForBackupRestore",
                table: "InvoiceSettingsScoped",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupBaseFolder",
                table: "InvoiceSettingsScoped");

            migrationBuilder.DropColumn(
                name: "UseServerForBackupRestore",
                table: "InvoiceSettingsScoped");
        }
    }
}
